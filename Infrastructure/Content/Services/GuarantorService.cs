using Application.DTOs;
using Application.Interfaces.Authentication;
using Application.Interfaces.Common;
using Application.Interfaces.Content;
using Application.Interfaces.Email;
using Domain.Entities;
using Infrastructure.Content.Data;
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
            ILogger<GuarantorService> logger)
        {
            this.db = db;
            this.tokenHandler = tokenHandler;
            this.emailService = emailService;
            this.configuration = configuration;
            this.originValidation = originValidation;
            this.logger = logger;
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

            if (string.Equals(guarantor.Status, GuarantorStatuses.Confirmed, StringComparison.Ordinal))
                throw new InvalidOperationException("A confirmed guarantor cannot be edited. Remove and re-add instead.");

            guarantor.Name = request.Name.Trim();
            guarantor.RelationshipToCaregiver = request.RelationshipToCaregiver.Trim();
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
            return await SendConfirmationLinkCoreAsync(caregiver, guarantor, origin, bypassCooldown: false, actor: "caregiver");
        }

        private async Task<GuarantorResponse> SendConfirmationLinkCoreAsync(
            Caregiver caregiver, Guarantor guarantor, string? origin, bool bypassCooldown, string actor)
        {
            if (string.Equals(guarantor.Status, GuarantorStatuses.Confirmed, StringComparison.Ordinal))
                throw new InvalidOperationException("This guarantor has already confirmed.");

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
            return Map(guarantor);
        }

        public async Task<GuarantorConfirmationResult> ConfirmByTokenAsync(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return new GuarantorConfirmationResult { Success = false, Message = "Confirmation token is required." };

            var (isValid, guarantorId, error) = tokenHandler.ValidateGuarantorConfirmationToken(token);
            if (!isValid || string.IsNullOrWhiteSpace(guarantorId))
                return new GuarantorConfirmationResult { Success = false, Message = error ?? "Invalid confirmation link." };

            if (!ObjectId.TryParse(guarantorId, out var oid))
                return new GuarantorConfirmationResult { Success = false, Message = "Invalid confirmation link." };

            var guarantor = await db.Guarantors.FirstOrDefaultAsync(g => g.Id == oid);
            if (guarantor == null)
                return new GuarantorConfirmationResult { Success = false, Message = "This guarantor record no longer exists." };

            if (string.Equals(guarantor.Status, GuarantorStatuses.Confirmed, StringComparison.Ordinal))
            {
                return new GuarantorConfirmationResult
                {
                    Success = true,
                    NewlyConfirmed = false,
                    Message = "You have already confirmed. No further action is needed.",
                    GuarantorId = guarantor.Id.ToString(),
                    CaregiverId = guarantor.CaregiverId,
                };
            }

            var now = DateTime.UtcNow;
            guarantor.Status = GuarantorStatuses.Confirmed;
            guarantor.VerifiedAt = now;
            guarantor.ConfirmationMethod = GuarantorConfirmationMethods.SelfServe;
            guarantor.CooldownUntil = null;
            guarantor.UpdatedOn = now;
            await db.SaveChangesAsync();

            logger.LogInformation("Guarantor {GuarantorId} confirmed (self-serve) for caregiver {CaregiverId}",
                guarantor.Id, guarantor.CaregiverId);

            return new GuarantorConfirmationResult
            {
                Success = true,
                NewlyConfirmed = true,
                Message = "Thank you. Your guarantor confirmation has been recorded.",
                GuarantorId = guarantor.Id.ToString(),
                CaregiverId = guarantor.CaregiverId,
            };
        }

        // ─────────────────── Staff (OperationsPolicy) actions ───────────────────

        public async Task<IEnumerable<GuarantorResponse>> AdminGetGuarantorsForCaregiverAsync(string caregiverId)
        {
            await RequireCaregiverAsync(caregiverId);
            var items = await db.Guarantors
                .Where(g => g.CaregiverId == caregiverId)
                .ToListAsync();
            return items.OrderBy(g => g.CreatedAt).Select(Map).ToList();
        }

        public async Task<GuarantorResponse> AdminConfirmGuarantorAsync(
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
                return Map(guarantor);
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
            return Map(guarantor);
        }

        public async Task<GuarantorResponse> AdminResendConfirmationLinkAsync(
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

            var response = await SendConfirmationLinkCoreAsync(
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

            return response;
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
