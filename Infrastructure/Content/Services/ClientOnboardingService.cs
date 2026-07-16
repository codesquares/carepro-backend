using Application.DTOs;
using Application.Interfaces.Content;
using Domain.Entities;
using Domain.Settings;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Infrastructure.Content.Services
{
    public class ClientOnboardingService : IClientOnboardingService
    {
        private readonly CareProDbContext _dbContext;
        private readonly ClientOnboardingSettings _settings;
        private readonly ILogger<ClientOnboardingService> _logger;

        private const string ActionStart = "start";
        private const string ActionContinueStep = "continue_step";
        private const string ActionSkipStep = "skip_step";
        private const string ActionDismissAll = "dismiss_all";
        private const string ActionAutoSkipEmptyMarketplace = "auto_skip_empty_marketplace";
        private const string ActionAutoSkipGateInactive = "auto_skip_gate_inactive";
        private const string ActionAutoSkipNoEligibleGigs = "auto_skip_no_eligible_gigs";
        private const string ActionPause = "pause";
        private const string ActionResume = "resume";
        private const string ActionCompleteSequence = "complete_sequence";

        private static readonly HashSet<string> AllowedActions =
        [
            ActionStart,
            ActionContinueStep,
            ActionSkipStep,
            ActionDismissAll,
            ActionAutoSkipEmptyMarketplace,
            ActionAutoSkipGateInactive,
            ActionAutoSkipNoEligibleGigs,
            ActionPause,
            ActionResume,
            ActionCompleteSequence
        ];

        private static readonly List<string> OrderedStepKeys =
        [
            "step_1_dashboard_orientation",
            "step_2_marketplace_search",
            "step_3_service_detail_actions",
            "step_4_checkout_basics",
            "step_5_commitment_explainer_conditional"
        ];

        private static readonly HashSet<string> AllowedStepKeys =
            OrderedStepKeys.ToHashSet(StringComparer.Ordinal);

        public ClientOnboardingService(
            CareProDbContext dbContext,
            IOptions<ClientOnboardingSettings> settings,
            ILogger<ClientOnboardingService> logger)
        {
            _dbContext = dbContext;
            _settings = settings.Value;
            _logger = logger;
        }

        public async Task<ClientOnboardingStateResponse> GetStateAsync(string clientId)
        {
            var contentVersion = _settings.CurrentContentVersion;
            var walkthrough = await _dbContext.ClientOnboardingWalkthroughs
                .FirstOrDefaultAsync(x => x.ClientId == clientId && x.ContentVersion == contentVersion);

            var tips = await _dbContext.ClientOnboardingTipsSeen
                .Where(x => x.ClientId == clientId)
                .OrderBy(x => x.LastSeenAt)
                .ToListAsync();

            walkthrough ??= CreateDefaultWalkthrough(clientId, contentVersion);
            return MapStateResponse(walkthrough, tips);
        }

        public async Task<ClientOnboardingPatchResult> PatchWalkthroughAsync(string clientId, PatchClientOnboardingWalkthroughRequest request)
        {
            ValidatePatchRequest(request);

            var walkthrough = await _dbContext.ClientOnboardingWalkthroughs
                .FirstOrDefaultAsync(x => x.ClientId == clientId && x.ContentVersion == request.ContentVersion);

            var isNew = walkthrough == null;
            walkthrough ??= CreateDefaultWalkthrough(clientId, request.ContentVersion);

            if (request.Version != walkthrough.Version)
            {
                var conflictState = await GetStateByVersionAsync(clientId, request.ContentVersion);
                return new ClientOnboardingPatchResult
                {
                    IsConflict = true,
                    ErrorCode = "VERSION_CONFLICT",
                    ErrorMessage = "Submitted version does not match the latest onboarding state.",
                    State = conflictState
                };
            }

            ApplyTransition(walkthrough, request.Action.Trim(), request.StepKey);

            walkthrough.TransitionHistory.Add(new ClientOnboardingTransitionRecord
            {
                Action = request.Action.Trim(),
                StepKey = request.StepKey,
                Reason = request.Reason,
                MetadataJson = request.Metadata == null ? null : JsonSerializer.Serialize(request.Metadata),
                PreviousVersion = request.Version,
                OccurredAt = DateTime.UtcNow
            });

            walkthrough.Version += 1;
            walkthrough.UpdatedAt = DateTime.UtcNow;

            if (isNew)
            {
                await _dbContext.ClientOnboardingWalkthroughs.AddAsync(walkthrough);
            }
            else
            {
                _dbContext.ClientOnboardingWalkthroughs.Update(walkthrough);
            }

            await _dbContext.SaveChangesAsync();

            var state = await GetStateAsync(clientId);
            return new ClientOnboardingPatchResult
            {
                IsConflict = false,
                State = state
            };
        }

        public async Task<ClientOnboardingTipSeenResponse> MarkTipSeenAsync(string clientId, MarkClientOnboardingTipSeenRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.TipKey))
            {
                throw new ArgumentException("tipKey is required.");
            }

            if (request.Context == null)
            {
                throw new ArgumentException("context is required.");
            }

            var now = DateTime.UtcNow;
            var existing = await _dbContext.ClientOnboardingTipsSeen
                .FirstOrDefaultAsync(x => x.ClientId == clientId && x.TipKey == request.TipKey);

            if (existing == null)
            {
                existing = new ClientOnboardingTipSeen
                {
                    ClientId = clientId,
                    TipKey = request.TipKey,
                    ContextEntries = ToContextEntries(request.Context),
                    DisplayVariant = request.DisplayVariant,
                    FirstSeenAt = now,
                    LastSeenAt = now,
                    UpdatedAt = now
                };

                await _dbContext.ClientOnboardingTipsSeen.AddAsync(existing);
            }
            else
            {
                existing.ContextEntries = ToContextEntries(request.Context);
                existing.DisplayVariant = request.DisplayVariant;
                existing.LastSeenAt = now;
                existing.UpdatedAt = now;
                _dbContext.ClientOnboardingTipsSeen.Update(existing);
            }

            await _dbContext.SaveChangesAsync();

            return new ClientOnboardingTipSeenResponse
            {
                TipKey = existing.TipKey,
                Context = ToDictionary(existing.ContextEntries),
                DisplayVariant = existing.DisplayVariant,
                FirstSeenAt = existing.FirstSeenAt,
                LastSeenAt = existing.LastSeenAt
            };
        }

        public async Task<ClientOnboardingStateResponse> ResetStateAsync(string clientId)
        {
            var contentVersion = _settings.CurrentContentVersion;
            var walkthrough = await _dbContext.ClientOnboardingWalkthroughs
                .FirstOrDefaultAsync(x => x.ClientId == clientId && x.ContentVersion == contentVersion);

            var isNew = walkthrough == null;
            walkthrough ??= CreateDefaultWalkthrough(clientId, contentVersion);

            var previousVersion = walkthrough.Version;

            walkthrough.Status = WalkthroughStatuses.NotStarted;
            walkthrough.CurrentStep = null;
            walkthrough.StepStates = new List<ClientOnboardingStepState>();
            walkthrough.StartedAt = null;
            walkthrough.CompletedAt = null;
            walkthrough.DismissedAt = null;
            walkthrough.Version += 1;
            walkthrough.UpdatedAt = DateTime.UtcNow;

            walkthrough.TransitionHistory.Add(new ClientOnboardingTransitionRecord
            {
                Action = "reset_state",
                StepKey = null,
                Reason = "qa_reset",
                MetadataJson = null,
                PreviousVersion = previousVersion,
                OccurredAt = DateTime.UtcNow
            });

            if (isNew)
            {
                await _dbContext.ClientOnboardingWalkthroughs.AddAsync(walkthrough);
            }
            else
            {
                _dbContext.ClientOnboardingWalkthroughs.Update(walkthrough);
            }

            await _dbContext.SaveChangesAsync();
            return await GetStateAsync(clientId);
        }

        private void ValidatePatchRequest(PatchClientOnboardingWalkthroughRequest request)
        {
            if (request == null)
            {
                throw new ArgumentException("Request body is required.");
            }

            if (string.IsNullOrWhiteSpace(request.ContentVersion))
            {
                throw new ArgumentException("contentVersion is required.");
            }

            if (!GetSupportedContentVersions().Contains(request.ContentVersion))
            {
                throw new ArgumentException("Unsupported contentVersion.");
            }

            if (string.IsNullOrWhiteSpace(request.Action))
            {
                throw new ArgumentException("action is required.");
            }

            var action = request.Action.Trim();
            if (!AllowedActions.Contains(action))
            {
                throw new ArgumentException("Invalid action.");
            }

            var requiresStep = ActionRequiresStepKey(action);
            var forbidsStep = ActionForbidsStepKey(action);

            if (requiresStep && string.IsNullOrWhiteSpace(request.StepKey))
            {
                throw new ArgumentException("stepKey is required for this action.");
            }

            if (forbidsStep && !string.IsNullOrWhiteSpace(request.StepKey))
            {
                throw new ArgumentException("stepKey is not allowed for this action.");
            }

            if (!string.IsNullOrWhiteSpace(request.StepKey) && !AllowedStepKeys.Contains(request.StepKey!))
            {
                throw new ArgumentException("Invalid stepKey.");
            }
        }

        private void ApplyTransition(ClientOnboardingWalkthrough walkthrough, string action, string? stepKey)
        {
            switch (action)
            {
                case ActionStart:
                    ApplyStart(walkthrough, stepKey!);
                    return;
                case ActionContinueStep:
                    ApplyContinueStep(walkthrough, stepKey);
                    return;
                case ActionSkipStep:
                    ApplySkipStep(walkthrough, stepKey);
                    return;
                case ActionAutoSkipEmptyMarketplace:
                case ActionAutoSkipGateInactive:
                case ActionAutoSkipNoEligibleGigs:
                    ApplyAutoSkipStep(walkthrough, stepKey);
                    return;
                case ActionDismissAll:
                    ApplyDismissAll(walkthrough);
                    return;
                case ActionPause:
                    ApplyPause(walkthrough);
                    return;
                case ActionResume:
                    ApplyResume(walkthrough);
                    return;
                case ActionCompleteSequence:
                    ApplyCompleteSequence(walkthrough);
                    return;
                default:
                    throw new ArgumentException("Invalid action.");
            }
        }

        private void ApplyStart(ClientOnboardingWalkthrough walkthrough, string stepKey)
        {
            if (walkthrough.Status != WalkthroughStatuses.NotStarted)
            {
                throw new InvalidOperationException("Invalid step transition for action start.");
            }

            var now = DateTime.UtcNow;
            walkthrough.Status = WalkthroughStatuses.InProgress;
            walkthrough.StartedAt ??= now;
            walkthrough.CompletedAt = null;
            walkthrough.DismissedAt = null;
            UpsertCurrentStep(walkthrough, stepKey, now);
        }

        private void ApplyContinueStep(ClientOnboardingWalkthrough walkthrough, string? stepKey)
        {
            if (walkthrough.Status != WalkthroughStatuses.InProgress)
            {
                throw new InvalidOperationException("Invalid step transition for action continue_step.");
            }

            var now = DateTime.UtcNow;
            var effectiveStepKey = ResolveEffectiveStepKey(walkthrough, stepKey, "continue_step");
            var currentStep = GetOrAddStepState(walkthrough, effectiveStepKey, now);

            if (currentStep.State == StepStates.Completed || currentStep.State == StepStates.Skipped)
            {
                throw new InvalidOperationException("Invalid step transition for action continue_step.");
            }

            currentStep.State = StepStates.Completed;
            currentStep.UpdatedAt = now;

            AdvanceToNextStep(walkthrough, effectiveStepKey, now);
        }

        private void ApplySkipStep(ClientOnboardingWalkthrough walkthrough, string? stepKey)
        {
            if (walkthrough.Status != WalkthroughStatuses.InProgress)
            {
                throw new InvalidOperationException("Invalid step transition for action skip_step.");
            }

            var effectiveStepKey = ResolveEffectiveStepKey(walkthrough, stepKey, "skip_step");
            MarkStepSkippedAndAdvance(walkthrough, effectiveStepKey);
        }

        private void ApplyAutoSkipStep(ClientOnboardingWalkthrough walkthrough, string? stepKey)
        {
            if (walkthrough.Status != WalkthroughStatuses.InProgress)
            {
                throw new InvalidOperationException("Invalid step transition for auto skip action.");
            }

            var effectiveStepKey = ResolveEffectiveStepKey(walkthrough, stepKey, "auto_skip");
            MarkStepSkippedAndAdvance(walkthrough, effectiveStepKey);
        }

        private void ApplyDismissAll(ClientOnboardingWalkthrough walkthrough)
        {
            if (walkthrough.Status == WalkthroughStatuses.Completed)
            {
                throw new InvalidOperationException("Invalid step transition for action dismiss_all.");
            }

            walkthrough.Status = WalkthroughStatuses.Dismissed;
            walkthrough.CurrentStep = null;
            walkthrough.DismissedAt = DateTime.UtcNow;
        }

        private void ApplyPause(ClientOnboardingWalkthrough walkthrough)
        {
            if (walkthrough.Status != WalkthroughStatuses.InProgress)
            {
                throw new InvalidOperationException("Invalid step transition for action pause.");
            }

            walkthrough.Status = WalkthroughStatuses.Paused;
        }

        private void ApplyResume(ClientOnboardingWalkthrough walkthrough)
        {
            if (walkthrough.Status != WalkthroughStatuses.Paused)
            {
                throw new InvalidOperationException("Invalid step transition for action resume.");
            }

            walkthrough.Status = WalkthroughStatuses.InProgress;
            walkthrough.StartedAt ??= DateTime.UtcNow;
        }

        private void ApplyCompleteSequence(ClientOnboardingWalkthrough walkthrough)
        {
            if (walkthrough.Status != WalkthroughStatuses.InProgress)
            {
                throw new InvalidOperationException("Invalid step transition for action complete_sequence.");
            }

            var now = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(walkthrough.CurrentStep))
            {
                var step = GetOrAddStepState(walkthrough, walkthrough.CurrentStep, now);
                step.State = StepStates.Completed;
                step.UpdatedAt = now;
            }

            walkthrough.Status = WalkthroughStatuses.Completed;
            walkthrough.CompletedAt = now;
            walkthrough.CurrentStep = null;
        }

        private void MarkStepSkippedAndAdvance(ClientOnboardingWalkthrough walkthrough, string stepKey)
        {
            var now = DateTime.UtcNow;
            var step = GetOrAddStepState(walkthrough, stepKey, now);
            if (step.State == StepStates.Completed)
            {
                throw new InvalidOperationException("Invalid step transition for skip action.");
            }

            step.State = StepStates.Skipped;
            step.UpdatedAt = now;

            AdvanceToNextStep(walkthrough, stepKey, now);
        }

        private string ResolveEffectiveStepKey(ClientOnboardingWalkthrough walkthrough, string? requestedStepKey, string action)
        {
            if (!string.IsNullOrWhiteSpace(requestedStepKey))
            {
                return requestedStepKey;
            }

            if (!string.IsNullOrWhiteSpace(walkthrough.CurrentStep))
            {
                return walkthrough.CurrentStep;
            }

            throw new InvalidOperationException($"Invalid step transition for action {action}.");
        }

        private void AdvanceToNextStep(ClientOnboardingWalkthrough walkthrough, string fromStepKey, DateTime now)
        {
            var index = OrderedStepKeys.IndexOf(fromStepKey);
            if (index < 0)
            {
                throw new InvalidOperationException("Invalid step transition due to unknown step order.");
            }

            for (var i = index + 1; i < OrderedStepKeys.Count; i++)
            {
                var candidateKey = OrderedStepKeys[i];
                var candidate = GetOrAddStepState(walkthrough, candidateKey, now);

                if (candidate.State == StepStates.Completed || candidate.State == StepStates.Skipped)
                {
                    continue;
                }

                UpsertCurrentStep(walkthrough, candidateKey, now);
                return;
            }

            walkthrough.CurrentStep = null;
        }

        private static void UpsertCurrentStep(ClientOnboardingWalkthrough walkthrough, string stepKey, DateTime now)
        {
            var current = walkthrough.StepStates.FirstOrDefault(x => x.State == StepStates.Current && x.StepKey != stepKey);
            if (current != null)
            {
                current.State = StepStates.Pending;
                current.UpdatedAt = now;
            }

            var step = GetOrAddStepState(walkthrough, stepKey, now);
            if (step.State == StepStates.Completed || step.State == StepStates.Skipped)
            {
                throw new InvalidOperationException("Invalid step transition for continue_step target.");
            }

            step.State = StepStates.Current;
            step.UpdatedAt = now;
            walkthrough.CurrentStep = stepKey;
        }

        private static ClientOnboardingStepState GetOrAddStepState(ClientOnboardingWalkthrough walkthrough, string stepKey, DateTime now)
        {
            var step = walkthrough.StepStates.FirstOrDefault(x => x.StepKey == stepKey);
            if (step != null)
            {
                return step;
            }

            step = new ClientOnboardingStepState
            {
                StepKey = stepKey,
                State = StepStates.Pending,
                UpdatedAt = now
            };
            walkthrough.StepStates.Add(step);
            return step;
        }

        private static bool ActionRequiresStepKey(string action)
        {
            return action == ActionStart
                || action == ActionAutoSkipEmptyMarketplace
                || action == ActionAutoSkipGateInactive
                || action == ActionAutoSkipNoEligibleGigs;
        }

        private static bool ActionForbidsStepKey(string action)
        {
            return action == ActionDismissAll
                || action == ActionPause
                || action == ActionResume
                || action == ActionCompleteSequence;
        }

        private async Task<ClientOnboardingStateResponse> GetStateByVersionAsync(string clientId, string contentVersion)
        {
            var walkthrough = await _dbContext.ClientOnboardingWalkthroughs
                .FirstOrDefaultAsync(x => x.ClientId == clientId && x.ContentVersion == contentVersion)
                ?? CreateDefaultWalkthrough(clientId, contentVersion);

            var tips = await _dbContext.ClientOnboardingTipsSeen
                .Where(x => x.ClientId == clientId)
                .OrderBy(x => x.LastSeenAt)
                .ToListAsync();

            return MapStateResponse(walkthrough, tips);
        }

        private HashSet<string> GetSupportedContentVersions()
        {
            var configured = _settings.SupportedContentVersions
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (configured.Count == 0)
            {
                configured.Add(_settings.CurrentContentVersion);
            }

            return configured;
        }

        private ClientOnboardingWalkthrough CreateDefaultWalkthrough(string clientId, string contentVersion)
        {
            return new ClientOnboardingWalkthrough
            {
                ClientId = clientId,
            ContentVersion = contentVersion,
                Status = WalkthroughStatuses.NotStarted,
                CurrentStep = null,
                Version = 0,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
        }

        private static ClientOnboardingStateResponse MapStateResponse(
            ClientOnboardingWalkthrough walkthrough,
            List<ClientOnboardingTipSeen> tips)
        {
            return new ClientOnboardingStateResponse
            {
                Walkthrough = new ClientOnboardingWalkthroughStateDto
                {
                    ContentVersion = walkthrough.ContentVersion,
                    Status = walkthrough.Status,
                    CurrentStep = walkthrough.CurrentStep,
                    StepStates = walkthrough.StepStates
                        .Select(x => new ClientOnboardingStepStateDto
                        {
                            StepKey = x.StepKey,
                            State = x.State,
                            UpdatedAt = x.UpdatedAt
                        })
                        .ToList(),
                    StartedAt = walkthrough.StartedAt,
                    CompletedAt = walkthrough.CompletedAt,
                    DismissedAt = walkthrough.DismissedAt,
                    Version = walkthrough.Version
                },
                SeenTips = tips.Select(x => new ClientOnboardingSeenTipDto
                {
                    TipKey = x.TipKey,
                    SeenAt = x.LastSeenAt,
                    Context = ToDictionary(x.ContextEntries),
                    DisplayVariant = x.DisplayVariant,
                    FirstSeenAt = x.FirstSeenAt,
                    LastSeenAt = x.LastSeenAt
                }).ToList()
            };
        }

        private static Dictionary<string, string> ToDictionary(List<ClientOnboardingContextEntry>? entries)
        {
            if (entries == null || entries.Count == 0)
            {
                return new Dictionary<string, string>();
            }

            return entries
                .GroupBy(x => x.Key)
                .ToDictionary(g => g.Key, g => g.Last().Value);
        }

        private static List<ClientOnboardingContextEntry> ToContextEntries(Dictionary<string, string>? context)
        {
            if (context == null || context.Count == 0)
            {
                return new List<ClientOnboardingContextEntry>();
            }

            return context.Select(kvp => new ClientOnboardingContextEntry
            {
                Key = kvp.Key,
                Value = kvp.Value
            }).ToList();
        }

    }
}