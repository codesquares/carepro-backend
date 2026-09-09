using Application.DTOs;
using Application.Interfaces.Content;
using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Infrastructure.Content.Services
{
    /// <summary>
    /// Phase 2 caregiver-vetting data capture. Follows the same conventions as
    /// <see cref="CaregiverProfileService"/>: ArgumentException for bad input,
    /// KeyNotFoundException for missing records, ownership always enforced via
    /// the caller-supplied caregiver id (sourced from the JWT by the controller).
    /// </summary>
    public class CaregiverVettingService : ICaregiverVettingService
    {
        private readonly CareProDbContext db;
        private readonly ILogger<CaregiverVettingService> logger;

        public CaregiverVettingService(CareProDbContext db, ILogger<CaregiverVettingService> logger)
        {
            this.db = db;
            this.logger = logger;
        }

        private static ObjectId ParseCaregiverId(string caregiverId)
        {
            if (string.IsNullOrWhiteSpace(caregiverId) || !ObjectId.TryParse(caregiverId, out var oid))
                throw new ArgumentException($"Invalid caregiver id '{caregiverId}'.");
            return oid;
        }

        private async Task<Caregiver> GetCaregiverAsync(string caregiverId)
        {
            var oid = ParseCaregiverId(caregiverId);
            return await db.CareGivers.FirstOrDefaultAsync(c => c.Id == oid)
                ?? throw new KeyNotFoundException($"Caregiver '{caregiverId}' not found.");
        }

        // ───────────────────── 2.1  CLASSIFICATION ─────────────────────

        public async Task<CaregiverClassificationResponse> GetClassificationAsync(string caregiverId)
        {
            var caregiver = await GetCaregiverAsync(caregiverId);
            return Map(caregiver);
        }

        public async Task<CaregiverClassificationResponse> SetClassificationAsync(
            string caregiverId, SetCaregiverClassificationRequest request)
        {
            if (request == null) throw new ArgumentException("Request body is required.");

            if (!Enum.TryParse<CaregiverType>(request.CaregiverType, ignoreCase: false, out var type)
                || !Enum.IsDefined(typeof(CaregiverType), type))
            {
                throw new ArgumentException("CaregiverType must be one of: AuxiliaryNurse, CHEW, RegisteredNurse.");
            }

            var caregiver = await GetCaregiverAsync(caregiverId);

            caregiver.CaregiverType = type;
            // Specialty is stored as a general nullable field regardless of type,
            // but a blank string is normalised to null.
            caregiver.Specialty = string.IsNullOrWhiteSpace(request.Specialty)
                ? null
                : request.Specialty.Trim();

            await db.SaveChangesAsync();
            logger.LogInformation(
                "Caregiver {CaregiverId} classification set to {CaregiverType} (specialty: {Specialty})",
                caregiverId, type, caregiver.Specialty ?? "none");

            return Map(caregiver);
        }

        private static CaregiverClassificationResponse Map(Caregiver c) => new()
        {
            CaregiverId = c.Id.ToString(),
            CaregiverType = c.CaregiverType?.ToString(),
            Specialty = c.Specialty,
        };

        // ───────────────────── 9.1  EXPERIENCE TIER (payroll) ─────────────────────

        public async Task<CaregiverExperienceTierResponse> GetExperienceTierAsync(string caregiverId)
        {
            var caregiver = await GetCaregiverAsync(caregiverId);
            return MapExperienceTier(caregiver);
        }

        public async Task<CaregiverExperienceTierResponse> SetExperienceTierAsync(
            string caregiverId, SetCaregiverExperienceTierRequest request)
        {
            if (request == null) throw new ArgumentException("Request body is required.");

            if (!Enum.TryParse<ExperienceTier>(request.ExperienceTier, ignoreCase: false, out var tier)
                || !Enum.IsDefined(typeof(ExperienceTier), tier))
            {
                throw new ArgumentException("ExperienceTier must be one of: Junior, Mid, Senior.");
            }

            var caregiver = await GetCaregiverAsync(caregiverId);
            caregiver.ExperienceTier = tier;

            await db.SaveChangesAsync();
            logger.LogInformation(
                "Caregiver {CaregiverId} experience tier set to {ExperienceTier}", caregiverId, tier);

            return MapExperienceTier(caregiver);
        }

        private static CaregiverExperienceTierResponse MapExperienceTier(Caregiver c) => new()
        {
            CaregiverId = c.Id.ToString(),
            ExperienceTier = c.ExperienceTier?.ToString(),
        };

        // ───────────────────── 2.3  ADDRESS HISTORY ─────────────────────

        public async Task<IEnumerable<CaregiverAddressHistoryResponse>> GetAddressHistoryAsync(string caregiverId)
        {
            await GetCaregiverAsync(caregiverId);
            var items = await db.CaregiverAddressHistories
                .Where(a => a.CaregiverId == caregiverId)
                .ToListAsync();
            return items
                .OrderByDescending(a => a.MovedOut ?? DateTime.MaxValue)
                .ThenByDescending(a => a.MovedIn)
                .Select(Map)
                .ToList();
        }

        public async Task<CaregiverAddressHistoryResponse> AddAddressHistoryAsync(
            string caregiverId, AddCaregiverAddressHistoryRequest request)
        {
            if (request == null) throw new ArgumentException("Request body is required.");
            await GetCaregiverAsync(caregiverId);
            ValidateAddressPeriod(request.MovedIn, request.MovedOut);

            var now = DateTime.UtcNow;
            var entity = new CaregiverAddressHistory
            {
                Id = ObjectId.GenerateNewId(),
                CaregiverId = caregiverId,
                Address = request.Address.Trim(),
                MovedIn = request.MovedIn,
                MovedOut = request.MovedOut,
                CreatedAt = now,
                UpdatedAt = now,
            };

            db.CaregiverAddressHistories.Add(entity);
            await db.SaveChangesAsync();
            logger.LogInformation("Address-history record {Id} added for caregiver {CaregiverId}", entity.Id, caregiverId);
            return Map(entity);
        }

        public async Task<CaregiverAddressHistoryResponse> UpdateAddressHistoryAsync(
            string caregiverId, string id, UpdateCaregiverAddressHistoryRequest request)
        {
            if (request == null) throw new ArgumentException("Request body is required.");
            if (string.IsNullOrWhiteSpace(id) || !ObjectId.TryParse(id, out var oid))
                throw new ArgumentException($"Invalid address-history id '{id}'.");

            var entity = await db.CaregiverAddressHistories.FirstOrDefaultAsync(a => a.Id == oid)
                ?? throw new KeyNotFoundException($"Address-history record '{id}' not found.");
            if (!string.Equals(entity.CaregiverId, caregiverId, StringComparison.Ordinal))
                throw new UnauthorizedAccessException("You are not authorised to access this address-history record.");

            ValidateAddressPeriod(request.MovedIn, request.MovedOut);

            entity.Address = request.Address.Trim();
            entity.MovedIn = request.MovedIn;
            entity.MovedOut = request.MovedOut;
            entity.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync();
            return Map(entity);
        }

        public async Task DeleteAddressHistoryAsync(string caregiverId, string id)
        {
            if (string.IsNullOrWhiteSpace(id) || !ObjectId.TryParse(id, out var oid))
                throw new ArgumentException($"Invalid address-history id '{id}'.");

            var entity = await db.CaregiverAddressHistories.FirstOrDefaultAsync(a => a.Id == oid)
                ?? throw new KeyNotFoundException($"Address-history record '{id}' not found.");
            if (!string.Equals(entity.CaregiverId, caregiverId, StringComparison.Ordinal))
                throw new UnauthorizedAccessException("You are not authorised to access this address-history record.");

            db.CaregiverAddressHistories.Remove(entity);
            await db.SaveChangesAsync();
        }

        public async Task<CaregiverAddressHistoryCoverageResponse> GetAddressHistoryCoverageAsync(string caregiverId)
        {
            await GetCaregiverAsync(caregiverId);
            var items = await db.CaregiverAddressHistories
                .Where(a => a.CaregiverId == caregiverId)
                .ToListAsync();

            return new CaregiverAddressHistoryCoverageResponse
            {
                IsComplete = CaregiverAddressHistoryCoverage.IsComplete(items, DateTime.UtcNow),
                RecordCount = items.Count,
                RequiredCount = CaregiverAddressHistoryCoverage.RequiredCount,
                RequiredYears = CaregiverAddressHistoryCoverage.RequiredYears,
            };
        }

        private static void ValidateAddressPeriod(DateTime movedIn, DateTime? movedOut)
        {
            if (movedIn == default)
                throw new ArgumentException("MovedIn is required.");
            if (movedIn > DateTime.UtcNow.AddDays(1))
                throw new ArgumentException("MovedIn cannot be in the future.");
            if (movedOut.HasValue && movedOut.Value < movedIn)
                throw new ArgumentException("MovedOut cannot be earlier than MovedIn.");
        }

        private static CaregiverAddressHistoryResponse Map(CaregiverAddressHistory a) => new()
        {
            Id = a.Id.ToString(),
            CaregiverId = a.CaregiverId,
            Address = a.Address,
            MovedIn = a.MovedIn,
            MovedOut = a.MovedOut,
            CreatedAt = a.CreatedAt,
            UpdatedAt = a.UpdatedAt,
        };

        // ───────────────────── 2.4  SOCIAL MEDIA HANDLES ─────────────────────

        private static readonly HashSet<string> AllowedSocialPlatforms = new(StringComparer.OrdinalIgnoreCase)
        {
            "Facebook", "Instagram", "X", "Twitter", "LinkedIn", "TikTok", "Snapchat", "YouTube", "Threads", "Other"
        };

        public async Task<IEnumerable<CaregiverSocialMediaHandleResponse>> GetSocialMediaHandlesAsync(string caregiverId)
        {
            await GetCaregiverAsync(caregiverId);
            var items = await db.CaregiverSocialMediaHandles
                .Where(s => s.CaregiverId == caregiverId)
                .ToListAsync();
            return items
                .OrderBy(s => s.Platform)
                .ThenBy(s => s.CreatedAt)
                .Select(Map)
                .ToList();
        }

        public async Task<CaregiverSocialMediaHandleResponse> AddSocialMediaHandleAsync(
            string caregiverId, AddCaregiverSocialMediaHandleRequest request)
        {
            if (request == null) throw new ArgumentException("Request body is required.");
            await GetCaregiverAsync(caregiverId);
            var platform = NormalisePlatform(request.Platform);

            var now = DateTime.UtcNow;
            var entity = new CaregiverSocialMediaHandle
            {
                Id = ObjectId.GenerateNewId(),
                CaregiverId = caregiverId,
                Platform = platform,
                Handle = request.Handle.Trim(),
                CreatedAt = now,
                UpdatedAt = now,
            };

            db.CaregiverSocialMediaHandles.Add(entity);
            await db.SaveChangesAsync();
            logger.LogInformation("Social-media handle {Id} ({Platform}) added for caregiver {CaregiverId}",
                entity.Id, platform, caregiverId);
            return Map(entity);
        }

        public async Task<CaregiverSocialMediaHandleResponse> UpdateSocialMediaHandleAsync(
            string caregiverId, string id, UpdateCaregiverSocialMediaHandleRequest request)
        {
            if (request == null) throw new ArgumentException("Request body is required.");
            if (string.IsNullOrWhiteSpace(id) || !ObjectId.TryParse(id, out var oid))
                throw new ArgumentException($"Invalid social-media handle id '{id}'.");

            var entity = await db.CaregiverSocialMediaHandles.FirstOrDefaultAsync(s => s.Id == oid)
                ?? throw new KeyNotFoundException($"Social-media handle '{id}' not found.");
            if (!string.Equals(entity.CaregiverId, caregiverId, StringComparison.Ordinal))
                throw new UnauthorizedAccessException("You are not authorised to access this social-media handle.");

            entity.Platform = NormalisePlatform(request.Platform);
            entity.Handle = request.Handle.Trim();
            entity.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync();
            return Map(entity);
        }

        public async Task DeleteSocialMediaHandleAsync(string caregiverId, string id)
        {
            if (string.IsNullOrWhiteSpace(id) || !ObjectId.TryParse(id, out var oid))
                throw new ArgumentException($"Invalid social-media handle id '{id}'.");

            var entity = await db.CaregiverSocialMediaHandles.FirstOrDefaultAsync(s => s.Id == oid)
                ?? throw new KeyNotFoundException($"Social-media handle '{id}' not found.");
            if (!string.Equals(entity.CaregiverId, caregiverId, StringComparison.Ordinal))
                throw new UnauthorizedAccessException("You are not authorised to access this social-media handle.");

            db.CaregiverSocialMediaHandles.Remove(entity);
            await db.SaveChangesAsync();
        }

        private static string NormalisePlatform(string platform)
        {
            if (string.IsNullOrWhiteSpace(platform) || !AllowedSocialPlatforms.Contains(platform.Trim()))
                throw new ArgumentException($"Platform must be one of: {string.Join(", ", AllowedSocialPlatforms)}.");
            // Canonicalise casing to the allow-list entry.
            return AllowedSocialPlatforms.First(p => string.Equals(p, platform.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        private static CaregiverSocialMediaHandleResponse Map(CaregiverSocialMediaHandle s) => new()
        {
            Id = s.Id.ToString(),
            CaregiverId = s.CaregiverId,
            Platform = s.Platform,
            Handle = s.Handle,
            CreatedAt = s.CreatedAt,
            UpdatedAt = s.UpdatedAt,
        };
    }
}
