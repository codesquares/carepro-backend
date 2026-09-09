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
    /// Admin CRUD for care <see cref="Package"/> variants. Follows the same
    /// conventions as <see cref="TrainingMaterialService"/>: ArgumentException for
    /// bad input, KeyNotFoundException for a missing record, bool returns for
    /// update/delete/toggle.
    /// </summary>
    public class PackageService : IPackageService
    {
        private readonly CareProDbContext _context;
        private readonly ILogger<PackageService> _logger;

        public PackageService(CareProDbContext context, ILogger<PackageService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<PackageDTO> CreatePackageAsync(AddPackageRequest request)
        {
            if (request == null) throw new ArgumentException("Request body is required");

            var category = NormaliseCategory(request.Category);
            var requiredType = ParseCaregiverType(request.RequiredCaregiverType);
            var payCalculationType = ParsePayCalculationType(request.PayCalculationType);

            if (string.IsNullOrWhiteSpace(request.TierLabel))
                throw new ArgumentException("TierLabel is required");
            if (request.BasePrice < 0)
                throw new ArgumentException("BasePrice cannot be negative");
            if (request.AdditionalDayPrice is < 0)
                throw new ArgumentException("AdditionalDayPrice cannot be negative");
            ValidateFixedCaregiverPay(payCalculationType, request.FixedCaregiverPay);

            var now = DateTime.UtcNow;
            var entity = new Package
            {
                Id = ObjectId.GenerateNewId(),
                Category = category,
                TierLabel = request.TierLabel.Trim(),
                RequiredCaregiverType = requiredType,
                RequiredSpecialty = string.IsNullOrWhiteSpace(request.RequiredSpecialty)
                    ? null
                    : request.RequiredSpecialty.Trim(),
                BasePrice = request.BasePrice,
                AdditionalDayPrice = request.AdditionalDayPrice,
                PayCalculationType = payCalculationType,
                FixedCaregiverPay = payCalculationType == Domain.Entities.PayCalculationType.Fixed
                    ? request.FixedCaregiverPay
                    : null,
                Description = request.Description?.Trim() ?? string.Empty,
                IsActive = request.IsActive,
                CreatedAt = now,
                UpdatedAt = now,
            };

            _context.Packages.Add(entity);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Package {Id} created: {Category} / {Tier} ({Type})",
                entity.Id, entity.Category, entity.TierLabel, entity.RequiredCaregiverType);
            return MapToDTO(entity);
        }

        public async Task<PackageDTO?> GetPackageByIdAsync(string id)
        {
            if (!ObjectId.TryParse(id, out var oid))
                return null;

            var entity = await _context.Packages.FirstOrDefaultAsync(p => p.Id == oid);
            return entity != null ? MapToDTO(entity) : null;
        }

        public async Task<List<PackageDTO>> GetAllPackagesAsync()
        {
            var packages = await _context.Packages
                .OrderBy(p => p.Category)
                .ThenByDescending(p => p.CreatedAt)
                .ToListAsync();
            return packages.Select(MapToDTO).ToList();
        }

        public async Task<bool> UpdatePackageAsync(UpdatePackageRequest request)
        {
            if (request == null) throw new ArgumentException("Request body is required");
            if (!ObjectId.TryParse(request.Id, out var oid))
                throw new ArgumentException("Invalid package ID format");

            var entity = await _context.Packages.FirstOrDefaultAsync(p => p.Id == oid)
                ?? throw new KeyNotFoundException($"Package with ID '{request.Id}' not found");

            if (!string.IsNullOrWhiteSpace(request.Category))
                entity.Category = NormaliseCategory(request.Category);

            if (!string.IsNullOrWhiteSpace(request.TierLabel))
                entity.TierLabel = request.TierLabel.Trim();

            if (!string.IsNullOrWhiteSpace(request.RequiredCaregiverType))
                entity.RequiredCaregiverType = ParseCaregiverType(request.RequiredCaregiverType);

            // Non-null (including "") applies; null leaves unchanged. Blank clears the specialty.
            if (request.RequiredSpecialty != null)
                entity.RequiredSpecialty = string.IsNullOrWhiteSpace(request.RequiredSpecialty)
                    ? null
                    : request.RequiredSpecialty.Trim();

            if (request.BasePrice.HasValue)
            {
                if (request.BasePrice.Value < 0) throw new ArgumentException("BasePrice cannot be negative");
                entity.BasePrice = request.BasePrice.Value;
            }

            if (request.AdditionalDayPrice.HasValue)
            {
                if (request.AdditionalDayPrice.Value < 0) throw new ArgumentException("AdditionalDayPrice cannot be negative");
                entity.AdditionalDayPrice = request.AdditionalDayPrice.Value;
            }

            if (!string.IsNullOrWhiteSpace(request.PayCalculationType))
                entity.PayCalculationType = ParsePayCalculationType(request.PayCalculationType);

            if (request.FixedCaregiverPay.HasValue)
            {
                // Evaluated against the type *after* any PayCalculationType change above,
                // so switching to Fixed and supplying the amount in the same request works.
                if (entity.PayCalculationType != Domain.Entities.PayCalculationType.Fixed)
                    throw new ArgumentException("FixedCaregiverPay can only be set when PayCalculationType is Fixed");
                if (request.FixedCaregiverPay.Value < 0)
                    throw new ArgumentException("FixedCaregiverPay cannot be negative");
                entity.FixedCaregiverPay = request.FixedCaregiverPay.Value;
            }

            // Switching to Hourly always clears any leftover FixedCaregiverPay from a
            // prior Fixed state — it's only ever meaningful for Fixed, and this is a
            // payroll-driving field so the invariant is enforced, not left to the caller
            // (unlike RequiredSpecialty). Staying/becoming Fixed requires a positive value
            // to already exist or have been supplied above.
            if (entity.PayCalculationType == Domain.Entities.PayCalculationType.Hourly)
                entity.FixedCaregiverPay = null;
            else if (entity.PayCalculationType == Domain.Entities.PayCalculationType.Fixed)
                ValidateFixedCaregiverPay(Domain.Entities.PayCalculationType.Fixed, entity.FixedCaregiverPay);

            if (request.Description != null)
                entity.Description = request.Description.Trim();

            if (request.IsActive.HasValue)
                entity.IsActive = request.IsActive.Value;

            entity.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            _logger.LogInformation("Package {Id} updated", request.Id);
            return true;
        }

        public async Task<bool> DeletePackageAsync(string id)
        {
            if (!ObjectId.TryParse(id, out var oid))
                throw new ArgumentException("Invalid package ID format");

            var entity = await _context.Packages.FirstOrDefaultAsync(p => p.Id == oid)
                ?? throw new KeyNotFoundException($"Package with ID '{id}' not found");

            _context.Packages.Remove(entity);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Package {Id} deleted", id);
            return true;
        }

        public async Task<bool> ToggleActiveStatusAsync(string id, bool isActive)
        {
            if (!ObjectId.TryParse(id, out var oid))
                throw new ArgumentException("Invalid package ID format");

            var entity = await _context.Packages.FirstOrDefaultAsync(p => p.Id == oid)
                ?? throw new KeyNotFoundException($"Package with ID '{id}' not found");

            entity.IsActive = isActive;
            entity.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return true;
        }

        // ── Helpers ──

        private static string NormaliseCategory(string category)
        {
            if (string.IsNullOrWhiteSpace(category))
                throw new ArgumentException("Category is required");
            var match = PackageCategories.All
                .FirstOrDefault(c => string.Equals(c, category.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match == null)
                throw new ArgumentException(
                    $"Category must be one of: {string.Join(", ", PackageCategories.All)}");
            return match;
        }

        private static CaregiverType ParseCaregiverType(string value)
        {
            if (!Enum.TryParse<CaregiverType>(value?.Trim(), ignoreCase: false, out var parsed)
                || !Enum.IsDefined(typeof(CaregiverType), parsed))
            {
                throw new ArgumentException(
                    "RequiredCaregiverType must be one of: AuxiliaryNurse, CHEW, RegisteredNurse");
            }
            return parsed;
        }

        private static Domain.Entities.PayCalculationType ParsePayCalculationType(string value)
        {
            if (!Enum.TryParse<Domain.Entities.PayCalculationType>(value?.Trim(), ignoreCase: false, out var parsed)
                || !Enum.IsDefined(typeof(Domain.Entities.PayCalculationType), parsed))
            {
                throw new ArgumentException("PayCalculationType must be one of: Hourly, Fixed");
            }
            return parsed;
        }

        private static void ValidateFixedCaregiverPay(Domain.Entities.PayCalculationType payCalculationType, decimal? fixedCaregiverPay)
        {
            if (payCalculationType == Domain.Entities.PayCalculationType.Fixed)
            {
                if (!fixedCaregiverPay.HasValue || fixedCaregiverPay.Value <= 0)
                    throw new ArgumentException("FixedCaregiverPay is required and must be greater than 0 when PayCalculationType is Fixed");
            }
            else if (fixedCaregiverPay.HasValue)
            {
                throw new ArgumentException("FixedCaregiverPay must not be set when PayCalculationType is Hourly");
            }
        }

        private static PackageDTO MapToDTO(Package p) => new()
        {
            Id = p.Id.ToString(),
            Category = p.Category,
            TierLabel = p.TierLabel,
            RequiredCaregiverType = p.RequiredCaregiverType.ToString(),
            RequiredSpecialty = p.RequiredSpecialty,
            BasePrice = p.BasePrice,
            AdditionalDayPrice = p.AdditionalDayPrice,
            PayCalculationType = p.PayCalculationType?.ToString(),
            FixedCaregiverPay = p.FixedCaregiverPay,
            Description = p.Description,
            IsActive = p.IsActive,
            CreatedAt = p.CreatedAt,
            UpdatedAt = p.UpdatedAt,
        };
    }
}
