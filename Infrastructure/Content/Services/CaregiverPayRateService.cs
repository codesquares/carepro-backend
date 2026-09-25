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
    /// Admin CRUD for the <see cref="CaregiverPayRate"/> table (Phase 9.3). Follows the
    /// same conventions as <see cref="PackageService"/>: ArgumentException for bad input,
    /// KeyNotFoundException for a missing record, bool returns for update/delete/toggle.
    ///
    /// Uniqueness is enforced only among IsActive rates: a (CaregiverType, ExperienceTier)
    /// pair may have at most one active rate at a time, but a superseded rate can be kept
    /// around deactivated rather than deleted (matches Package's IsActive-as-current-effective
    /// convention, not RequiredCaregiverType's stricter always-on validation).
    /// </summary>
    public class CaregiverPayRateService : ICaregiverPayRateService
    {
        private readonly CareProDbContext _context;
        private readonly ILogger<CaregiverPayRateService> _logger;

        public CaregiverPayRateService(CareProDbContext context, ILogger<CaregiverPayRateService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<CaregiverPayRateDTO> CreatePayRateAsync(AddCaregiverPayRateRequest request)
        {
            if (request == null) throw new ArgumentException("Request body is required");

            var caregiverType = ParseCaregiverType(request.CaregiverType);
            var experienceTier = ParseExperienceTier(request.ExperienceTier);

            if (request.HourlyRate < 0)
                throw new ArgumentException("HourlyRate cannot be negative");

            if (request.IsActive)
                await EnsureNoActiveDuplicateAsync(caregiverType, experienceTier, excludingId: null);

            var now = DateTime.UtcNow;
            var entity = new CaregiverPayRate
            {
                Id = ObjectId.GenerateNewId(),
                CaregiverType = caregiverType,
                ExperienceTier = experienceTier,
                HourlyRate = request.HourlyRate,
                IsActive = request.IsActive,
                CreatedAt = now,
                UpdatedAt = now,
            };

            _context.CaregiverPayRates.Add(entity);
            await _context.SaveChangesAsync();
            _logger.LogInformation("CaregiverPayRate {Id} created: {Type}/{Tier} = {Rate}",
                entity.Id, entity.CaregiverType, entity.ExperienceTier, entity.HourlyRate);
            return MapToDTO(entity);
        }

        public async Task<CaregiverPayRateDTO?> GetPayRateByIdAsync(string id)
        {
            if (!ObjectId.TryParse(id, out var oid))
                return null;

            var entity = await _context.CaregiverPayRates.FirstOrDefaultAsync(r => r.Id == oid);
            return entity != null ? MapToDTO(entity) : null;
        }

        public async Task<List<CaregiverPayRateDTO>> GetAllPayRatesAsync()
        {
            var rates = await _context.CaregiverPayRates
                .OrderBy(r => r.CaregiverType)
                .ThenBy(r => r.ExperienceTier)
                .ThenByDescending(r => r.CreatedAt)
                .ToListAsync();
            return rates.Select(MapToDTO).ToList();
        }

        public async Task<bool> UpdatePayRateAsync(UpdateCaregiverPayRateRequest request)
        {
            if (request == null) throw new ArgumentException("Request body is required");
            if (!ObjectId.TryParse(request.Id, out var oid))
                throw new ArgumentException("Invalid pay rate ID format");

            var entity = await _context.CaregiverPayRates.FirstOrDefaultAsync(r => r.Id == oid)
                ?? throw new KeyNotFoundException($"CaregiverPayRate with ID '{request.Id}' not found");

            if (!string.IsNullOrWhiteSpace(request.CaregiverType))
                entity.CaregiverType = ParseCaregiverType(request.CaregiverType);

            if (!string.IsNullOrWhiteSpace(request.ExperienceTier))
                entity.ExperienceTier = ParseExperienceTier(request.ExperienceTier);

            if (request.HourlyRate.HasValue)
            {
                if (request.HourlyRate.Value < 0) throw new ArgumentException("HourlyRate cannot be negative");
                entity.HourlyRate = request.HourlyRate.Value;
            }

            if (request.IsActive.HasValue)
                entity.IsActive = request.IsActive.Value;

            if (entity.IsActive)
                await EnsureNoActiveDuplicateAsync(entity.CaregiverType, entity.ExperienceTier, excludingId: oid);

            entity.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            _logger.LogInformation("CaregiverPayRate {Id} updated", request.Id);
            return true;
        }

        public async Task<bool> DeletePayRateAsync(string id)
        {
            if (!ObjectId.TryParse(id, out var oid))
                throw new ArgumentException("Invalid pay rate ID format");

            var entity = await _context.CaregiverPayRates.FirstOrDefaultAsync(r => r.Id == oid)
                ?? throw new KeyNotFoundException($"CaregiverPayRate with ID '{id}' not found");

            _context.CaregiverPayRates.Remove(entity);
            await _context.SaveChangesAsync();
            _logger.LogInformation("CaregiverPayRate {Id} deleted", id);
            return true;
        }

        public async Task<bool> ToggleActiveStatusAsync(string id, bool isActive)
        {
            if (!ObjectId.TryParse(id, out var oid))
                throw new ArgumentException("Invalid pay rate ID format");

            var entity = await _context.CaregiverPayRates.FirstOrDefaultAsync(r => r.Id == oid)
                ?? throw new KeyNotFoundException($"CaregiverPayRate with ID '{id}' not found");

            if (isActive)
                await EnsureNoActiveDuplicateAsync(entity.CaregiverType, entity.ExperienceTier, excludingId: oid);

            entity.IsActive = isActive;
            entity.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return true;
        }

        // ── Helpers ──

        private async Task EnsureNoActiveDuplicateAsync(CaregiverType type, ExperienceTier tier, ObjectId? excludingId)
        {
            var duplicate = await _context.CaregiverPayRates.AnyAsync(r =>
                r.CaregiverType == type && r.ExperienceTier == tier && r.IsActive &&
                (excludingId == null || r.Id != excludingId));

            if (duplicate)
                throw new ArgumentException(
                    $"An active pay rate already exists for {type}/{tier}. Deactivate it first.");
        }

        private static CaregiverType ParseCaregiverType(string value)
        {
            if (!Enum.TryParse<CaregiverType>(value?.Trim(), ignoreCase: false, out var parsed)
                || !Enum.IsDefined(typeof(CaregiverType), parsed))
            {
                throw new ArgumentException("CaregiverType must be one of: AuxiliaryNurse, CHEW, RegisteredNurse");
            }
            return parsed;
        }

        private static ExperienceTier ParseExperienceTier(string value)
        {
            if (!Enum.TryParse<ExperienceTier>(value?.Trim(), ignoreCase: false, out var parsed)
                || !Enum.IsDefined(typeof(ExperienceTier), parsed))
            {
                throw new ArgumentException("ExperienceTier must be one of: Junior, Mid, Senior");
            }
            return parsed;
        }

        private static CaregiverPayRateDTO MapToDTO(CaregiverPayRate r) => new()
        {
            Id = r.Id.ToString(),
            CaregiverType = r.CaregiverType.ToString(),
            ExperienceTier = r.ExperienceTier.ToString(),
            HourlyRate = r.HourlyRate,
            IsActive = r.IsActive,
            CreatedAt = r.CreatedAt,
            UpdatedAt = r.UpdatedAt,
        };
    }
}
