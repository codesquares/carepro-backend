using Application.DTOs;
using Application.Interfaces.Content;
using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using System;
using System.Text.Json;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Infrastructure.Content.Services
{
    public class AdminCaregiverService : IAdminCaregiverService
    {
        private readonly CareProDbContext _db;
        private readonly ILogger<AdminCaregiverService> _logger;

        public AdminCaregiverService(
            CareProDbContext db,
            ILogger<AdminCaregiverService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<AdminUpdateCaregiverNameResponse> UpdateCaregiverLegalNameAsync(
            string caregiverId,
            AdminUpdateCaregiverNameRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (!request.Confirmed)
                throw new ArgumentException("Name change must be explicitly confirmed (Confirmed must be true).", nameof(request.Confirmed));
            if (string.IsNullOrWhiteSpace(request.AdminId))
                throw new ArgumentException("AdminId is required", nameof(request.AdminId));
            if (string.IsNullOrWhiteSpace(request.FirstName))
                throw new ArgumentException("FirstName is required", nameof(request.FirstName));
            if (string.IsNullOrWhiteSpace(request.LastName))
                throw new ArgumentException("LastName is required", nameof(request.LastName));
            if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length < 5)
                throw new ArgumentException("A reason (min 5 chars) is required for an admin name edit", nameof(request.Reason));

            if (!ObjectId.TryParse(caregiverId, out var caregiverObjectId))
                throw new ArgumentException("Invalid Caregiver ID format.", nameof(caregiverId));

            var caregiver = await _db.CareGivers.FirstOrDefaultAsync(c => c.Id == caregiverObjectId);
            if (caregiver == null)
                throw new KeyNotFoundException($"Caregiver with ID '{caregiverId}' not found.");

            var newFirst = request.FirstName.Trim();
            var newMiddle = string.IsNullOrWhiteSpace(request.MiddleName) ? null : request.MiddleName.Trim();
            var newLast = request.LastName.Trim();

            // Snapshot previous values for audit + response BEFORE mutating
            var previousFirst = caregiver.FirstName;
            var previousMiddle = caregiver.MiddleName;
            var previousLast = caregiver.LastName;

            // No-op short-circuit: still recorded as audit if fields differ; if
            // nothing changed, return early without writing audit noise.
            if (string.Equals(previousFirst, newFirst, StringComparison.Ordinal)
                && string.Equals(previousMiddle, newMiddle, StringComparison.Ordinal)
                && string.Equals(previousLast, newLast, StringComparison.Ordinal))
            {
                return new AdminUpdateCaregiverNameResponse
                {
                    Success = true,
                    CaregiverId = caregiverId,
                    PreviousFirstName = previousFirst,
                    PreviousMiddleName = previousMiddle,
                    PreviousLastName = previousLast,
                    NewFirstName = newFirst,
                    NewMiddleName = newMiddle,
                    NewLastName = newLast,
                    AppUserUpdated = false,
                    Message = "No changes detected — caregiver name already matches the supplied values."
                };
            }

            caregiver.FirstName = newFirst;
            caregiver.MiddleName = newMiddle;
            caregiver.LastName = newLast;

            // Mirror to AppUser. AppUser.AppUserId == Caregiver.Id (verified
            // via CareGiverService.CreateCaregiverUserAsync). AppUser has no
            // MiddleName field, so only First/Last propagate.
            var appUserPreviousFirst = (string?)null;
            var appUserPreviousLast = (string?)null;
            var appUser = await _db.AppUsers.FirstOrDefaultAsync(u => u.AppUserId == caregiverObjectId);
            var appUserUpdated = false;
            if (appUser != null)
            {
                appUserPreviousFirst = appUser.FirstName;
                appUserPreviousLast = appUser.LastName;
                appUser.FirstName = newFirst;
                appUser.LastName = newLast;
                appUserUpdated = true;
            }
            else
            {
                _logger.LogWarning(
                    "AppUser not found for Caregiver {CaregiverId} during admin name edit — caregiver record updated, AppUser skipped.",
                    caregiverId);
            }

            await _db.SaveChangesAsync();

            var before = new
            {
                Caregiver = new
                {
                    FirstName = previousFirst,
                    MiddleName = previousMiddle,
                    LastName = previousLast
                },
                AppUser = appUser == null ? null : new
                {
                    FirstName = appUserPreviousFirst,
                    LastName = appUserPreviousLast
                }
            };
            var after = new
            {
                Caregiver = new
                {
                    FirstName = newFirst,
                    MiddleName = newMiddle,
                    LastName = newLast
                },
                AppUser = appUser == null ? null : new
                {
                    FirstName = newFirst,
                    LastName = newLast
                }
            };

            await _db.AdminAuditLogs.AddAsync(new AdminAuditLog
            {
                Id = ObjectId.GenerateNewId(),
                AdminId = request.AdminId,
                TargetEntityType = "Caregiver",
                TargetEntityId = caregiverId,
                TargetUserId = caregiverId,
                Action = "CaregiverNameEdit",
                BeforeJson = JsonSerializer.Serialize(before),
                AfterJson = JsonSerializer.Serialize(after),
                Reason = request.Reason.Trim(),
                Timestamp = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "Admin {AdminId} edited legal name for Caregiver {CaregiverId}. AppUserUpdated={AppUserUpdated}",
                request.AdminId, caregiverId, appUserUpdated);

            return new AdminUpdateCaregiverNameResponse
            {
                Success = true,
                CaregiverId = caregiverId,
                PreviousFirstName = previousFirst,
                PreviousMiddleName = previousMiddle,
                PreviousLastName = previousLast,
                NewFirstName = newFirst,
                NewMiddleName = newMiddle,
                NewLastName = newLast,
                AppUserUpdated = appUserUpdated,
                Message = appUserUpdated
                    ? "Caregiver name updated. Linked AppUser FirstName/LastName also updated."
                    : "Caregiver name updated. No matching AppUser was found to mirror First/Last name."
            };
        }

        public async Task<AdminBulkClearMiddleNameResponse> BulkClearCaregiverMiddleNameAsync(
            AdminBulkClearMiddleNameRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.AdminId))
                throw new ArgumentException("AdminId is required", nameof(request.AdminId));
            if (request.UserIds == null || request.UserIds.Count == 0)
                throw new ArgumentException("At least one user ID is required", nameof(request.UserIds));
            if (request.UserIds.Count > 500)
                throw new ArgumentException("Maximum 500 IDs per request", nameof(request.UserIds));
            if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length < 5)
                throw new ArgumentException("A reason (min 5 chars) is required", nameof(request.Reason));

            var cleared = 0;
            var skipped = 0;
            var failedIds = new List<string>();

            foreach (var rawId in request.UserIds)
            {
                if (!ObjectId.TryParse(rawId, out var objectId))
                {
                    failedIds.Add(rawId);
                    continue;
                }

                try
                {
                    var caregiver = await _db.CareGivers.FirstOrDefaultAsync(c => c.Id == objectId);
                    if (caregiver == null || string.IsNullOrWhiteSpace(caregiver.MiddleName))
                    {
                        skipped++;
                        continue;
                    }

                    var previous = caregiver.MiddleName;
                    caregiver.MiddleName = null;

                    await _db.AdminAuditLogs.AddAsync(new AdminAuditLog
                    {
                        Id = ObjectId.GenerateNewId(),
                        AdminId = request.AdminId,
                        TargetEntityType = "Caregiver",
                        TargetEntityId = rawId,
                        TargetUserId = rawId,
                        Action = "BulkClearMiddleName",
                        BeforeJson = JsonSerializer.Serialize(new { MiddleName = previous }),
                        AfterJson = JsonSerializer.Serialize(new { MiddleName = (string?)null }),
                        Reason = request.Reason.Trim(),
                        Timestamp = DateTime.UtcNow
                    });

                    cleared++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to clear MiddleName for Caregiver {Id}", rawId);
                    failedIds.Add(rawId);
                }
            }

            if (cleared > 0)
                await _db.SaveChangesAsync();

            _logger.LogInformation(
                "Admin {AdminId} bulk-cleared caregiver MiddleName. Cleared={Cleared}, Skipped={Skipped}, Failed={Failed}",
                request.AdminId, cleared, skipped, failedIds.Count);

            return new AdminBulkClearMiddleNameResponse
            {
                Cleared = cleared,
                Skipped = skipped,
                Failed = failedIds.Count,
                FailedIds = failedIds,
                Message = $"Done. {cleared} cleared, {skipped} skipped (already null), {failedIds.Count} failed."
            };
        }
    }
}
