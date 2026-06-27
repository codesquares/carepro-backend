using Application.DTOs;
using Application.Interfaces.Content;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Infrastructure.Content.Services;

public class DefaultAddressCleanupService : IDefaultAddressCleanupService
{
    private const string PlaceholderAddress = "Adeola Odeku Street, Victoria Island, Lagos, Nigeria";

    private readonly CareProDbContext _db;
    private readonly ILogger<DefaultAddressCleanupService> _logger;

    public DefaultAddressCleanupService(CareProDbContext db, ILogger<DefaultAddressCleanupService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<DefaultAddressCleanupResult> CleanupDefaultAddressAsync(
        DefaultAddressCleanupRequest request,
        string adminId,
        string adminEmail)
    {
        var scope = NormalizeScope(request.Scope);

        var result = new DefaultAddressCleanupResult
        {
            DryRun = request.DryRun,
            Scope = scope,
            PlaceholderAddress = PlaceholderAddress
        };

        if (!request.DryRun && string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new ArgumentException("Reason is required when DryRun is false.", nameof(request.Reason));
        }

        var includeCaregivers = scope is "All" or "Caregivers";
        var includeClients = scope is "All" or "Clients";

        var caregivers = includeCaregivers
            ? await _db.CareGivers.ToListAsync()
            : new List<Domain.Entities.Caregiver>();

        var clients = includeClients
            ? await _db.Clients.ToListAsync()
            : new List<Domain.Entities.Client>();

        var locations = await _db.Locations.ToListAsync();

        var caregiverLocationMap = locations
            .Where(l => string.Equals(l.UserType, "Caregiver", StringComparison.OrdinalIgnoreCase) && IsPlaceholder(l.Address))
            .GroupBy(l => l.UserId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var clientLocationMap = locations
            .Where(l => string.Equals(l.UserType, "Client", StringComparison.OrdinalIgnoreCase) && IsPlaceholder(l.Address))
            .GroupBy(l => l.UserId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var caregiver in caregivers)
        {
            caregiverLocationMap.TryGetValue(caregiver.Id.ToString(), out var placeholderLocations);
            placeholderLocations ??= new List<Domain.Entities.Location>();

            var shouldResetHomeAddress = IsPlaceholder(caregiver.HomeAddress);
            var shouldResetLocation = IsPlaceholder(caregiver.Location);
            var shouldResetServiceAddress = IsPlaceholder(caregiver.ServiceAddress);

            var userAffected = shouldResetHomeAddress || shouldResetLocation || shouldResetServiceAddress || placeholderLocations.Count > 0;
            if (!userAffected)
                continue;

            result.AffectedCaregiverUsers++;
            result.AffectedLocationRecords += placeholderLocations.Count;

            if (result.CaregiverRecipientPreview.Count < request.PreviewLimit && !string.IsNullOrWhiteSpace(caregiver.Email))
            {
                result.CaregiverRecipientPreview.Add(new CaregiverRecipientPreview
                {
                    UserId = caregiver.Id.ToString(),
                    Email = caregiver.Email,
                    FirstName = caregiver.FirstName
                });
            }

            if (request.DryRun)
                continue;

            try
            {
                if (shouldResetHomeAddress)
                    caregiver.HomeAddress = null;

                if (shouldResetLocation)
                    caregiver.Location = null;

                if (shouldResetServiceAddress)
                {
                    caregiver.ServiceAddress = null;
                    caregiver.ServiceCity = null;
                    caregiver.ServiceState = null;
                    caregiver.Latitude = null;
                    caregiver.Longitude = null;
                }

                if (placeholderLocations.Count > 0)
                {
                    foreach (var location in placeholderLocations)
                    {
                        location.Address = string.Empty;
                        location.City = string.Empty;
                        location.State = null;
                        location.Country = null;
                        location.PostalCode = null;
                        location.Latitude = 0;
                        location.Longitude = 0;
                        location.IsActive = false;
                        location.IsDeleted = true;
                        location.UpdatedAt = DateTime.UtcNow;
                    }
                }

                _db.CareGivers.Update(caregiver);
                if (placeholderLocations.Count > 0)
                    _db.Locations.UpdateRange(placeholderLocations);

                await _db.SaveChangesAsync();
                _db.ChangeTracker.Clear();

                result.UpdatedCaregiverUsers++;
                result.UpdatedLocationRecords += placeholderLocations.Count;
            }
            catch (Exception ex)
            {
                _db.ChangeTracker.Clear();
                result.FailedUsers++;
                result.FailedUserIds.Add(caregiver.Id.ToString());
                result.Errors.Add($"Caregiver {caregiver.Id}: {ex.Message}");
                _logger.LogError(ex, "DefaultAddressCleanup: Failed caregiver reset for {CaregiverId}", caregiver.Id);
            }
        }

        foreach (var client in clients)
        {
            clientLocationMap.TryGetValue(client.Id.ToString(), out var placeholderLocations);
            placeholderLocations ??= new List<Domain.Entities.Location>();

            var shouldResetHomeAddress = IsPlaceholder(client.HomeAddress);
            var shouldResetAddress = IsPlaceholder(client.Address);

            var userAffected = shouldResetHomeAddress || shouldResetAddress || placeholderLocations.Count > 0;
            if (!userAffected)
                continue;

            result.AffectedClientUsers++;
            result.AffectedLocationRecords += placeholderLocations.Count;

            if (request.DryRun)
                continue;

            try
            {
                if (shouldResetHomeAddress)
                    client.HomeAddress = null;

                if (shouldResetAddress)
                {
                    client.Address = null;
                    client.PreferredCity = null;
                    client.PreferredState = null;
                    client.Latitude = null;
                    client.Longitude = null;
                }

                if (placeholderLocations.Count > 0)
                {
                    foreach (var location in placeholderLocations)
                    {
                        location.Address = string.Empty;
                        location.City = string.Empty;
                        location.State = null;
                        location.Country = null;
                        location.PostalCode = null;
                        location.Latitude = 0;
                        location.Longitude = 0;
                        location.IsActive = false;
                        location.IsDeleted = true;
                        location.UpdatedAt = DateTime.UtcNow;
                    }
                }

                _db.Clients.Update(client);
                if (placeholderLocations.Count > 0)
                    _db.Locations.UpdateRange(placeholderLocations);

                await _db.SaveChangesAsync();
                _db.ChangeTracker.Clear();

                result.UpdatedClientUsers++;
                result.UpdatedLocationRecords += placeholderLocations.Count;
            }
            catch (Exception ex)
            {
                _db.ChangeTracker.Clear();
                result.FailedUsers++;
                result.FailedUserIds.Add(client.Id.ToString());
                result.Errors.Add($"Client {client.Id}: {ex.Message}");
                _logger.LogError(ex, "DefaultAddressCleanup: Failed client reset for {ClientId}", client.Id);
            }
        }

        if (!request.DryRun)
        {
            var audit = new Domain.Entities.AdminAuditLog
            {
                Id = ObjectId.GenerateNewId(),
                AdminId = adminId,
                AdminEmail = adminEmail,
                TargetEntityType = "System",
                TargetEntityId = "DefaultAddressCleanup",
                TargetUserId = null,
                Action = "DefaultAddressCleanupExecute",
                BeforeJson = JsonSerializer.Serialize(new
                {
                    scope,
                    placeholder = PlaceholderAddress
                }),
                AfterJson = JsonSerializer.Serialize(new
                {
                    result.UpdatedCaregiverUsers,
                    result.UpdatedClientUsers,
                    result.UpdatedLocationRecords,
                    result.FailedUsers
                }),
                Reason = request.Reason ?? "Default placeholder address cleanup",
                Timestamp = DateTime.UtcNow
            };

            await _db.AdminAuditLogs.AddAsync(audit);
            await _db.SaveChangesAsync();
        }

        return result;
    }

    private static string NormalizeScope(string? rawScope)
    {
        var scope = (rawScope ?? "All").Trim();
        if (scope.Equals("Caregivers", StringComparison.OrdinalIgnoreCase))
            return "Caregivers";
        if (scope.Equals("Clients", StringComparison.OrdinalIgnoreCase))
            return "Clients";
        return "All";
    }

    private static bool IsPlaceholder(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalizedInput = NormalizeAddress(value);
        var normalizedPlaceholder = NormalizeAddress(PlaceholderAddress);
        return normalizedInput == normalizedPlaceholder;
    }

    private static string NormalizeAddress(string address)
    {
        var normalized = address.Trim().ToLowerInvariant();
        normalized = Regex.Replace(normalized, @"\s+", " ");
        normalized = Regex.Replace(normalized, @"\s*,\s*", ",");
        return normalized;
    }
}
