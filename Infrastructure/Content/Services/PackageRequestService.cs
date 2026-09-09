using Application.DTOs;
using Application.Interfaces.Content;
using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using System;
using System.Threading.Tasks;

namespace Infrastructure.Content.Services
{
    public class PackageRequestService : IPackageRequestService
    {
        private readonly CareProDbContext _db;
        private readonly ILogger<PackageRequestService> _logger;

        public PackageRequestService(CareProDbContext db, ILogger<PackageRequestService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<PackageRequestDTO> CreateAsync(string clientId, CreatePackageRequestRequest request)
        {
            if (request == null) throw new ArgumentException("Request body is required.");
            if (string.IsNullOrWhiteSpace(clientId)) throw new ArgumentException("Client identity is required.");
            if (!ObjectId.TryParse(request.PackageId, out var packageOid))
                throw new ArgumentException("Invalid package id.");

            var package = await _db.Packages.FirstOrDefaultAsync(p => p.Id == packageOid)
                ?? throw new KeyNotFoundException($"Package '{request.PackageId}' not found.");
            if (!package.IsActive)
                throw new InvalidOperationException("This package is not currently available.");

            var now = DateTime.UtcNow;
            var entity = new PackageRequest
            {
                Id = ObjectId.GenerateNewId(),
                ClientId = clientId,
                PackageId = package.Id.ToString(),
                PackageCategory = package.Category,
                PackageTierLabel = package.TierLabel,
                RequiredCaregiverType = package.RequiredCaregiverType,
                RequiredSpecialty = package.RequiredSpecialty,
                ServiceCategory = string.IsNullOrWhiteSpace(request.ServiceCategory)
                    ? package.Category
                    : request.ServiceCategory.Trim(),
                Location = request.Location?.Trim(),
                Latitude = request.Latitude,
                Longitude = request.Longitude,
                Budget = request.Budget,
                Notes = request.Notes?.Trim(),
                Status = PackageRequestStatuses.Pending,
                CreatedAt = now,
            };

            _db.PackageRequests.Add(entity);
            await _db.SaveChangesAsync();
            _logger.LogInformation("PackageRequest {Id} created by client {ClientId} for package {PackageId}",
                entity.Id, clientId, entity.PackageId);
            return await MapAsync(entity);
        }

        public async Task<PackageRequestDTO> GetForClientAsync(string clientId, string packageRequestId)
        {
            if (!ObjectId.TryParse(packageRequestId, out var oid))
                throw new ArgumentException("Invalid package request id.");

            var entity = await _db.PackageRequests.FirstOrDefaultAsync(p => p.Id == oid && p.DeletedAt == null)
                ?? throw new KeyNotFoundException($"Package request '{packageRequestId}' not found.");

            if (!string.Equals(entity.ClientId, clientId, StringComparison.Ordinal))
                throw new UnauthorizedAccessException("You are not authorised to view this request.");

            return await MapAsync(entity);
        }

        private async Task<PackageRequestDTO> MapAsync(PackageRequest e)
        {
            ConfirmedCaregiverDTO? confirmed = null;

            // The client only ever sees a caregiver once an assignment has been accepted.
            if (string.Equals(e.Status, PackageRequestStatuses.Confirmed, StringComparison.Ordinal)
                && !string.IsNullOrEmpty(e.ConfirmedCaregiverId)
                && ObjectId.TryParse(e.ConfirmedCaregiverId, out var cgOid))
            {
                var cg = await _db.CareGivers.FirstOrDefaultAsync(c => c.Id == cgOid);
                if (cg != null)
                {
                    confirmed = new ConfirmedCaregiverDTO
                    {
                        CaregiverId = cg.Id.ToString(),
                        Name = $"{cg.FirstName} {cg.LastName}".Trim(),
                        ProfileImage = cg.ProfileImage,
                        CaregiverType = cg.CaregiverType?.ToString() ?? string.Empty,
                        Specialty = cg.Specialty,
                        ConfirmedAt = e.ConfirmedAt ?? e.UpdatedAt ?? e.CreatedAt,
                    };
                }
            }

            return new PackageRequestDTO
            {
                Id = e.Id.ToString(),
                ClientId = e.ClientId,
                PackageId = e.PackageId,
                PackageCategory = e.PackageCategory,
                PackageTierLabel = e.PackageTierLabel,
                RequiredCaregiverType = e.RequiredCaregiverType.ToString(),
                RequiredSpecialty = e.RequiredSpecialty,
                ServiceCategory = e.ServiceCategory,
                Location = e.Location,
                Budget = e.Budget,
                Notes = e.Notes,
                Status = e.Status,
                ConfirmedCaregiver = confirmed,
                CreatedAt = e.CreatedAt,
            };
        }
    }
}
