using Application.DTOs;
using Application.Interfaces.Content;
using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
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

        public VerificationService(CareProDbContext careProDbContext, ICareGiverService careGiverService, ILogger<VerificationService> logger)
        {
            this.careProDbContext = careProDbContext;
            this.careGiverService = careGiverService;
            this.logger = logger;
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

                bool isVerified = addVerificationRequest.VerificationStatus?.ToLower() == "completed" ||
                                  addVerificationRequest.VerificationStatus?.ToLower() == "verified" ||
                                  addVerificationRequest.VerificationStatus?.ToLower() == "success";

                if (existingVerification != null)
                {
                    // Update existing verification instead of throwing error
                    logger.LogInformation("Updating existing verification for UserId: {UserId}", addVerificationRequest.UserId);

                    existingVerification.VerificationMethod = addVerificationRequest.VerificationMethod;
                    existingVerification.VerificationStatus = addVerificationRequest.VerificationStatus;
                    existingVerification.VerificationNo = addVerificationRequest.VerificationNo;
                    existingVerification.UpdatedOn = DateTime.UtcNow;
                    existingVerification.IsVerified = isVerified;

                    await careProDbContext.SaveChangesAsync();

                    logger.LogInformation("Successfully updated verification for UserId: {UserId} with status: {Status}",
                        addVerificationRequest.UserId, addVerificationRequest.VerificationStatus);

                    // Update caregiver profile with verification state
                    await UpdateCaregiverVerificationStateAsync(addVerificationRequest.UserId, isVerified, addVerificationRequest.VerificationStatus);

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
                };

                await careProDbContext.Verifications.AddAsync(verification);
                await careProDbContext.SaveChangesAsync();

                logger.LogInformation("Successfully created verification for UserId: {UserId} with status: {Status}",
                    addVerificationRequest.UserId, addVerificationRequest.VerificationStatus);

                // Update caregiver profile with verification state
                await UpdateCaregiverVerificationStateAsync(addVerificationRequest.UserId, isVerified, addVerificationRequest.VerificationStatus);

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
    }
}
