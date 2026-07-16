using System.Security.Claims;
using System.Text.Json;
using Application.DTOs;
using CarePro_Api.Controllers.Content;
using Domain.Entities;
using Domain.Settings;
using Infrastructure.Content.Data;
using Infrastructure.Content.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using MongoDB.EntityFrameworkCore.Extensions;
using Xunit;

namespace CommitmentGate.Tests;

public class ClientOnboardingApiTests
{
    [Fact]
    public async Task API_007_ResetState_NonProd_QAClient_Succeeds_WithBeforeAfterEvidence()
    {
        using var db = CreateDb();
        var clientId = "client-api-007";
        var controller = CreateController(db, clientId, "Client", isQaEnabled: true, environmentName: "Staging");

        var start = new PatchClientOnboardingWalkthroughRequest
        {
            ContentVersion = "2026-07-15",
            Version = 0,
            Action = "start",
            StepKey = "step_1_dashboard_orientation"
        };

        var startRes = await controller.PatchWalkthrough(start);
        var startOk = Assert.IsType<OkObjectResult>(startRes);
        var startedState = Assert.IsType<ClientOnboardingStateResponse>(startOk.Value);

        var complete = new PatchClientOnboardingWalkthroughRequest
        {
            ContentVersion = "2026-07-15",
            Version = startedState.Walkthrough.Version,
            Action = "complete_sequence"
        };

        var completeRes = await controller.PatchWalkthrough(complete);
        var completeOk = Assert.IsType<OkObjectResult>(completeRes);
        var completedState = Assert.IsType<ClientOnboardingStateResponse>(completeOk.Value);
        Assert.Equal(WalkthroughStatuses.Completed, completedState.Walkthrough.Status);

        var beforeResetStateRes = await controller.GetState();
        var beforeResetOk = Assert.IsType<OkObjectResult>(beforeResetStateRes);
        var beforeResetState = Assert.IsType<ClientOnboardingStateResponse>(beforeResetOk.Value);

        var resetRes = await controller.ResetState();
        var resetOk = Assert.IsType<OkObjectResult>(resetRes);
        var resetState = Assert.IsType<ClientOnboardingStateResponse>(resetOk.Value);

        Assert.Equal(WalkthroughStatuses.NotStarted, resetState.Walkthrough.Status);
        Assert.Null(resetState.Walkthrough.CurrentStep);
        Assert.Empty(resetState.Walkthrough.StepStates);
        Assert.Null(resetState.Walkthrough.StartedAt);
        Assert.Null(resetState.Walkthrough.CompletedAt);
        Assert.Null(resetState.Walkthrough.DismissedAt);

        Console.WriteLine($"API-007_BEFORE_RESET_STATE {JsonSerializer.Serialize(beforeResetState)}");
        Console.WriteLine($"API-007_AFTER_RESET_STATE {JsonSerializer.Serialize(resetState)}");
    }

    [Fact]
    public async Task API_008_ResetState_Production_ReturnsNotFound()
    {
        using var db = CreateDb();
        var controller = CreateController(db, "client-api-008", "Client", isQaEnabled: true, environmentName: "Production");

        var resetRes = await controller.ResetState();
        Assert.IsType<NotFoundResult>(resetRes);
        Console.WriteLine("API-008_RESET_PRODUCTION_STATUS 404");
    }

    [Fact]
    public async Task API_009_ResetState_NormalClientWithoutQaAccess_Forbid()
    {
        using var db = CreateDb();
        var controller = CreateController(db, "client-api-009", "Client", isQaEnabled: false, environmentName: "Development");

        var resetRes = await controller.ResetState();
        Assert.IsType<ForbidResult>(resetRes);
        Console.WriteLine("API-009_RESET_NORMAL_CLIENT_STATUS 403");
    }

    [Fact]
    public async Task API_006_FinalStep_ContinueOrSkip_SetsCurrentStepNull_AndCompleteSequenceSucceeds()
    {
        using var db = CreateDb();
        var clientIdContinue = "client-api-006-continue";
        var controllerContinue = CreateController(db, clientIdContinue, "Client");

        var startAtFinalStep = new PatchClientOnboardingWalkthroughRequest
        {
            ContentVersion = "2026-07-15",
            Version = 0,
            Action = "start",
            StepKey = "step_5_commitment_explainer_conditional"
        };

        var startContinueResult = await controllerContinue.PatchWalkthrough(startAtFinalStep);
        var startContinueOk = Assert.IsType<OkObjectResult>(startContinueResult);
        var startContinueState = Assert.IsType<ClientOnboardingStateResponse>(startContinueOk.Value);
        Assert.Equal("step_5_commitment_explainer_conditional", startContinueState.Walkthrough.CurrentStep);

        var continueFinalRequest = new PatchClientOnboardingWalkthroughRequest
        {
            ContentVersion = "2026-07-15",
            Version = 1,
            Action = "continue_step",
            StepKey = "step_5_commitment_explainer_conditional"
        };

        var continueFinalResult = await controllerContinue.PatchWalkthrough(continueFinalRequest);
        var continueFinalOk = Assert.IsType<OkObjectResult>(continueFinalResult);
        var continueFinalState = Assert.IsType<ClientOnboardingStateResponse>(continueFinalOk.Value);
        Assert.Null(continueFinalState.Walkthrough.CurrentStep);

        var completeAfterContinueRequest = new PatchClientOnboardingWalkthroughRequest
        {
            ContentVersion = "2026-07-15",
            Version = 2,
            Action = "complete_sequence",
            StepKey = null
        };

        var completeAfterContinueResult = await controllerContinue.PatchWalkthrough(completeAfterContinueRequest);
        var completeAfterContinueOk = Assert.IsType<OkObjectResult>(completeAfterContinueResult);
        var completeAfterContinueState = Assert.IsType<ClientOnboardingStateResponse>(completeAfterContinueOk.Value);
        Assert.Equal(WalkthroughStatuses.Completed, completeAfterContinueState.Walkthrough.Status);
        Assert.Null(completeAfterContinueState.Walkthrough.CurrentStep);

        var clientIdSkip = "client-api-006-skip";
        var controllerSkip = CreateController(db, clientIdSkip, "Client");

        var startSkipResult = await controllerSkip.PatchWalkthrough(startAtFinalStep);
        var startSkipOk = Assert.IsType<OkObjectResult>(startSkipResult);
        var startSkipState = Assert.IsType<ClientOnboardingStateResponse>(startSkipOk.Value);
        Assert.Equal("step_5_commitment_explainer_conditional", startSkipState.Walkthrough.CurrentStep);

        var skipFinalRequest = new PatchClientOnboardingWalkthroughRequest
        {
            ContentVersion = "2026-07-15",
            Version = 1,
            Action = "skip_step",
            StepKey = "step_5_commitment_explainer_conditional"
        };

        var skipFinalResult = await controllerSkip.PatchWalkthrough(skipFinalRequest);
        var skipFinalOk = Assert.IsType<OkObjectResult>(skipFinalResult);
        var skipFinalState = Assert.IsType<ClientOnboardingStateResponse>(skipFinalOk.Value);
        Assert.Null(skipFinalState.Walkthrough.CurrentStep);

        var completeAfterSkipRequest = new PatchClientOnboardingWalkthroughRequest
        {
            ContentVersion = "2026-07-15",
            Version = 2,
            Action = "complete_sequence",
            StepKey = null
        };

        var completeAfterSkipResult = await controllerSkip.PatchWalkthrough(completeAfterSkipRequest);
        var completeAfterSkipOk = Assert.IsType<OkObjectResult>(completeAfterSkipResult);
        var completeAfterSkipState = Assert.IsType<ClientOnboardingStateResponse>(completeAfterSkipOk.Value);
        Assert.Equal(WalkthroughStatuses.Completed, completeAfterSkipState.Walkthrough.Status);
        Assert.Null(completeAfterSkipState.Walkthrough.CurrentStep);

        Console.WriteLine($"API-006_CONTINUE_REQUEST {JsonSerializer.Serialize(continueFinalRequest)}");
        Console.WriteLine($"API-006_CONTINUE_RESPONSE {JsonSerializer.Serialize(continueFinalState)}");
        Console.WriteLine($"API-006_COMPLETE_AFTER_CONTINUE_REQUEST {JsonSerializer.Serialize(completeAfterContinueRequest)}");
        Console.WriteLine($"API-006_COMPLETE_AFTER_CONTINUE_RESPONSE {JsonSerializer.Serialize(completeAfterContinueState)}");
        Console.WriteLine($"API-006_SKIP_REQUEST {JsonSerializer.Serialize(skipFinalRequest)}");
        Console.WriteLine($"API-006_SKIP_RESPONSE {JsonSerializer.Serialize(skipFinalState)}");
        Console.WriteLine($"API-006_COMPLETE_AFTER_SKIP_REQUEST {JsonSerializer.Serialize(completeAfterSkipRequest)}");
        Console.WriteLine($"API-006_COMPLETE_AFTER_SKIP_RESPONSE {JsonSerializer.Serialize(completeAfterSkipState)}");
    }

    [Fact]
    public async Task API_004_StartThenContinueStep_AdvancesCurrentStep_WithStateEvidence()
    {
        using var db = CreateDb();
        var clientId = "client-api-004";
        var controller = CreateController(db, clientId, "Client");

        var startRequest = new PatchClientOnboardingWalkthroughRequest
        {
            ContentVersion = "2026-07-15",
            Version = 0,
            Action = "start",
            StepKey = "step_1_dashboard_orientation",
            Metadata = new ClientOnboardingMetadataDto
            {
                EligibleGigCount = 2,
                GateEnabledLive = true,
                Route = "/client/dashboard"
            }
        };

        var startResult = await controller.PatchWalkthrough(startRequest);
        var startOk = Assert.IsType<OkObjectResult>(startResult);
        var startState = Assert.IsType<ClientOnboardingStateResponse>(startOk.Value);
        Assert.Equal("step_1_dashboard_orientation", startState.Walkthrough.CurrentStep);

        var beforeContinueResult = await controller.GetState();
        var beforeContinueOk = Assert.IsType<OkObjectResult>(beforeContinueResult);
        var beforeContinueState = Assert.IsType<ClientOnboardingStateResponse>(beforeContinueOk.Value);

        var continueRequest = new PatchClientOnboardingWalkthroughRequest
        {
            ContentVersion = "2026-07-15",
            Version = 1,
            Action = "continue_step",
            StepKey = "step_1_dashboard_orientation",
            Reason = "next"
        };

        var continueResult = await controller.PatchWalkthrough(continueRequest);
        var continueOk = Assert.IsType<OkObjectResult>(continueResult);
        var continueState = Assert.IsType<ClientOnboardingStateResponse>(continueOk.Value);
        Assert.Equal("step_2_marketplace_search", continueState.Walkthrough.CurrentStep);

        var continueWithoutStepRequest = new PatchClientOnboardingWalkthroughRequest
        {
            ContentVersion = "2026-07-15",
            Version = 2,
            Action = "continue_step",
            StepKey = null,
            Reason = "next_without_step"
        };

        var continueWithoutStepResult = await controller.PatchWalkthrough(continueWithoutStepRequest);
        var continueWithoutStepOk = Assert.IsType<OkObjectResult>(continueWithoutStepResult);
        var continueWithoutStepState = Assert.IsType<ClientOnboardingStateResponse>(continueWithoutStepOk.Value);
        Assert.Equal("step_3_service_detail_actions", continueWithoutStepState.Walkthrough.CurrentStep);

        var afterContinueResult = await controller.GetState();
        var afterContinueOk = Assert.IsType<OkObjectResult>(afterContinueResult);
        var afterContinueState = Assert.IsType<ClientOnboardingStateResponse>(afterContinueOk.Value);

        Console.WriteLine($"API-004_REQUEST_START {JsonSerializer.Serialize(startRequest)}");
        Console.WriteLine($"API-004_RESPONSE_START {JsonSerializer.Serialize(startState)}");
        Console.WriteLine($"API-004_STATE_BEFORE_CONTINUE {JsonSerializer.Serialize(beforeContinueState)}");
        Console.WriteLine($"API-004_REQUEST_CONTINUE {JsonSerializer.Serialize(continueRequest)}");
        Console.WriteLine($"API-004_RESPONSE_CONTINUE {JsonSerializer.Serialize(continueState)}");
        Console.WriteLine($"API-004_REQUEST_CONTINUE_NO_STEP {JsonSerializer.Serialize(continueWithoutStepRequest)}");
        Console.WriteLine($"API-004_RESPONSE_CONTINUE_NO_STEP {JsonSerializer.Serialize(continueWithoutStepState)}");
        Console.WriteLine($"API-004_STATE_AFTER_CONTINUE {JsonSerializer.Serialize(afterContinueState)}");
    }

    [Fact]
    public async Task API_005_SkipAndAutoSkip_ActionsAdvanceToNextStep()
    {
        using var db = CreateDb();
        var clientId = "client-api-005";
        var controller = CreateController(db, clientId, "Client");

        var start = new PatchClientOnboardingWalkthroughRequest
        {
            ContentVersion = "2026-07-15",
            Version = 0,
            Action = "start",
            StepKey = "step_1_dashboard_orientation"
        };

        var startRes = await controller.PatchWalkthrough(start);
        var startOk = Assert.IsType<OkObjectResult>(startRes);
        var startState = Assert.IsType<ClientOnboardingStateResponse>(startOk.Value);
        Assert.Equal("step_1_dashboard_orientation", startState.Walkthrough.CurrentStep);

        var skip1 = new PatchClientOnboardingWalkthroughRequest
        {
            ContentVersion = "2026-07-15",
            Version = 1,
            Action = "skip_step",
            StepKey = "step_1_dashboard_orientation"
        };

        var skip1Res = await controller.PatchWalkthrough(skip1);
        var skip1Ok = Assert.IsType<OkObjectResult>(skip1Res);
        var skip1State = Assert.IsType<ClientOnboardingStateResponse>(skip1Ok.Value);
        Assert.Equal("step_2_marketplace_search", skip1State.Walkthrough.CurrentStep);

        var autoSkipEmpty = new PatchClientOnboardingWalkthroughRequest
        {
            ContentVersion = "2026-07-15",
            Version = 2,
            Action = "auto_skip_empty_marketplace",
            StepKey = "step_2_marketplace_search",
            Metadata = new ClientOnboardingMetadataDto
            {
                EligibleGigCount = 0,
                GateEnabledLive = true,
                Route = "/client/marketplace"
            }
        };

        var autoSkipEmptyRes = await controller.PatchWalkthrough(autoSkipEmpty);
        var autoSkipEmptyOk = Assert.IsType<OkObjectResult>(autoSkipEmptyRes);
        var autoSkipEmptyState = Assert.IsType<ClientOnboardingStateResponse>(autoSkipEmptyOk.Value);
        Assert.Equal("step_3_service_detail_actions", autoSkipEmptyState.Walkthrough.CurrentStep);

        var autoSkipGate = new PatchClientOnboardingWalkthroughRequest
        {
            ContentVersion = "2026-07-15",
            Version = 3,
            Action = "auto_skip_gate_inactive",
            StepKey = "step_3_service_detail_actions",
            Metadata = new ClientOnboardingMetadataDto
            {
                EligibleGigCount = 1,
                GateEnabledLive = false,
                Route = "/client/service"
            }
        };

        var autoSkipGateRes = await controller.PatchWalkthrough(autoSkipGate);
        var autoSkipGateOk = Assert.IsType<OkObjectResult>(autoSkipGateRes);
        var autoSkipGateState = Assert.IsType<ClientOnboardingStateResponse>(autoSkipGateOk.Value);
        Assert.Equal("step_4_checkout_basics", autoSkipGateState.Walkthrough.CurrentStep);

        var autoSkipNoEligible = new PatchClientOnboardingWalkthroughRequest
        {
            ContentVersion = "2026-07-15",
            Version = 4,
            Action = "auto_skip_no_eligible_gigs",
            StepKey = "step_4_checkout_basics",
            Metadata = new ClientOnboardingMetadataDto
            {
                EligibleGigCount = 0,
                GateEnabledLive = true,
                Route = "/client/checkout"
            }
        };

        var autoSkipNoEligibleRes = await controller.PatchWalkthrough(autoSkipNoEligible);
        var autoSkipNoEligibleOk = Assert.IsType<OkObjectResult>(autoSkipNoEligibleRes);
        var autoSkipNoEligibleState = Assert.IsType<ClientOnboardingStateResponse>(autoSkipNoEligibleOk.Value);
        Assert.Equal("step_5_commitment_explainer_conditional", autoSkipNoEligibleState.Walkthrough.CurrentStep);

        Console.WriteLine($"API-005_SKIP_RESPONSE {JsonSerializer.Serialize(skip1State)}");
        Console.WriteLine($"API-005_AUTOSKIP_EMPTY_RESPONSE {JsonSerializer.Serialize(autoSkipEmptyState)}");
        Console.WriteLine($"API-005_AUTOSKIP_GATE_RESPONSE {JsonSerializer.Serialize(autoSkipGateState)}");
        Console.WriteLine($"API-005_AUTOSKIP_NO_ELIGIBLE_RESPONSE {JsonSerializer.Serialize(autoSkipNoEligibleState)}");
    }

    [Fact]
    public async Task API_001_GetState_ReturnsDurableWalkthroughAndSeenTips_WithSnapshots()
    {
        using var db = CreateDb();

        var clientId = "client-api-001";
        db.ClientOnboardingWalkthroughs.Add(new ClientOnboardingWalkthrough
        {
            ClientId = clientId,
            ContentVersion = "2026-07-15",
            Status = WalkthroughStatuses.InProgress,
            CurrentStep = "step_1_dashboard_orientation",
            StepStates =
            [
                new ClientOnboardingStepState
                {
                    StepKey = "step_1_dashboard_orientation",
                    State = Domain.Entities.StepStates.Current,
                    UpdatedAt = DateTime.UtcNow.AddMinutes(-2)
                }
            ],
            StartedAt = DateTime.UtcNow.AddMinutes(-3),
            Version = 4,
            UpdatedAt = DateTime.UtcNow.AddMinutes(-1)
        });

        db.ClientOnboardingTipsSeen.Add(new ClientOnboardingTipSeen
        {
            ClientId = clientId,
            TipKey = "tip_chat_access_status",
            ContextEntries =
            [
                new ClientOnboardingContextEntry { Key = "route", Value = "/chat/abc" },
                new ClientOnboardingContextEntry { Key = "caregiverId", Value = "cg-123" }
            ],
            DisplayVariant = "access_required",
            FirstSeenAt = DateTime.UtcNow.AddMinutes(-10),
            LastSeenAt = DateTime.UtcNow.AddMinutes(-1),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-1)
        });

        await db.SaveChangesAsync();

        var dbBefore = await SnapshotClientStateAsync(db, clientId);
        var controller = CreateController(db, clientId, "Client");

        var result = await controller.GetState();
        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ClientOnboardingStateResponse>(ok.Value);

        Assert.Equal("2026-07-15", response.Walkthrough.ContentVersion);
        Assert.Equal("step_1_dashboard_orientation", response.Walkthrough.CurrentStep);
        Assert.Single(response.SeenTips);
        Assert.Equal("tip_chat_access_status", response.SeenTips[0].TipKey);

        var dbAfter = await SnapshotClientStateAsync(db, clientId);

        Console.WriteLine($"API-001_REQUEST {{\"method\":\"GET\",\"path\":\"/api/client-onboarding/state\"}}");
        Console.WriteLine($"API-001_RESPONSE {JsonSerializer.Serialize(response)}");
        Console.WriteLine($"API-001_DB_BEFORE {dbBefore}");
        Console.WriteLine($"API-001_DB_AFTER {dbAfter}");
    }

    [Fact]
    public async Task API_002_PatchWalkthrough_RejectsStaleVersion_WithLatestStateSnapshot()
    {
        using var db = CreateDb();
        var clientId = "client-api-002";

        var controller = CreateController(db, clientId, "Client");

        var initialRequest = new PatchClientOnboardingWalkthroughRequest
        {
            ContentVersion = "2026-07-15",
            Version = 0,
            Action = "start",
            StepKey = "step_1_dashboard_orientation",
            Metadata = new ClientOnboardingMetadataDto
            {
                EligibleGigCount = 3,
                GateEnabledLive = true,
                Route = "/client/dashboard"
            }
        };

        var initialResponse = await controller.PatchWalkthrough(initialRequest);
        if (initialResponse is BadRequestObjectResult badRequest)
        {
            throw new Xunit.Sdk.XunitException($"Unexpected bad request: {JsonSerializer.Serialize(badRequest.Value)}");
        }
        var initialOk = Assert.IsType<OkObjectResult>(initialResponse);
        var initialState = Assert.IsType<ClientOnboardingStateResponse>(initialOk.Value);
        Assert.Equal(1, initialState.Walkthrough.Version);

        var staleRequest = new PatchClientOnboardingWalkthroughRequest
        {
            ContentVersion = "2026-07-15",
            Version = 0,
            Action = "continue_step",
            StepKey = "step_2_marketplace_search",
            Reason = "frontend_stale_retry",
            Metadata = new ClientOnboardingMetadataDto
            {
                EligibleGigCount = 1,
                GateEnabledLive = true,
                Route = "/client/marketplace"
            }
        };

        var dbBeforeConflict = await SnapshotClientStateAsync(db, clientId);

        var conflictResult = await controller.PatchWalkthrough(staleRequest);
        var conflict = Assert.IsType<ConflictObjectResult>(conflictResult);
        var conflictBody = Assert.IsType<ClientOnboardingConflictResponse>(conflict.Value);

        Assert.Equal("VERSION_CONFLICT", conflictBody.ErrorCode);
        Assert.Equal(1, conflictBody.LatestState.Walkthrough.Version);
        Assert.Equal(WalkthroughStatuses.InProgress, conflictBody.LatestState.Walkthrough.Status);

        var dbAfterConflict = await SnapshotClientStateAsync(db, clientId);

        Console.WriteLine($"API-002_REQUEST_OK {JsonSerializer.Serialize(initialRequest)}");
        Console.WriteLine($"API-002_RESPONSE_OK {JsonSerializer.Serialize(initialState)}");
        Console.WriteLine($"API-002_REQUEST_CONFLICT {JsonSerializer.Serialize(staleRequest)}");
        Console.WriteLine($"API-002_RESPONSE_CONFLICT {JsonSerializer.Serialize(conflictBody)}");
        Console.WriteLine($"API-002_DB_BEFORE_CONFLICT {dbBeforeConflict}");
        Console.WriteLine($"API-002_DB_AFTER_CONFLICT {dbAfterConflict}");

        var persisted = await db.ClientOnboardingWalkthroughs
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ClientId == clientId && x.ContentVersion == "2026-07-15");
        Assert.NotNull(persisted);
        Assert.NotEmpty(persisted!.TransitionHistory);

        using var metadataDoc = JsonDocument.Parse(persisted.TransitionHistory[0].MetadataJson!);
        Assert.Equal(JsonValueKind.Number, metadataDoc.RootElement.GetProperty("eligibleGigCount").ValueKind);
        Assert.Equal(JsonValueKind.True, metadataDoc.RootElement.GetProperty("gateEnabledLive").ValueKind);
        Assert.Equal(JsonValueKind.String, metadataDoc.RootElement.GetProperty("route").ValueKind);

        Console.WriteLine($"API-002_PERSISTED_METADATA_JSON {persisted.TransitionHistory[0].MetadataJson}");
    }

    [Fact]
    public async Task API_003_PostTipSeen_IsIdempotent_AndPreservesFirstSeenAt()
    {
        using var db = CreateDb();
        var clientId = "client-api-003";

        var controller = CreateController(db, clientId, "Client");

        var firstRequest = new MarkClientOnboardingTipSeenRequest
        {
            TipKey = "tip_chat_access_status",
            DisplayVariant = "access_required",
            Context = new Dictionary<string, string>
            {
                ["route"] = "/chat/room-77",
                ["conversationId"] = "conv-77",
                ["caregiverId"] = "cg-909"
            }
        };

        var beforeFirst = await SnapshotClientStateAsync(db, clientId);
        var firstResult = await controller.MarkTipSeen(firstRequest);
        var firstOk = Assert.IsType<OkObjectResult>(firstResult);
        var firstResponse = Assert.IsType<ClientOnboardingTipSeenResponse>(firstOk.Value);

        var secondRequest = new MarkClientOnboardingTipSeenRequest
        {
            TipKey = "tip_chat_access_status",
            DisplayVariant = "access_granted",
            Context = new Dictionary<string, string>
            {
                ["route"] = "/chat/room-77",
                ["conversationId"] = "conv-77",
                ["caregiverId"] = "cg-909"
            }
        };

        var secondResult = await controller.MarkTipSeen(secondRequest);
        var secondOk = Assert.IsType<OkObjectResult>(secondResult);
        var secondResponse = Assert.IsType<ClientOnboardingTipSeenResponse>(secondOk.Value);

        Assert.Equal(firstResponse.FirstSeenAt, secondResponse.FirstSeenAt);
        Assert.True(secondResponse.LastSeenAt >= firstResponse.LastSeenAt);

        var records = await db.ClientOnboardingTipsSeen
            .Where(x => x.ClientId == clientId && x.TipKey == "tip_chat_access_status")
            .ToListAsync();

        Assert.Single(records);
        Assert.Equal("access_granted", records[0].DisplayVariant);

        var afterSecond = await SnapshotClientStateAsync(db, clientId);

        Console.WriteLine($"API-003_REQUEST_FIRST {JsonSerializer.Serialize(firstRequest)}");
        Console.WriteLine($"API-003_RESPONSE_FIRST {JsonSerializer.Serialize(firstResponse)}");
        Console.WriteLine($"API-003_REQUEST_SECOND {JsonSerializer.Serialize(secondRequest)}");
        Console.WriteLine($"API-003_RESPONSE_SECOND {JsonSerializer.Serialize(secondResponse)}");
        Console.WriteLine($"API-003_DB_BEFORE_FIRST {beforeFirst}");
        Console.WriteLine($"API-003_DB_AFTER_SECOND {afterSecond}");
    }

    private static ClientOnboardingController CreateController(
        CareProDbContext db,
        string userId,
        string role,
        bool isQaEnabled = false,
        string environmentName = "Development")
    {
        var settings = Options.Create(new ClientOnboardingSettings
        {
            CurrentContentVersion = "2026-07-15",
            SupportedContentVersions = ["2026-07-15"]
        });

        var service = new ClientOnboardingService(
            db,
            settings,
            LoggerFactory.Create(_ => { }).CreateLogger<ClientOnboardingService>());

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Role, role)
        };

        if (isQaEnabled)
        {
            claims.Add(new Claim(ClaimTypes.Role, "QA"));
            claims.Add(new Claim("qa_access", "true"));
        }

        var controller = new ClientOnboardingController(
            service,
            LoggerFactory.Create(_ => { }).CreateLogger<ClientOnboardingController>(),
            new FakeHostEnvironment(environmentName));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                claims,
                "TestAuth"))
            }
        };

        return controller;
    }

    private static CareProDbContext CreateDb()
    {
        var databaseName = $"cpo_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<CareProDbContext>()
            .UseMongoDB("mongodb://127.0.0.1:27018", databaseName)
            .Options;

        return new TestCareProDbContext(options);
    }

    private static async Task<string> SnapshotClientStateAsync(CareProDbContext db, string clientId)
    {
        var walkthrough = await db.ClientOnboardingWalkthroughs
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ClientId == clientId);

        var tips = await db.ClientOnboardingTipsSeen
            .AsNoTracking()
            .Where(x => x.ClientId == clientId)
            .OrderBy(x => x.TipKey)
            .ToListAsync();

        return JsonSerializer.Serialize(new
        {
            walkthrough,
            tips
        });
    }
}

internal sealed class FakeHostEnvironment : IHostEnvironment
{
    public FakeHostEnvironment(string environmentName)
    {
        EnvironmentName = environmentName;
        ApplicationName = "CarePro-Api.Tests";
        ContentRootPath = AppContext.BaseDirectory;
    }

    public string EnvironmentName { get; set; }
    public string ApplicationName { get; set; }
    public string ContentRootPath { get; set; }
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
        new Microsoft.Extensions.FileProviders.PhysicalFileProvider(AppContext.BaseDirectory);
}