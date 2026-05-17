using Application.DTOs;
using Application.Interfaces.Content;
using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using Org.BouncyCastle.Ocsp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Content.Services
{
    public class VerificationService : IVerificationService
    {
        private readonly CareProDbContext careProDbContext;
        private readonly ICareGiverService careGiverService;
        private readonly ILogger<VerificationService> logger;
        private readonly IConfiguration configuration;

        // Defaults if config keys are missing — keeps the gate functional even
        // before the appsettings rollout reaches every environment.
        private const int DefaultMaxVerificationAttempts = 5;
        private const int DefaultCooldownHoursAfterFailure = 24;
        private const int SessionExpiryMinutes = 30;

        public VerificationService(
            CareProDbContext careProDbContext,
            ICareGiverService careGiverService,
            ILogger<VerificationService> logger,
            IConfiguration configuration)
        {
            this.careProDbContext = careProDbContext;
            this.careGiverService = careGiverService;
            this.logger = logger;
            this.configuration = configuration;
        }

        public async Task<string> CreateVerificationAsync(AddVerificationRequest addVerificationRequest)
        {
            try
            {
                logger.LogInformation("Starting CreateVerificationAsync for UserId: {UserId}", addVerificationRequest?.UserId);
                
                // Validate input
                if (addVerificationRequest == null)
                {
                    logger.LogError("AddVerificationRequest is null");
                    throw new ArgumentNullException(nameof(addVerificationRequest), "Verification request cannot be null");
                }
                
                if (string.IsNullOrEmpty(addVerificationRequest.UserId))
                {
                    logger.LogError("UserId is null or empty");
                    throw new ArgumentException("UserId is required", nameof(addVerificationRequest.UserId));
                }
                
                // Log the request details for debugging
                logger.LogInformation("Processing verification request - UserId: {UserId}, Method: {Method}, Status: {Status}, VerificationNo: {VerificationNo}",
                    addVerificationRequest.UserId, addVerificationRequest.VerificationMethod, 
                    addVerificationRequest.VerificationStatus, addVerificationRequest.VerificationNo);

                // Check if user exists - try both AppUserId and direct UserId match
                logger.LogInformation("Looking for user with ID: {UserId}", addVerificationRequest.UserId);
                
                var appUser = await careProDbContext.AppUsers.FirstOrDefaultAsync(x => 
                    x.AppUserId.ToString() == addVerificationRequest.UserId || 
                    x.Email == addVerificationRequest.UserId);
                
                if (appUser == null)
                {
                    logger.LogWarning("User not found in AppUsers table for UserId: {UserId}. Checking if this is a valid ObjectId format.", addVerificationRequest.UserId);
                    
                    // Try to parse as ObjectId to give better error message
                    if (!ObjectId.TryParse(addVerificationRequest.UserId, out var objectId))
                    {
                        throw new ArgumentException($"The User ID '{addVerificationRequest.UserId}' is not in a valid format and no user was found");
                    }
                    
                    throw new KeyNotFoundException($"No user found with ID '{addVerificationRequest.UserId}'");
                }

                logger.LogInformation("Found user: {FirstName} {LastName} with email: {Email}", 
                    appUser.FirstName, appUser.LastName, appUser.Email);

                // Check name matching - but make it more flexible since the DTO doesn't include these fields
                if (!string.IsNullOrEmpty(addVerificationRequest.VerifiedFirstName) && 
                    !string.IsNullOrEmpty(addVerificationRequest.VerifiedLastName))
                {
                    if (appUser.FirstName?.Trim().ToLowerInvariant() != addVerificationRequest.VerifiedFirstName?.Trim().ToLowerInvariant() || 
                        appUser.LastName?.Trim().ToLowerInvariant() != addVerificationRequest.VerifiedLastName?.Trim().ToLowerInvariant())
                    {
                        logger.LogWarning("Name mismatch - Stored: {StoredFirst} {StoredLast}, Verified: {VerifiedFirst} {VerifiedLast}",
                            appUser.FirstName, appUser.LastName, addVerificationRequest.VerifiedFirstName, addVerificationRequest.VerifiedLastName);
                        
                        throw new InvalidOperationException($"The verified name '{addVerificationRequest.VerifiedFirstName} {addVerificationRequest.VerifiedLastName}' does not match the stored name '{appUser.FirstName} {appUser.LastName}'");
                    }
                }

                // Check for existing verification
                logger.LogInformation("Checking for existing verification for UserId: {UserId}", addVerificationRequest.UserId);
                
                var existingVerification = await careProDbContext.Verifications.FirstOrDefaultAsync(x => x.UserId == addVerificationRequest.UserId);

                if (existingVerification != null)
                {
                    logger.LogInformation("Found existing verification with ID: {VerificationId}. Current status: {Status}", 
                        existingVerification.VerificationId, existingVerification.VerificationStatus);
                    
                    // Instead of throwing error, update the existing verification
                    logger.LogInformation("Updating existing verification instead of creating new one");
                    
                    existingVerification.VerificationMethod = addVerificationRequest.VerificationMethod;
                    existingVerification.VerificationStatus = addVerificationRequest.VerificationStatus;
                    existingVerification.VerificationNo = addVerificationRequest.VerificationNo;
                    existingVerification.UpdatedOn = DateTime.UtcNow;
                    
                    // Update verified status based on new status
                    existingVerification.IsVerified = addVerificationRequest.VerificationStatus?.ToLowerInvariant() is "verified" or "completed" or "success";
                    
                    careProDbContext.Verifications.Update(existingVerification);
                    await careProDbContext.SaveChangesAsync();
                    
                    logger.LogInformation("Successfully updated existing verification with ID: {VerificationId}", existingVerification.VerificationId);
                    return existingVerification.VerificationId.ToString();
                }

                // Create new verification record
                logger.LogInformation("Creating new verification record for UserId: {UserId}", addVerificationRequest.UserId);

                var verification = new Verification
                {
                    VerificationMethod = addVerificationRequest.VerificationMethod,
                    VerificationNo = addVerificationRequest.VerificationNo,
                    VerificationStatus = addVerificationRequest.VerificationStatus,
                    UserId = addVerificationRequest.UserId,
                    VerificationId = ObjectId.GenerateNewId(),
                    IsVerified = addVerificationRequest.VerificationStatus?.ToLowerInvariant() is "verified" or "completed" or "success",
                    VerifiedOn = DateTime.UtcNow,
                };

                logger.LogInformation("Generated new verification with ID: {VerificationId}", verification.VerificationId);

                await careProDbContext.Verifications.AddAsync(verification);
                await careProDbContext.SaveChangesAsync();

                logger.LogInformation("Successfully created verification with ID: {VerificationId} for UserId: {UserId}",
                    verification.VerificationId, addVerificationRequest.UserId);

                return verification.VerificationId.ToString();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in CreateVerificationAsync for UserId: {UserId}. Exception: {ExceptionType}", 
                    addVerificationRequest?.UserId, ex.GetType().Name);
                throw;
            }
        }

        public async Task<VerificationResponse> GetVerificationAsync(string userId)
        {
            var verification = await careProDbContext.Verifications.FirstOrDefaultAsync(x => x.UserId.ToString() == userId);

            if (verification == null)
            {
                throw new KeyNotFoundException($"User with ID '{userId}' has not been verified.");
            }


            var verificationDTO = new VerificationResponse()
            {
                VerificationId = verification.VerificationId.ToString(),
                UserId = verification.UserId,
                VerificationMethod = verification.VerificationMethod,
                VerificationNo = verification.VerificationNo,
                VerificationStatus = verification.VerificationStatus,
                IsVerified = verification.IsVerified,
                VerifiedOn = verification.VerifiedOn,
                UpdatedOn = verification.UpdatedOn,
                AttemptCount = verification.AttemptCount ?? 0,
                CooldownUntil = verification.CooldownUntil,
                UserType = verification.UserType,
            };

            return verificationDTO;
        }

        public async Task<string> UpdateVerificationAsync(string verificationId, UpdateVerificationRequest updateVerificationRequest)
        {
            if (!ObjectId.TryParse(verificationId, out var objectId))
            {
                throw new ArgumentException("Invalid Verification ID format.");
            }

            var existingVerification = await careProDbContext.Verifications.FindAsync(objectId);
            if (existingVerification == null)
            {
                throw new KeyNotFoundException($"Verification with ID '{verificationId}' not found.");
            }
            existingVerification.VerificationMethod = updateVerificationRequest.VerificationMode;
            existingVerification.VerificationStatus = updateVerificationRequest.VerificationStatus;
            existingVerification.UpdatedOn = DateTime.Now;

            // Update verified status based on new status
            existingVerification.IsVerified = updateVerificationRequest.VerificationStatus?.ToLower() is "completed" or "verified" or "success";

            careProDbContext.Verifications.Update(existingVerification);
            await careProDbContext.SaveChangesAsync();

            return $"Verification with ID '{verificationId}' Updated successfully.";

        }

        public async Task<VerificationResponse?> GetUserVerificationStatusAsync(string userId)
        {
            try
            {
                var verification = await careProDbContext.Verifications.FirstOrDefaultAsync(x => x.UserId.ToString() == userId);

                if (verification == null)
                {
                    return null;
                }

                // Get the latest webhook log timestamp for this user
                var lastWebhookLog = await careProDbContext.WebhookLogs
                    .Where(w => w.UserId == userId)
                    .OrderByDescending(w => w.ReceivedAt)
                    .FirstOrDefaultAsync();

                var verificationResponse = new VerificationResponse()
                {
                    VerificationId = verification.VerificationId.ToString(),
                    UserId = verification.UserId,
                    VerificationMethod = verification.VerificationMethod,
                    VerificationNo = verification.VerificationNo,
                    VerificationStatus = verification.VerificationStatus,
                    IsVerified = verification.IsVerified,
                    VerifiedOn = verification.VerifiedOn,
                    UpdatedOn = verification.UpdatedOn,
                    LastWebhookReceivedAt = lastWebhookLog?.ReceivedAt,
                    AttemptCount = verification.AttemptCount ?? 0,
                    CooldownUntil = verification.CooldownUntil,
                    UserType = verification.UserType,
                };

                return verificationResponse;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting verification status for user: {UserId}", userId);
                return null;
            }
        }

        public async Task<string> AddVerificationAsync(AddVerificationRequest addVerificationRequest)
        {
            // For webhook data, we need a more flexible approach
            try
            {
                logger.LogInformation("Processing verification for UserId: {UserId}", addVerificationRequest.UserId);

                // Check if user exists in AppUsers table
                var appUser = await careProDbContext.AppUsers.FirstOrDefaultAsync(x =>
                    x.AppUserId.ToString() == addVerificationRequest.UserId ||
                    x.Email == addVerificationRequest.UserId);

                if (appUser == null)
                {
                    logger.LogWarning("User not found in AppUsers table for UserId: {UserId}", addVerificationRequest.UserId);
                    // For webhook data, we'll still create the verification record
                    // This allows tracking verification attempts even if user isn't in our system yet
                }

                // Check for existing verification
                var existingVerification = await careProDbContext.Verifications.FirstOrDefaultAsync(x => x.UserId == addVerificationRequest.UserId);

                bool isVerified = IsVerifiedStatus(addVerificationRequest.VerificationStatus);

                // Compute cooldown adjustment based on incoming status.
                //   success    -> clear cooldown (user is done)
                //   failed     -> set cooldown to now + configured hours
                //   abandoned  -> set cooldown (treated as failure)
                //   pending    -> leave existing cooldown untouched
                //   anything else -> leave untouched
                DateTime? newCooldown = ComputeCooldown(
                    addVerificationRequest.VerificationStatus,
                    existingVerification?.CooldownUntil);

                // Backward-compatible UserType resolution. Null/empty incoming
                // value preserves whatever's already on the record; if both
                // are missing we default to "Caregiver" to match legacy data.
                var resolvedUserType = ResolveUserType(
                    addVerificationRequest.UserType,
                    existingVerification?.UserType);

                if (existingVerification != null)
                {
                    // Update existing verification instead of throwing error
                    logger.LogInformation("Updating existing verification for UserId: {UserId}", addVerificationRequest.UserId);

                    existingVerification.VerificationMethod = addVerificationRequest.VerificationMethod;
                    existingVerification.VerificationStatus = addVerificationRequest.VerificationStatus;
                    existingVerification.VerificationNo = addVerificationRequest.VerificationNo;
                    existingVerification.UpdatedOn = DateTime.UtcNow;
                    existingVerification.IsVerified = isVerified;
                    existingVerification.CooldownUntil = newCooldown;
                    existingVerification.UserType = resolvedUserType;

                    await careProDbContext.SaveChangesAsync();

                    logger.LogInformation("Successfully updated verification for UserId: {UserId} with status: {Status}",
                        addVerificationRequest.UserId, addVerificationRequest.VerificationStatus);

                    // Route profile update based on user type (case-insensitive)
                    await UpdateUserVerificationStateAsync(
                        addVerificationRequest.UserId,
                        resolvedUserType,
                        isVerified,
                        addVerificationRequest.VerificationStatus);

                    return existingVerification.VerificationId.ToString();
                }

                // Create new verification record
                logger.LogInformation("Creating new verification record for UserId: {UserId}", addVerificationRequest.UserId);

                var verification = new Verification
                {
                    VerificationMethod = addVerificationRequest.VerificationMethod,
                    VerificationNo = addVerificationRequest.VerificationNo,
                    VerificationStatus = addVerificationRequest.VerificationStatus,
                    UserId = addVerificationRequest.UserId,
                    VerificationId = ObjectId.GenerateNewId(),
                    IsVerified = isVerified,
                    VerifiedOn = DateTime.UtcNow,
                    CooldownUntil = newCooldown,
                    UserType = resolvedUserType,
                    // AttemptCount intentionally left at 0 — webhook arrival
                    // is not the same event as session initiation. The
                    // initiate-session endpoint is what increments attempts.
                };

                await careProDbContext.Verifications.AddAsync(verification);
                await careProDbContext.SaveChangesAsync();

                logger.LogInformation("Successfully created verification for UserId: {UserId} with status: {Status}",
                    addVerificationRequest.UserId, addVerificationRequest.VerificationStatus);

                await UpdateUserVerificationStateAsync(
                    addVerificationRequest.UserId,
                    resolvedUserType,
                    isVerified,
                    addVerificationRequest.VerificationStatus);

                return verification.VerificationId.ToString();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing verification for UserId: {UserId}", addVerificationRequest.UserId);
                throw;
            }
        }

        private async Task UpdateCaregiverVerificationStateAsync(string userId, bool isVerified, string? verificationStatus)
        {
            try
            {
                var caregiver = await careProDbContext.CareGivers.FirstOrDefaultAsync(c => c.Id.ToString() == userId);
                if (caregiver == null)
                {
                    logger.LogWarning("Caregiver not found for userId {UserId}, skipping profile verification update", userId);
                    return;
                }

                caregiver.IsIdentityVerified = isVerified;
                caregiver.IdentityVerificationStatus = verificationStatus;
                if (isVerified)
                {
                    caregiver.IdentityVerifiedAt = DateTime.UtcNow;
                }

                await careProDbContext.SaveChangesAsync();
                logger.LogInformation("Updated caregiver profile verification state for UserId: {UserId}, IsIdentityVerified: {IsVerified}", userId, isVerified);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to update caregiver profile verification state for UserId: {UserId}", userId);
                // Don't throw - verification record was already saved, this is a secondary update
            }
        }

        public async Task<int> BackfillCaregiverVerificationStateAsync()
        {
            var verifiedRecords = await careProDbContext.Verifications
                .Where(v => v.IsVerified)
                .ToListAsync();

            int updated = 0;

            foreach (var verification in verifiedRecords)
            {
                try
                {
                    var caregiver = await careProDbContext.CareGivers
                        .FirstOrDefaultAsync(c => c.Id.ToString() == verification.UserId);

                    if (caregiver == null || caregiver.IsIdentityVerified == true)
                        continue;

                    caregiver.IsIdentityVerified = true;
                    caregiver.IdentityVerificationStatus = verification.VerificationStatus;
                    caregiver.IdentityVerifiedAt = verification.UpdatedOn ?? verification.VerifiedOn;

                    await careProDbContext.SaveChangesAsync();
                    updated++;
                    logger.LogInformation("Backfilled verification state for caregiver: {UserId}", verification.UserId);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to backfill verification state for caregiver: {UserId}", verification.UserId);
                }
            }

            return updated;
        }

        public async Task<AdminVerificationStatusOverrideResponse> AdminOverrideVerificationStatusAsync(
            string verificationId,
            AdminVerificationStatusOverrideRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.AdminId))
                throw new ArgumentException("AdminId is required", nameof(request.AdminId));
            if (string.IsNullOrWhiteSpace(request.NewStatus))
                throw new ArgumentException("NewStatus is required", nameof(request.NewStatus));
            if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length < 5)
                throw new ArgumentException("A reason (min 5 chars) is required for an admin override", nameof(request.Reason));

            // Whitelist allowed status values to keep data consistent
            var normalised = request.NewStatus.Trim();
            var allowed = new[] { "Completed", "Verified", "Success", "Failed", "Pending" };
            if (!allowed.Any(a => string.Equals(a, normalised, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException(
                    $"NewStatus must be one of: {string.Join(", ", allowed)}",
                    nameof(request.NewStatus));
            }

            if (!ObjectId.TryParse(verificationId, out var objectId))
                throw new ArgumentException("Invalid Verification ID format.", nameof(verificationId));

            var existing = await careProDbContext.Verifications.FindAsync(objectId);
            if (existing == null)
                throw new KeyNotFoundException($"Verification with ID '{verificationId}' not found.");

            var previousStatus = existing.VerificationStatus ?? string.Empty;
            var previousIsVerified = existing.IsVerified;

            existing.VerificationStatus = normalised;
            existing.IsVerified = normalised.ToLowerInvariant()
                is "completed" or "verified" or "success";
            existing.UpdatedOn = DateTime.UtcNow;

            careProDbContext.Verifications.Update(existing);
            await careProDbContext.SaveChangesAsync();

            // Keep the caregiver profile flags in sync with the override.
            // Reuses the same path the webhook flow uses, so no new
            // side-effect surface is introduced.
            await UpdateCaregiverVerificationStateAsync(
                existing.UserId,
                existing.IsVerified,
                existing.VerificationStatus);

            // Audit log — append-only, never mutates existing data
            var before = new
            {
                VerificationStatus = previousStatus,
                IsVerified = previousIsVerified
            };
            var after = new
            {
                VerificationStatus = existing.VerificationStatus,
                IsVerified = existing.IsVerified
            };

            await careProDbContext.AdminAuditLogs.AddAsync(new AdminAuditLog
            {
                Id = ObjectId.GenerateNewId(),
                AdminId = request.AdminId,
                TargetEntityType = "Verification",
                TargetEntityId = existing.VerificationId.ToString(),
                TargetUserId = existing.UserId,
                Action = "VerificationStatusOverride",
                BeforeJson = System.Text.Json.JsonSerializer.Serialize(before),
                AfterJson = System.Text.Json.JsonSerializer.Serialize(after),
                Reason = request.Reason.Trim(),
                Timestamp = DateTime.UtcNow
            });
            await careProDbContext.SaveChangesAsync();

            logger.LogInformation(
                "Admin {AdminId} overrode verification {VerificationId} from {Previous} to {New}",
                request.AdminId, verificationId, previousStatus, existing.VerificationStatus);

            return new AdminVerificationStatusOverrideResponse
            {
                Success = true,
                VerificationId = existing.VerificationId.ToString(),
                PreviousStatus = previousStatus,
                NewStatus = existing.VerificationStatus,
                IsVerified = existing.IsVerified,
                Message = $"Verification status overridden by admin (previous: '{previousStatus}', new: '{existing.VerificationStatus}')."
            };
        }

        // =================================================================
        // Cost-control gate (added May 2026)
        // =================================================================

        public async Task<VerificationGateResponse> CheckVerificationEligibilityAsync(string userId, string userType)
        {
            if (string.IsNullOrWhiteSpace(userId))
                throw new ArgumentException("UserId is required", nameof(userId));

            var resolvedUserType = ResolveUserType(userType, null);
            var maxAttempts = GetMaxVerificationAttempts();

            var record = await careProDbContext.Verifications
                .FirstOrDefaultAsync(v => v.UserId == userId);

            // No record yet — fully eligible
            if (record == null)
            {
                return new VerificationGateResponse
                {
                    IsEligible = true,
                    Reason = "eligible",
                    AttemptCount = 0,
                    AttemptsRemaining = maxAttempts,
                    CooldownUntil = null
                };
            }

            var attemptCount = record.AttemptCount ?? 0;
            var attemptsRemaining = Math.Max(0, maxAttempts - attemptCount);
            var status = (record.VerificationStatus ?? string.Empty).Trim().ToLowerInvariant();

            // Already verified — block (also covers "completed", "verified", "success")
            if (record.IsVerified || status is "completed" or "verified" or "success" or "successful")
            {
                return new VerificationGateResponse
                {
                    IsEligible = false,
                    Reason = "already_verified",
                    AttemptCount = attemptCount,
                    AttemptsRemaining = attemptsRemaining,
                    CooldownUntil = record.CooldownUntil
                };
            }

            // Pending review — Dojah is still processing; do not let user retry
            if (status is "pending" or "ongoing" or "started")
            {
                return new VerificationGateResponse
                {
                    IsEligible = false,
                    Reason = "pending_review",
                    AttemptCount = attemptCount,
                    AttemptsRemaining = attemptsRemaining,
                    CooldownUntil = record.CooldownUntil
                };
            }

            // Active cooldown after a failed/abandoned attempt
            if (record.CooldownUntil.HasValue && record.CooldownUntil.Value > DateTime.UtcNow)
            {
                return new VerificationGateResponse
                {
                    IsEligible = false,
                    Reason = "cooldown_active",
                    AttemptCount = attemptCount,
                    AttemptsRemaining = attemptsRemaining,
                    CooldownUntil = record.CooldownUntil
                };
            }

            // Hard cap reached
            if (attemptCount >= maxAttempts)
            {
                return new VerificationGateResponse
                {
                    IsEligible = false,
                    Reason = "max_attempts_reached",
                    AttemptCount = attemptCount,
                    AttemptsRemaining = 0,
                    CooldownUntil = record.CooldownUntil
                };
            }

            return new VerificationGateResponse
            {
                IsEligible = true,
                Reason = "eligible",
                AttemptCount = attemptCount,
                AttemptsRemaining = attemptsRemaining,
                CooldownUntil = null
            };
        }

        public async Task<(VerificationGateResponse Gate, InitiateSessionResponse? Session)> InitiateVerificationSessionAsync(
            string userId,
            string userType)
        {
            if (string.IsNullOrWhiteSpace(userId))
                throw new ArgumentException("UserId is required", nameof(userId));

            var resolvedUserType = ResolveUserType(userType, null);
            var maxAttempts = GetMaxVerificationAttempts();

            // Re-check eligibility under the same logic the read-only endpoint uses.
            var gate = await CheckVerificationEligibilityAsync(userId, resolvedUserType);
            if (!gate.IsEligible)
            {
                logger.LogInformation(
                    "Blocked verification session initiation for UserId={UserId}, UserType={UserType}, Reason={Reason}",
                    userId, resolvedUserType, gate.Reason);
                return (gate, null);
            }

            // Atomically increment attempt count + stamp LastAttemptAt.
            var record = await careProDbContext.Verifications
                .FirstOrDefaultAsync(v => v.UserId == userId);

            var now = DateTime.UtcNow;

            if (record == null)
            {
                record = new Verification
                {
                    VerificationId = ObjectId.GenerateNewId(),
                    UserId = userId,
                    VerificationMethod = string.Empty,
                    VerificationNo = string.Empty,
                    VerificationStatus = "initiated",
                    IsVerified = false,
                    VerifiedOn = now,
                    AttemptCount = 1,
                    LastAttemptAt = now,
                    UserType = resolvedUserType
                };
                await careProDbContext.Verifications.AddAsync(record);
            }
            else
            {
                record.AttemptCount = (record.AttemptCount ?? 0) + 1;
                record.LastAttemptAt = now;
                record.UserType = ResolveUserType(resolvedUserType, record.UserType);
                // Status stays whatever it was (likely "failed" or "abandoned")
                // until Dojah's webhook arrives.
            }

            await careProDbContext.SaveChangesAsync();

            var unix = new DateTimeOffset(now).ToUnixTimeSeconds();
            var referenceId = $"{resolvedUserType.ToLowerInvariant()}_{userId}_{unix}";

            var attemptsRemaining = Math.Max(0, maxAttempts - (record.AttemptCount ?? 0));

            logger.LogInformation(
                "Issued verification session reference_id={ReferenceId} for UserId={UserId}, UserType={UserType}, AttemptCount={AttemptCount}",
                referenceId, userId, resolvedUserType, record.AttemptCount ?? 0);

            var session = new InitiateSessionResponse
            {
                ReferenceId = referenceId,
                UserId = userId,
                UserType = resolvedUserType,
                IssuedAt = now,
                ExpiresAt = now.AddMinutes(SessionExpiryMinutes),
                AttemptCount = record.AttemptCount ?? 0,
                AttemptsRemaining = attemptsRemaining
            };

            // Re-build a fresh, authoritative gate response reflecting the
            // post-increment counters so the frontend can update its UI in
            // one round-trip.
            var freshGate = new VerificationGateResponse
            {
                IsEligible = true,
                Reason = "eligible",
                AttemptCount = record.AttemptCount ?? 0,
                AttemptsRemaining = attemptsRemaining,
                CooldownUntil = null
            };

            return (freshGate, session);
        }

        // -----------------------------------------------------------------
        // Internal helpers (case-insensitive, backward-compatible)
        // -----------------------------------------------------------------

        private static bool IsVerifiedStatus(string? status)
        {
            if (string.IsNullOrWhiteSpace(status)) return false;
            var s = status.Trim().ToLowerInvariant();
            return s is "completed" or "verified" or "success" or "successful";
        }

        private DateTime? ComputeCooldown(string? incomingStatus, DateTime? existingCooldown)
        {
            var s = (incomingStatus ?? string.Empty).Trim().ToLowerInvariant();
            var hours = GetCooldownHours();

            return s switch
            {
                "completed" or "verified" or "success" or "successful"
                    => null,                                          // success clears cooldown
                "failed" or "abandoned"
                    => DateTime.UtcNow.AddHours(hours),               // start cooldown
                _   => existingCooldown                                // pending/ongoing/etc — leave as-is
            };
        }

        private static string ResolveUserType(string? incoming, string? existing)
        {
            // 1. Honour explicit incoming value when present
            if (!string.IsNullOrWhiteSpace(incoming))
            {
                return NormaliseUserType(incoming);
            }

            // 2. Otherwise keep existing record's value
            if (!string.IsNullOrWhiteSpace(existing))
            {
                return NormaliseUserType(existing);
            }

            // 3. Backward-compat default for legacy records
            return "Caregiver";
        }

        private static string NormaliseUserType(string raw)
        {
            var t = raw.Trim().ToLowerInvariant();
            return t switch
            {
                "client" => "Client",
                "caregiver" => "Caregiver",
                _ => "Caregiver" // unknown values fall back to caregiver
            };
        }

        private int GetMaxVerificationAttempts()
        {
            var v = configuration?["Dojah:MaxVerificationAttempts"];
            return int.TryParse(v, out var parsed) && parsed > 0
                ? parsed
                : DefaultMaxVerificationAttempts;
        }

        private int GetCooldownHours()
        {
            var v = configuration?["Dojah:CooldownHoursAfterFailure"];
            return int.TryParse(v, out var parsed) && parsed >= 0
                ? parsed
                : DefaultCooldownHoursAfterFailure;
        }

        private async Task UpdateUserVerificationStateAsync(
            string userId,
            string userType,
            bool isVerified,
            string? verificationStatus)
        {
            var t = (userType ?? "Caregiver").Trim().ToLowerInvariant();
            if (t == "client")
            {
                await UpdateClientVerificationStateAsync(userId, isVerified, verificationStatus);
            }
            else
            {
                await UpdateCaregiverVerificationStateAsync(userId, isVerified, verificationStatus);
            }
        }

        private async Task UpdateClientVerificationStateAsync(
            string userId,
            bool isVerified,
            string? verificationStatus)
        {
            try
            {
                var client = await careProDbContext.Clients
                    .FirstOrDefaultAsync(c => c.Id.ToString() == userId);

                if (client == null)
                {
                    logger.LogWarning(
                        "Client not found for userId {UserId}, skipping profile verification update",
                        userId);
                    return;
                }

                client.IsIdentityVerified = isVerified;
                client.IdentityVerificationStatus = verificationStatus;
                if (isVerified)
                {
                    client.IdentityVerifiedAt = DateTime.UtcNow;
                }

                await careProDbContext.SaveChangesAsync();
                logger.LogInformation(
                    "Updated client profile verification state for UserId: {UserId}, IsIdentityVerified: {IsVerified}",
                    userId, isVerified);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Failed to update client profile verification state for UserId: {UserId}",
                    userId);
                // Swallow — verification record itself is already saved.
            }
        }
    }
}
