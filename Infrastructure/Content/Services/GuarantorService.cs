using Application.Commands;
using Application.DTOs;
using Application.Interfaces.Authentication;
using Application.Interfaces.Common;
using Application.Interfaces.Content;
using Application.Interfaces.Email;
using Domain.Entities;
using Infrastructure.Content.Data;
using Infrastructure.Content.Helpers;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;

namespace Infrastructure.Content.Services
{
    /// <summary>
    /// Phase 2 vetting: caregiver guarantors. Cardinality (exactly two) is enforced
    /// here, not in the DB. Confirmation is self-serve via an emailed signed link
    /// (JWT "guarantor_confirmation" purpose claim). Attempt-cap + cooldown gating
    /// mirrors <see cref="VerificationService"/>.
    /// </summary>
    public class GuarantorService : IGuarantorService
    {
        private readonly CareProDbContext db;
        private readonly ITokenHandler tokenHandler;
        private readonly IEmailService emailService;
        private readonly IConfiguration configuration;
        private readonly IOriginValidationService originValidation;
        private readonly ILogger<GuarantorService> logger;
        private readonly IOpsAlertService opsAlerts;
        private readonly IMediator mediator;

        // Defaults mirror VerificationService's gate; a resend cooldown rather than
        // a failure cooldown since there is no "failed" state for a guarantor.
        private const int MaxConfirmationSendAttempts = 5;
        private const int ResendCooldownMinutes = 60;

        public GuarantorService(
            CareProDbContext db,
            ITokenHandler tokenHandler,
            IEmailService emailService,
            IConfiguration configuration,
            IOriginValidationService originValidation,
            ILogger<GuarantorService> logger,
            IOpsAlertService opsAlerts,
            IMediator mediator)
        {
            this.db = db;
            this.tokenHandler = tokenHandler;
            this.emailService = emailService;
            this.configuration = configuration;
            this.originValidation = originValidation;
            this.logger = logger;
            this.opsAlerts = opsAlerts;
            this.mediator = mediator;
        }

        // ───────────────────────── Helpers ─────────────────────────

        private static ObjectId ParseObjectId(string id, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(id) || !ObjectId.TryParse(id, out var oid))
                throw new ArgumentException($"Invalid {fieldName} '{id}'.");
            return oid;
        }

        private async Task<Caregiver> RequireCaregiverAsync(string caregiverId)
        {
            var oid = ParseObjectId(caregiverId, "caregiver id");
            return await db.CareGivers.FirstOrDefaultAsync(c => c.Id == oid)
                ?? throw new KeyNotFoundException($"Caregiver '{caregiverId}' not found.");
        }

        private async Task<Guarantor> RequireOwnedGuarantorAsync(string caregiverId, string guarantorId)
        {
            var oid = ParseObjectId(guarantorId, "guarantor id");
            var guarantor = await db.Guarantors.FirstOrDefaultAsync(g => g.Id == oid)
                ?? throw new KeyNotFoundException($"Guarantor '{guarantorId}' not found.");
            if (!string.Equals(guarantor.CaregiverId, caregiverId, StringComparison.Ordinal))
                throw new UnauthorizedAccessException("You are not authorised to access this guarantor.");
            return guarantor;
        }

        private static GuarantorResponse Map(Guarantor g) => new()
        {
            Id = g.Id.ToString(),
            CaregiverId = g.CaregiverId,
            Name = g.Name,
            RelationshipToCaregiver = g.RelationshipToCaregiver,
            RelationshipCategory = g.RelationshipCategory,
            YearsKnown = g.YearsKnown,
            PhoneNo = g.PhoneNo,
            Email = g.Email,
            Address = g.Address,
            Status = g.Status,
            VerifiedAt = g.VerifiedAt,
            ConfirmationMethod = g.ConfirmationMethod,
            ConfirmedByAdminId = g.ConfirmedByAdminId,
            ConfirmedByAdminEmail = g.ConfirmedByAdminEmail,
            AttemptCount = g.AttemptCount ?? 0,
            LastAttemptAt = g.LastAttemptAt,
            CooldownUntil = g.CooldownUntil,
            CreatedAt = g.CreatedAt,
            UpdatedOn = g.UpdatedOn,
        };

        private static AdminGuarantorResponse MapAdmin(Guarantor g, string caregiverName)
        {
            var b = Map(g);
            return new AdminGuarantorResponse
            {
                Id = b.Id, CaregiverId = b.CaregiverId, Name = b.Name,
                RelationshipToCaregiver = b.RelationshipToCaregiver,
                RelationshipCategory = b.RelationshipCategory, YearsKnown = b.YearsKnown,
                PhoneNo = b.PhoneNo, Email = b.Email, Address = b.Address, Status = b.Status,
                VerifiedAt = b.VerifiedAt, ConfirmationMethod = b.ConfirmationMethod,
                ConfirmedByAdminId = b.ConfirmedByAdminId, ConfirmedByAdminEmail = b.ConfirmedByAdminEmail,
                AttemptCount = b.AttemptCount, LastAttemptAt = b.LastAttemptAt, CooldownUntil = b.CooldownUntil,
                CreatedAt = b.CreatedAt, UpdatedOn = b.UpdatedOn,
                CaregiverName = caregiverName,
                ResponsesSubmittedAt = g.ResponsesSubmittedAt,
                RespCaregiverName = g.RespCaregiverName,
                RespRelationshipCategory = g.RespRelationshipCategory,
                RespYearsKnown = g.RespYearsKnown,
                RespLastContact = g.RespLastContact,
                RespCaregiverArea = g.RespCaregiverArea,
                RespHowTheyKnow = g.RespHowTheyKnow,
                RespTrustAnswer = g.RespTrustAnswer,
                RespTrustExplanation = g.RespTrustExplanation,
                RespPhoneNo = g.RespPhoneNo,
                ReviewFlags = g.ReviewFlags,
                ReviewedByAdminEmail = g.ReviewedByAdminEmail,
                ReviewedAt = g.ReviewedAt,
                ReviewNote = g.ReviewNote,
            };
        }

        private async Task<Dictionary<string, string>> CaregiverNamesAsync(IEnumerable<string> caregiverIds)
        {
            var oids = caregiverIds.Distinct()
                .Select(id => ObjectId.TryParse(id, out var o) ? (ObjectId?)o : null)
                .Where(o => o.HasValue).Select(o => o!.Value).ToList();
            if (oids.Count == 0) return new Dictionary<string, string>();
            var caregivers = await db.CareGivers.Where(c => oids.Contains(c.Id)).ToListAsync();
            return caregivers.ToDictionary(c => c.Id.ToString(), c => $"{c.FirstName} {c.LastName}".Trim());
        }

        private async Task<AdminGuarantorResponse> MapAdminAsync(Guarantor g)
        {
            var names = await CaregiverNamesAsync(new[] { g.CaregiverId });
            return MapAdmin(g, names.GetValueOrDefault(g.CaregiverId, string.Empty));
        }

        // ───────────────────────── CRUD ─────────────────────────

        public async Task<IEnumerable<GuarantorResponse>> GetGuarantorsAsync(string caregiverId)
        {
            await RequireCaregiverAsync(caregiverId);
            var items = await db.Guarantors
                .Where(g => g.CaregiverId == caregiverId)
                .ToListAsync();
            return items.OrderBy(g => g.CreatedAt).Select(Map).ToList();
        }

        public async Task<GuarantorResponse> AddGuarantorAsync(string caregiverId, AddGuarantorRequest request)
        {
            if (request == null) throw new ArgumentException("Request body is required.");
            await RequireCaregiverAsync(caregiverId);

            var existingCount = await db.Guarantors.CountAsync(g => g.CaregiverId == caregiverId);
            if (existingCount >= IGuarantorService.RequiredGuarantorCount)
            {
                throw new InvalidOperationException(
                    $"A caregiver may have at most {IGuarantorService.RequiredGuarantorCount} guarantors. " +
                    "Remove one before adding another.");
            }

            var now = DateTime.UtcNow;
            var entity = new Guarantor
            {
                Id = ObjectId.GenerateNewId(),
                CaregiverId = caregiverId,
                Name = request.Name.Trim(),
                RelationshipToCaregiver = request.RelationshipToCaregiver.Trim(),
                RelationshipCategory = request.RelationshipCategory,
                YearsKnown = request.YearsKnown,
                PhoneNo = request.PhoneNo.Trim(),
                Email = request.Email.Trim(),
                Address = request.Address.Trim(),
                Status = GuarantorStatuses.Pending,
                CreatedAt = now,
                AttemptCount = 0,
            };

            db.Guarantors.Add(entity);
            await db.SaveChangesAsync();
            logger.LogInformation("Guarantor {Id} added for caregiver {CaregiverId}", entity.Id, caregiverId);
            return Map(entity);
        }

        public async Task<GuarantorResponse> UpdateGuarantorAsync(string caregiverId, string guarantorId, UpdateGuarantorRequest request)
        {
            if (request == null) throw new ArgumentException("Request body is required.");
            var guarantor = await RequireOwnedGuarantorAsync(caregiverId, guarantorId);

            if (!string.Equals(guarantor.Status, GuarantorStatuses.Pending, StringComparison.Ordinal))
                throw new InvalidOperationException("A guarantor who has responded cannot be edited. Remove and re-add instead.");

            guarantor.Name = request.Name.Trim();
            guarantor.RelationshipToCaregiver = request.RelationshipToCaregiver.Trim();
            guarantor.RelationshipCategory = request.RelationshipCategory;
            guarantor.YearsKnown = request.YearsKnown;
            guarantor.PhoneNo = request.PhoneNo.Trim();
            guarantor.Email = request.Email.Trim();
            guarantor.Address = request.Address.Trim();
            guarantor.UpdatedOn = DateTime.UtcNow;

            await db.SaveChangesAsync();
            return Map(guarantor);
        }

        public async Task DeleteGuarantorAsync(string caregiverId, string guarantorId)
        {
            var guarantor = await RequireOwnedGuarantorAsync(caregiverId, guarantorId);
            db.Guarantors.Remove(guarantor);
            await db.SaveChangesAsync();
            logger.LogInformation("Guarantor {Id} removed for caregiver {CaregiverId}", guarantorId, caregiverId);
        }

        // ─────────────────────── Confirmation ───────────────────────

        public async Task<GuarantorResponse> SendConfirmationLinkAsync(string caregiverId, string guarantorId, string? origin)
        {
            var caregiver = await RequireCaregiverAsync(caregiverId);
            var guarantor = await RequireOwnedGuarantorAsync(caregiverId, guarantorId);
            return Map(await SendConfirmationLinkCoreAsync(caregiver, guarantor, origin, bypassCooldown: false, actor: "caregiver"));
        }

        private async Task<Guarantor> SendConfirmationLinkCoreAsync(
            Caregiver caregiver, Guarantor guarantor, string? origin, bool bypassCooldown, string actor)
        {
            if (!string.Equals(guarantor.Status, GuarantorStatuses.Pending, StringComparison.Ordinal))
                throw new InvalidOperationException("This guarantor has already responded.");

            var now = DateTime.UtcNow;
            if (!bypassCooldown && guarantor.CooldownUntil.HasValue && guarantor.CooldownUntil.Value > now)
            {
                throw new InvalidOperationException(
                    $"A confirmation link was sent recently. Try again after {guarantor.CooldownUntil.Value:u}.");
            }

            var attemptCount = guarantor.AttemptCount ?? 0;
            if (attemptCount >= MaxConfirmationSendAttempts)
            {
                throw new InvalidOperationException(
                    "The maximum number of confirmation attempts for this guarantor has been reached. " +
                    "Please contact support.");
            }

            var token = tokenHandler.GenerateGuarantorConfirmationToken(guarantor.Id.ToString());
            var link = BuildConfirmationLink(token, origin);
            var caregiverName = $"{caregiver.FirstName} {caregiver.LastName}".Trim();

            await emailService.SendGuarantorConfirmationEmailAsync(guarantor.Email, guarantor.Name, caregiverName, link);

            guarantor.AttemptCount = attemptCount + 1;
            guarantor.LastAttemptAt = now;
            guarantor.CooldownUntil = now.AddMinutes(ResendCooldownMinutes);
            guarantor.UpdatedOn = now;
            await db.SaveChangesAsync();

            logger.LogInformation(
                "Guarantor confirmation link sent for guarantor {GuarantorId} (attempt {Attempt}, by {Actor})",
                guarantor.Id, guarantor.AttemptCount, actor);
            return guarantor;
        }

        private async Task<(Guarantor? Guarantor, string? Error)> ResolveTokenAsync(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return (null, "Confirmation token is required.");

            var (isValid, guarantorId, error) = tokenHandler.ValidateGuarantorConfirmationToken(token);
            if (!isValid || string.IsNullOrWhiteSpace(guarantorId))
                return (null, error ?? "Invalid confirmation link.");

            if (!ObjectId.TryParse(guarantorId, out var oid))
                return (null, "Invalid confirmation link.");

            var guarantor = await db.Guarantors.FirstOrDefaultAsync(g => g.Id == oid);
            return guarantor == null
                ? (null, "This guarantor record no longer exists.")
                : (guarantor, null);
        }

        public async Task<GuarantorQuizInfo?> GetQuizInfoAsync(string token)
        {
            var (guarantor, _) = await ResolveTokenAsync(token);
            if (guarantor == null) return null;
            return new GuarantorQuizInfo
            {
                GuarantorName = guarantor.Name,
                AlreadyResponded = !string.Equals(guarantor.Status, GuarantorStatuses.Pending, StringComparison.Ordinal),
            };
        }

        public async Task<GuarantorConfirmationResult> SubmitResponsesAsync(string token, SubmitGuarantorResponsesRequest request)
        {
            if (request == null)
                return new GuarantorConfirmationResult { Success = false, Message = "Your answers are required." };

            var (guarantor, error) = await ResolveTokenAsync(token);
            if (guarantor == null)
                return new GuarantorConfirmationResult { Success = false, Message = error ?? "Invalid confirmation link." };

            // One submission per guarantor. A repeat (double click, reopened email) is a harmless no-op
            // that discloses nothing about how the first submission was judged.
            if (!string.Equals(guarantor.Status, GuarantorStatuses.Pending, StringComparison.Ordinal))
            {
                return new GuarantorConfirmationResult
                {
                    Success = true,
                    Message = "You have already responded. No further action is needed.",
                    GuarantorId = guarantor.Id.ToString(),
                };
            }

            // Content rules that DataAnnotations can't express. These reject the submission outright
            // (no state change) so the guarantor can fix the form and resubmit.
            if (GuarantorResponseEvaluator.WordCount(request.HowTheyKnow) < GuarantorResponseEvaluator.MinimumHowTheyKnowWords)
                throw new ArgumentException(
                    $"Please describe how you know them in at least {GuarantorResponseEvaluator.MinimumHowTheyKnowWords} words.");
            if (!string.Equals(request.TrustAnswer, GuarantorTrustAnswers.Yes, StringComparison.Ordinal)
                && string.IsNullOrWhiteSpace(request.TrustExplanation))
                throw new ArgumentException("Please explain your answer.");

            var caregiver = await RequireCaregiverAsync(guarantor.CaregiverId);
            var flags = GuarantorResponseEvaluator.Evaluate(guarantor, caregiver, new GuarantorAnswers
            {
                CaregiverName = request.CaregiverName,
                RelationshipCategory = request.RelationshipCategory,
                YearsKnown = request.YearsKnown,
                LastContact = request.LastContact,
                TrustAnswer = request.TrustAnswer,
                PhoneNo = request.PhoneNo,
            });

            var now = DateTime.UtcNow;
            guarantor.ResponsesSubmittedAt = now;
            guarantor.RespCaregiverName = request.CaregiverName.Trim();
            guarantor.RespRelationshipCategory = request.RelationshipCategory;
            guarantor.RespYearsKnown = request.YearsKnown;
            guarantor.RespLastContact = request.LastContact;
            guarantor.RespCaregiverArea = request.CaregiverArea.Trim();
            guarantor.RespHowTheyKnow = request.HowTheyKnow.Trim();
            guarantor.RespTrustAnswer = request.TrustAnswer;
            guarantor.RespTrustExplanation = string.IsNullOrWhiteSpace(request.TrustExplanation) ? null : request.TrustExplanation.Trim();
            guarantor.RespPhoneNo = string.IsNullOrWhiteSpace(request.PhoneNo) ? null : request.PhoneNo.Trim();
            guarantor.ReviewFlags = flags;
            guarantor.CooldownUntil = null;
            guarantor.UpdatedOn = now;

            if (flags.Count == 0)
            {
                guarantor.Status = GuarantorStatuses.Confirmed;
                guarantor.VerifiedAt = now;
                guarantor.ConfirmationMethod = GuarantorConfirmationMethods.SelfServe;
            }
            else
            {
                guarantor.Status = GuarantorStatuses.NeedsReview;
            }
            await db.SaveChangesAsync();

            logger.LogInformation(
                "Guarantor {GuarantorId} submitted answers for caregiver {CaregiverId}: {Outcome} (flags: {Flags})",
                guarantor.Id, guarantor.CaregiverId, guarantor.Status, string.Join(",", flags));

            if (flags.Count > 0)
            {
                await opsAlerts.NotifyAdminsAsync(
                    NotificationTypes.GuarantorNeedsReview,
                    "Guarantor needs review",
                    $"A guarantor ({guarantor.Name}) for {caregiver.FirstName} {caregiver.LastName} answered the questionnaire and needs a staff decision.",
                    guarantor.Id.ToString(),
                    alsoEmail: false);
            }

            return new GuarantorConfirmationResult
            {
                Success = true,
                Message = "Thank you. Your responses have been recorded.",
                GuarantorId = guarantor.Id.ToString(),
            };
        }

        // ─────────────────── Staff (OperationsPolicy) actions ───────────────────

        public async Task<IEnumerable<AdminGuarantorResponse>> AdminGetGuarantorsForCaregiverAsync(string caregiverId)
        {
            var caregiver = await RequireCaregiverAsync(caregiverId);
            var name = $"{caregiver.FirstName} {caregiver.LastName}".Trim();
            var items = await db.Guarantors
                .Where(g => g.CaregiverId == caregiverId)
                .ToListAsync();
            return items.OrderBy(g => g.CreatedAt).Select(g => MapAdmin(g, name)).ToList();
        }

        public async Task<IEnumerable<AdminGuarantorResponse>> AdminGetNeedsReviewAsync()
        {
            var items = await db.Guarantors
                .Where(g => g.Status == GuarantorStatuses.NeedsReview)
                .ToListAsync();
            var names = await CaregiverNamesAsync(items.Select(g => g.CaregiverId));
            return items
                .OrderBy(g => g.ResponsesSubmittedAt ?? g.CreatedAt)
                .Select(g => MapAdmin(g, names.GetValueOrDefault(g.CaregiverId, string.Empty)))
                .ToList();
        }

        public async Task<AdminGuarantorResponse> AdminReviewGuarantorAsync(
            string guarantorId, string adminId, string? adminEmail, AdminReviewGuarantorRequest request)
        {
            if (string.IsNullOrWhiteSpace(adminId))
                throw new ArgumentException("Admin identity is required.");
            if (request == null || string.IsNullOrWhiteSpace(request.Note) || request.Note.Trim().Length < 5)
                throw new ArgumentException("A note (min 5 chars) is required for a guarantor review decision.");
            var approve = string.Equals(request.Decision, "approve", StringComparison.Ordinal);
            if (!approve && !string.Equals(request.Decision, "reject", StringComparison.Ordinal))
                throw new ArgumentException("Decision must be 'approve' or 'reject'.");

            var oid = ParseObjectId(guarantorId, "guarantor id");
            var guarantor = await db.Guarantors.FirstOrDefaultAsync(g => g.Id == oid)
                ?? throw new KeyNotFoundException($"Guarantor '{guarantorId}' not found.");

            if (!string.Equals(guarantor.Status, GuarantorStatuses.NeedsReview, StringComparison.Ordinal))
                throw new InvalidOperationException("Only a guarantor awaiting review can be approved or rejected.");

            var before = new { guarantor.Status, guarantor.VerifiedAt, guarantor.ConfirmationMethod };

            var now = DateTime.UtcNow;
            guarantor.ReviewedByAdminId = adminId;
            guarantor.ReviewedByAdminEmail = adminEmail;
            guarantor.ReviewedAt = now;
            guarantor.ReviewNote = request.Note.Trim();
            guarantor.UpdatedOn = now;
            if (approve)
            {
                guarantor.Status = GuarantorStatuses.Confirmed;
                guarantor.VerifiedAt = now;
                guarantor.ConfirmationMethod = GuarantorConfirmationMethods.StaffReview;
            }
            else
            {
                guarantor.Status = GuarantorStatuses.Rejected;
            }

            var after = new { guarantor.Status, guarantor.VerifiedAt, guarantor.ConfirmationMethod };
            await db.AdminAuditLogs.AddAsync(new AdminAuditLog
            {
                Id = ObjectId.GenerateNewId(),
                AdminId = adminId,
                AdminEmail = adminEmail,
                TargetEntityType = "Guarantor",
                TargetEntityId = guarantor.Id.ToString(),
                TargetUserId = guarantor.CaregiverId,
                Action = approve ? "GuarantorReviewApproved" : "GuarantorReviewRejected",
                BeforeJson = JsonSerializer.Serialize(before),
                AfterJson = JsonSerializer.Serialize(after),
                Reason = request.Note.Trim(),
                Timestamp = now
            });
            await db.SaveChangesAsync();

            logger.LogInformation("Admin {AdminId} {Decision} guarantor {GuarantorId} for caregiver {CaregiverId}",
                adminId, approve ? "approved" : "rejected", guarantor.Id, guarantor.CaregiverId);

            if (!approve)
            {
                try
                {
                    await mediator.Send(new SendNotificationCommand(
                        guarantor.CaregiverId, "system", NotificationTypes.GuarantorRejected,
                        $"We could not accept {guarantor.Name} as one of your guarantors. Please remove them and add another guarantor from the Vetting page.",
                        "Guarantor not accepted", guarantor.Id.ToString()));
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to notify caregiver {CaregiverId} of guarantor rejection", guarantor.CaregiverId);
                }
            }

            return await MapAdminAsync(guarantor);
        }

        public async Task<AdminGuarantorResponse> AdminConfirmGuarantorAsync(
            string guarantorId, string adminId, string? adminEmail, string reason)
        {
            if (string.IsNullOrWhiteSpace(adminId))
                throw new ArgumentException("Admin identity is required.");
            if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5)
                throw new ArgumentException("A reason (min 5 chars) is required for a manual guarantor confirmation.");

            var oid = ParseObjectId(guarantorId, "guarantor id");
            var guarantor = await db.Guarantors.FirstOrDefaultAsync(g => g.Id == oid)
                ?? throw new KeyNotFoundException($"Guarantor '{guarantorId}' not found.");

            // Idempotent: an already-confirmed guarantor (self-serve or a prior override)
            // is a safe no-op — leave the original confirmation and attribution intact.
            if (string.Equals(guarantor.Status, GuarantorStatuses.Confirmed, StringComparison.Ordinal))
            {
                logger.LogInformation(
                    "Admin {AdminId} manual-confirm on guarantor {GuarantorId} was a no-op (already confirmed via {Method})",
                    adminId, guarantor.Id, guarantor.ConfirmationMethod ?? "unknown");
                return await MapAdminAsync(guarantor);
            }

            var before = new { guarantor.Status, guarantor.VerifiedAt, guarantor.ConfirmationMethod, guarantor.ConfirmedByAdminId };

            var now = DateTime.UtcNow;
            guarantor.Status = GuarantorStatuses.Confirmed;
            guarantor.VerifiedAt = now;
            guarantor.ConfirmationMethod = GuarantorConfirmationMethods.StaffOverride;
            guarantor.ConfirmedByAdminId = adminId;
            guarantor.ConfirmedByAdminEmail = adminEmail;
            guarantor.CooldownUntil = null;
            guarantor.UpdatedOn = now;

            var after = new { guarantor.Status, guarantor.VerifiedAt, guarantor.ConfirmationMethod, guarantor.ConfirmedByAdminId };

            await db.AdminAuditLogs.AddAsync(new AdminAuditLog
            {
                Id = ObjectId.GenerateNewId(),
                AdminId = adminId,
                AdminEmail = adminEmail,
                TargetEntityType = "Guarantor",
                TargetEntityId = guarantor.Id.ToString(),
                TargetUserId = guarantor.CaregiverId,
                Action = "GuarantorManualConfirm",
                BeforeJson = JsonSerializer.Serialize(before),
                AfterJson = JsonSerializer.Serialize(after),
                Reason = reason.Trim(),
                Timestamp = now
            });

            await db.SaveChangesAsync();

            logger.LogInformation(
                "Admin {AdminId} manually confirmed guarantor {GuarantorId} for caregiver {CaregiverId}",
                adminId, guarantor.Id, guarantor.CaregiverId);
            return await MapAdminAsync(guarantor);
        }

        public async Task<AdminGuarantorResponse> AdminResendConfirmationLinkAsync(
            string guarantorId, string adminId, string? adminEmail, string? origin)
        {
            if (string.IsNullOrWhiteSpace(adminId))
                throw new ArgumentException("Admin identity is required.");

            var oid = ParseObjectId(guarantorId, "guarantor id");
            var guarantor = await db.Guarantors.FirstOrDefaultAsync(g => g.Id == oid)
                ?? throw new KeyNotFoundException($"Guarantor '{guarantorId}' not found.");

            var caregiverOid = ParseObjectId(guarantor.CaregiverId, "caregiver id");
            var caregiver = await db.CareGivers.FirstOrDefaultAsync(c => c.Id == caregiverOid)
                ?? throw new KeyNotFoundException($"Caregiver '{guarantor.CaregiverId}' not found.");

            await SendConfirmationLinkCoreAsync(
                caregiver, guarantor, origin, bypassCooldown: true, actor: $"admin:{adminId}");

            await db.AdminAuditLogs.AddAsync(new AdminAuditLog
            {
                Id = ObjectId.GenerateNewId(),
                AdminId = adminId,
                AdminEmail = adminEmail,
                TargetEntityType = "Guarantor",
                TargetEntityId = guarantor.Id.ToString(),
                TargetUserId = guarantor.CaregiverId,
                Action = "GuarantorLinkResendByStaff",
                AfterJson = JsonSerializer.Serialize(new { guarantor.AttemptCount, guarantor.LastAttemptAt }),
                Reason = "Staff resent confirmation link on caregiver's behalf.",
                Timestamp = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            return await MapAdminAsync(guarantor);
        }

        private string BuildConfirmationLink(string token, string? origin)
        {
            var encoded = HttpUtility.UrlEncode(token);
            // Only trust the caller-supplied origin when it is a known frontend origin;
            // otherwise fall back to the configured canonical frontend URL. This keeps
            // an attacker-controlled Origin header out of the emailed link.
            var baseUrl = (!string.IsNullOrWhiteSpace(origin) && originValidation.IsFrontendOrigin(origin)
                ? origin
                : configuration["FrontendUrl"] ?? "https://oncarepro.com").TrimEnd('/');
            return $"{baseUrl}/guarantor-confirmation?token={encoded}";
        }
    }
}
