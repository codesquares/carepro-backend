using Application.DTOs;
using Application.Interfaces.Content;
using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Content.Services
{
    public class LocationService : ILocationService
    {
        private readonly CareProDbContext _context;
        private readonly IGeocodingService _geocodingService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<LocationService> _logger;

        public LocationService(
            CareProDbContext context,
            IGeocodingService geocodingService,
            IConfiguration configuration,
            ILogger<LocationService> logger)
        {
            _context = context;
            _geocodingService = geocodingService;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<LocationDTO> SetUserLocationAsync(SetLocationRequest request)
        {
            try
            {
                _logger.LogInformation($"Setting location for user {request.UserId} of type {request.UserType}");

                // 1. Geocode address to get coordinates and city
                var geocodeResult = await _geocodingService.GeocodeAsync(request.Address);

                // 2. Check if user already has a location
                var existingLocation = await _context.Locations
                    .FirstOrDefaultAsync(l => l.UserId == request.UserId &&
                                            l.UserType == request.UserType &&
                                            !l.IsDeleted);

                Location location;

                if (existingLocation != null)
                {
                    // Update existing
                    existingLocation.Address = request.Address;
                    existingLocation.City = geocodeResult.City;
                    existingLocation.State = geocodeResult.State;
                    existingLocation.Country = geocodeResult.Country;
                    existingLocation.Latitude = geocodeResult.Latitude;
                    existingLocation.Longitude = geocodeResult.Longitude;
                    existingLocation.UpdatedAt = DateTime.UtcNow;
                    existingLocation.IsActive = true;

                    location = existingLocation;
                    _logger.LogInformation($"Updated existing location for user {request.UserId}");
                }
                else
                {
                    // Create new
                    location = new Location
                    {
                        Id = ObjectId.GenerateNewId(),
                        UserId = request.UserId,
                        UserType = request.UserType,
                        Address = request.Address,
                        City = geocodeResult.City,
                        State = geocodeResult.State,
                        Country = geocodeResult.Country,
                        Latitude = geocodeResult.Latitude,
                        Longitude = geocodeResult.Longitude,
                        IsActive = true,
                        IsDeleted = false,
                        CreatedAt = DateTime.UtcNow
                    };

                    await _context.Locations.AddAsync(location);
                    _logger.LogInformation($"Created new location for user {request.UserId}");
                }

                // 3. Update user entity with location data
                await UpdateUserEntityLocation(request.UserId, request.UserType, geocodeResult);

                await _context.SaveChangesAsync();

                // 4. Also update HomeAddress field to keep it in sync
                await UpdateUserEntityHomeAddressAsync(request.UserId, request.UserType, request.Address);

                return new LocationDTO
                {
                    Id = location.Id.ToString(),
                    Address = location.Address,
                    City = location.City,
                    State = location.State,
                    Country = location.Country,
                    Latitude = location.Latitude,
                    Longitude = location.Longitude,
                    IsActive = location.IsActive,
                    CreatedAt = location.CreatedAt,
                    UpdatedAt = location.UpdatedAt
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error setting location for user {request.UserId}");
                throw;
            }
        }

        public async Task<LocationDTO?> GetUserLocationAsync(string userId, string userType)
        {
            var location = await _context.Locations
                .FirstOrDefaultAsync(l => l.UserId == userId &&
                                        l.UserType == userType &&
                                        l.IsActive &&
                                        !l.IsDeleted);

            if (location == null)
                return null;

            return new LocationDTO
            {
                Id = location.Id.ToString(),
                Address = location.Address,
                City = location.City,
                State = location.State,
                Country = location.Country,
                Latitude = location.Latitude,
                Longitude = location.Longitude,
                IsActive = location.IsActive,
                CreatedAt = location.CreatedAt,
                UpdatedAt = location.UpdatedAt
            };
        }

        public async Task<LocationDTO> UpdateUserLocationAsync(UpdateUserLocationRequest request)
        {
            var location = await _context.Locations
                .FirstOrDefaultAsync(l => l.UserId == request.UserId &&
                                        l.UserType == request.UserType &&
                                        !l.IsDeleted);

            // If location doesn't exist, create it (upsert pattern)
            if (location == null)
            {
                var createRequest = new SetLocationRequest
                {
                    UserId = request.UserId,
                    UserType = request.UserType,
                    Address = request.Address ?? throw new ArgumentException("Address is required when creating a new location")
                };

                return await SetUserLocationAsync(createRequest);
            }

            // Update provided fields
            if (!string.IsNullOrEmpty(request.Address))
            {
                var geocodeResult = await _geocodingService.GeocodeAsync(request.Address);
                location.Address = request.Address;
                location.City = geocodeResult.City;
                location.State = geocodeResult.State;
                location.Latitude = geocodeResult.Latitude;
                location.Longitude = geocodeResult.Longitude;
            }
            else
            {
                if (!string.IsNullOrEmpty(request.City))
                    location.City = request.City;
                if (!string.IsNullOrEmpty(request.State))
                    location.State = request.State;
                if (request.Latitude.HasValue)
                    location.Latitude = request.Latitude.Value;
                if (request.Longitude.HasValue)
                    location.Longitude = request.Longitude.Value;
            }

            location.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation($"Location saved. Now attempting to sync HomeAddress for {request.UserType} {request.UserId}");

            // Update HomeAddress field on user entity to keep it in sync
            await UpdateUserEntityHomeAddressAsync(request.UserId, request.UserType, location.Address);

            return new LocationDTO
            {
                Id = location.Id.ToString(),
                Address = location.Address,
                City = location.City,
                State = location.State,
                Country = location.Country,
                Latitude = location.Latitude,
                Longitude = location.Longitude,
                IsActive = location.IsActive,
                CreatedAt = location.CreatedAt,
                UpdatedAt = location.UpdatedAt
            };
        }

        public async Task<bool> DeleteUserLocationAsync(string userId, string userType)
        {
            var location = await _context.Locations
                .FirstOrDefaultAsync(l => l.UserId == userId &&
                                        l.UserType == userType &&
                                        !l.IsDeleted);

            if (location == null)
                return false;

            location.IsDeleted = true;
            location.IsActive = false;
            location.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<DistanceResponse> CalculateDistanceAsync(DistanceCalculationRequest request)
        {
            var distanceKm = CalculateHaversineDistance(
                request.FromLatitude, request.FromLongitude,
                request.ToLatitude, request.ToLongitude
            );

            var distanceMiles = distanceKm * 0.621371; // Convert to miles

            return new DistanceResponse
            {
                DistanceKm = Math.Round(distanceKm, 2),
                DistanceMiles = Math.Round(distanceMiles, 2),
                FormattedDistance = distanceKm < 1
                    ? $"{Math.Round(distanceKm * 1000)}m"
                    : $"{Math.Round(distanceKm, 1)}km"
            };
        }

        public async Task<IEnumerable<CaregiverProximityResponse>> FindNearbyCaregivers(ProximitySearchRequest request)
        {
            try
            {
                double clientLat, clientLng;
                string serviceCity = "";

                // Get client's service location coordinates
                if (request.ServiceLatitude.HasValue && request.ServiceLongitude.HasValue)
                {
                    clientLat = request.ServiceLatitude.Value;
                    clientLng = request.ServiceLongitude.Value;

                    if (!string.IsNullOrEmpty(request.ServiceAddress))
                    {
                        serviceCity = await _geocodingService.GetCityFromAddressAsync(request.ServiceAddress);
                    }
                }
                else if (!string.IsNullOrEmpty(request.ServiceAddress))
                {
                    var geocodeResult = await _geocodingService.GeocodeAsync(request.ServiceAddress);
                    clientLat = geocodeResult.Latitude;
                    clientLng = geocodeResult.Longitude;
                    serviceCity = geocodeResult.City;
                }
                else
                {
                    throw new ArgumentException("Either coordinates or service address must be provided");
                }

                // Use preferred city if provided
                if (!string.IsNullOrEmpty(request.PreferredCity))
                {
                    serviceCity = request.PreferredCity;
                }

                // Get all available caregivers with locations
                var caregivers = await _context.CareGivers
                    .Where(c => c.IsAvailable &&
                              !c.IsDeleted &&
                              c.Latitude.HasValue &&
                              c.Longitude.HasValue)
                    .ToListAsync();

                _logger.LogInformation($"Found {caregivers.Count} available caregivers with locations");

                // Calculate distances and scores
                var proximityResults = caregivers.Select(caregiver =>
                {
                    var distance = CalculateHaversineDistance(
                        clientLat, clientLng,
                        caregiver.Latitude!.Value, caregiver.Longitude!.Value
                    );

                    var sameCity = !string.IsNullOrEmpty(serviceCity) &&
                                  !string.IsNullOrEmpty(caregiver.ServiceCity) &&
                                  string.Equals(serviceCity, caregiver.ServiceCity, StringComparison.OrdinalIgnoreCase);

                    var proximityScore = CalculateProximityScore(distance, sameCity);

                    return new CaregiverProximityResponse
                    {
                        CaregiverId = caregiver.Id.ToString(),
                        CaregiverName = $"{caregiver.FirstName} {caregiver.LastName}",
                        ProfileImage = caregiver.ProfileImage,
                        DistanceKm = Math.Round(distance, 2),
                        SameCity = sameCity,
                        ProximityScore = proximityScore,
                        IsAvailable = caregiver.IsAvailable,
                        AboutMe = caregiver.AboutMe,
                        Location = new LocationDTO
                        {
                            Address = caregiver.ServiceAddress ?? "",
                            City = caregiver.ServiceCity ?? "",
                            State = caregiver.ServiceState,
                            Latitude = caregiver.Latitude.Value,
                            Longitude = caregiver.Longitude.Value,
                            IsActive = true,
                            CreatedAt = caregiver.CreatedAt
                        }
                    };
                })
                .Where(r => r.DistanceKm <= request.MaxDistanceKm)
                .OrderByDescending(r => r.ProximityScore)
                .ThenBy(r => r.DistanceKm)
                .ToList();

                _logger.LogInformation($"Returning {proximityResults.Count} caregivers within {request.MaxDistanceKm}km");

                return proximityResults;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error finding nearby caregivers");
                throw;
            }
        }

        public async Task<string> ExtractCityFromAddressAsync(string address)
        {
            return await _geocodingService.GetCityFromAddressAsync(address);
        }

        public async Task<(double Latitude, double Longitude)> GeocodeAddressAsync(string address)
        {
            var result = await _geocodingService.GeocodeAsync(address);
            return (result.Latitude, result.Longitude);
        }

        public async Task<IEnumerable<CaregiverProximityResponse>> GetCaregiversByCity(string city, int maxResults = 50)
        {
            var caregivers = await _context.CareGivers
                .Where(c => c.IsAvailable &&
                          !c.IsDeleted &&
                          c.ServiceCity != null &&
                          c.ServiceCity.ToLower().Contains(city.ToLower()))
                .Take(maxResults)
                .ToListAsync();

            return caregivers.Select(caregiver => new CaregiverProximityResponse
            {
                CaregiverId = caregiver.Id.ToString(),
                CaregiverName = $"{caregiver.FirstName} {caregiver.LastName}",
                ProfileImage = caregiver.ProfileImage,
                DistanceKm = 0, // Unknown distance
                SameCity = true,
                ProximityScore = 100, // Max score for same city
                IsAvailable = caregiver.IsAvailable,
                AboutMe = caregiver.AboutMe,
                Location = new LocationDTO
                {
                    Address = caregiver.ServiceAddress ?? "",
                    City = caregiver.ServiceCity ?? "",
                    State = caregiver.ServiceState,
                    Latitude = caregiver.Latitude ?? 0,
                    Longitude = caregiver.Longitude ?? 0,
                    IsActive = true,
                    CreatedAt = caregiver.CreatedAt
                }
            });
        }

        public async Task<IEnumerable<LocationDTO>> GetUserLocationHistory(string userId, string userType)
        {
            var locations = await _context.Locations
                .Where(l => l.UserId == userId && l.UserType == userType)
                .OrderByDescending(l => l.CreatedAt)
                .ToListAsync();

            return locations.Select(l => new LocationDTO
            {
                Id = l.Id.ToString(),
                Address = l.Address,
                City = l.City,
                State = l.State,
                Country = l.Country,
                Latitude = l.Latitude,
                Longitude = l.Longitude,
                IsActive = l.IsActive,
                CreatedAt = l.CreatedAt,
                UpdatedAt = l.UpdatedAt
            });
        }

        #region Private Helper Methods

        private async Task UpdateUserEntityLocation(string userId, string userType, GeocodeResponse geocodeResult)
        {
            if (userType == "Caregiver")
            {
                var caregiver = await _context.CareGivers
                    .FirstOrDefaultAsync(c => c.Id.ToString() == userId);

                if (caregiver != null)
                {
                    caregiver.ServiceCity = geocodeResult.City;
                    caregiver.ServiceState = geocodeResult.State;
                    caregiver.ServiceAddress = geocodeResult.FormattedAddress;
                    caregiver.Latitude = geocodeResult.Latitude;
                    caregiver.Longitude = geocodeResult.Longitude;
                    
                    _context.CareGivers.Update(caregiver);
                    // Note: SaveChangesAsync is called by the caller method
                }
            }
            else if (userType == "Client")
            {
                var client = await _context.Clients
                    .FirstOrDefaultAsync(c => c.Id.ToString() == userId);

                if (client != null)
                {
                    client.PreferredCity = geocodeResult.City;
                    client.PreferredState = geocodeResult.State;
                    client.Address = geocodeResult.FormattedAddress;
                    client.Latitude = geocodeResult.Latitude;
                    client.Longitude = geocodeResult.Longitude;
                    
                    _context.Clients.Update(client);
                    // Note: SaveChangesAsync is called by the caller method
                }
            }
        }

        private double CalculateHaversineDistance(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371; // Earth's radius in kilometers

            var dLat = ToRadians(lat2 - lat1);
            var dLon = ToRadians(lon2 - lon1);

            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

            return R * c;
        }

        private int CalculateProximityScore(double distanceKm, bool sameCity)
        {
            var baseScore = 100;

            // Same city bonus
            if (sameCity)
                baseScore += 50;

            // Distance penalty (configurable)
            var distancePenaltyPerKm = _configuration.GetValue<int>("LocationSettings:DistancePenaltyPerKm", 2);
            var distancePenalty = (int)(distanceKm * distancePenaltyPerKm);

            return Math.Max(0, baseScore - distancePenalty);
        }

        private double ToRadians(double deg) => deg * (Math.PI / 180);

        /// <summary>
        /// Updates the HomeAddress field on the user entity (Caregiver or Client)
        /// to keep it in sync with the Location table
        /// </summary>
        private async Task UpdateUserEntityHomeAddressAsync(string userId, string userType, string address)
        {
            try
            {
                _logger.LogInformation($"Attempting to sync HomeAddress for {userType} {userId} with address: {address}");

                if (string.IsNullOrEmpty(address))
                {
                    _logger.LogWarning($"Address is null or empty, skipping HomeAddress sync for {userId}");
                    return;
                }

                if (userType == "Caregiver")
                {
                    if (!ObjectId.TryParse(userId, out var objectId))
                    {
                        _logger.LogWarning($"Invalid ObjectId format for caregiver: {userId}");
                        return;
                    }

                    var caregiver = await _context.CareGivers.FindAsync(objectId);
                    if (caregiver != null)
                    {
                        _logger.LogInformation($"Found caregiver {userId}. Current HomeAddress: '{caregiver.HomeAddress}'. Updating to: '{address}'");
                        caregiver.HomeAddress = address;
                        _context.CareGivers.Update(caregiver);
                        await _context.SaveChangesAsync();
                        _logger.LogInformation($"Successfully updated HomeAddress for caregiver {userId}");
                    }
                    else
                    {
                        _logger.LogWarning($"Caregiver {userId} not found in database");
                    }
                }
                else if (userType == "Client")
                {
                    if (!ObjectId.TryParse(userId, out var objectId))
                    {
                        _logger.LogWarning($"Invalid ObjectId format for client: {userId}");
                        return;
                    }

                    var client = await _context.Clients.FindAsync(objectId);
                    if (client != null)
                    {
                        _logger.LogInformation($"Found client {userId}. Current HomeAddress: '{client.HomeAddress}'. Updating to: '{address}'");
                        client.HomeAddress = address;
                        _context.Clients.Update(client);
                        await _context.SaveChangesAsync();
                        _logger.LogInformation($"Successfully updated HomeAddress for client {userId}");
                    }
                    else
                    {
                        _logger.LogWarning($"Client {userId} not found in database");
                    }
                }
                else
                {
                    _logger.LogWarning($"Unknown userType: {userType}. Expected 'Caregiver' or 'Client'");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to update HomeAddress for {userType} {userId}. Location update succeeded but HomeAddress sync failed.");
            }
        }

        #endregion
    }
}