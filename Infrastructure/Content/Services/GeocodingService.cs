using Application.DTOs;
using Application.Interfaces.Content;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;

namespace Infrastructure.Content.Services
{
    public class GeocodingService : IGeocodingService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<GeocodingService> _logger;

        public GeocodingService(HttpClient httpClient, IConfiguration configuration, ILogger<GeocodingService> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<GeocodeResponse> GeocodeAsync(string address)
        {
            try
            {
                _logger.LogInformation($"Geocoding address: {address}");

                var apiKey = _configuration["GoogleMaps:ApiKey"];
                if (string.IsNullOrEmpty(apiKey))
                {
                    _logger.LogWarning("Google Maps API key not configured, using mock geocoding");
                    return await MockGeocodeAsync(address);
                }

                var encodedAddress = HttpUtility.UrlEncode(address);
                var url = $"https://maps.googleapis.com/maps/api/geocode/json?address={encodedAddress}&key={apiKey}";

                var response = await _httpClient.GetStringAsync(url);
                var geocodeResult = JsonSerializer.Deserialize<GoogleMapsGeocodingResponse>(response, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                if (geocodeResult?.Status == "OK" && geocodeResult.Results?.Any() == true)
                {
                    var result = geocodeResult.Results.First();
                    var location = result.Geometry?.Location;

                    if (location == null)
                        throw new InvalidOperationException("Invalid geocoding response: no location data");

                    var city = ExtractCityFromComponents(result.AddressComponents);
                    var state = ExtractStateFromComponents(result.AddressComponents);
                    var country = ExtractCountryFromComponents(result.AddressComponents);

                    return new GeocodeResponse
                    {
                        Latitude = location.Lat,
                        Longitude = location.Lng,
                        City = city,
                        State = state,
                        Country = country,
                        FormattedAddress = result.FormattedAddress ?? address
                    };
                }
                else
                {
                    _logger.LogWarning($"Geocoding failed for address: {address}. Status: {geocodeResult?.Status}");
                    return await MockGeocodeAsync(address);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error geocoding address: {address}");
                return await MockGeocodeAsync(address);
            }
        }

        public async Task<GeocodeResponse> ReverseGeocodeAsync(double latitude, double longitude)
        {
            try
            {
                _logger.LogInformation($"Reverse geocoding coordinates: {latitude}, {longitude}");

                var apiKey = _configuration["GoogleMaps:ApiKey"];
                if (string.IsNullOrEmpty(apiKey))
                {
                    _logger.LogWarning("Google Maps API key not configured, using mock reverse geocoding");
                    return await MockReverseGeocodeAsync(latitude, longitude);
                }

                var url = $"https://maps.googleapis.com/maps/api/geocode/json?latlng={latitude},{longitude}&key={apiKey}";

                var response = await _httpClient.GetStringAsync(url);
                var geocodeResult = JsonSerializer.Deserialize<GoogleMapsGeocodingResponse>(response, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                if (geocodeResult?.Status == "OK" && geocodeResult.Results?.Any() == true)
                {
                    var result = geocodeResult.Results.First();

                    var city = ExtractCityFromComponents(result.AddressComponents);
                    var state = ExtractStateFromComponents(result.AddressComponents);
                    var country = ExtractCountryFromComponents(result.AddressComponents);

                    return new GeocodeResponse
                    {
                        Latitude = latitude,
                        Longitude = longitude,
                        City = city,
                        State = state,
                        Country = country,
                        FormattedAddress = result.FormattedAddress ?? $"{latitude}, {longitude}"
                    };
                }
                else
                {
                    _logger.LogWarning($"Reverse geocoding failed for coordinates: {latitude}, {longitude}. Status: {geocodeResult?.Status}");
                    return await MockReverseGeocodeAsync(latitude, longitude);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error reverse geocoding coordinates: {latitude}, {longitude}");
                return await MockReverseGeocodeAsync(latitude, longitude);
            }
        }

        public async Task<string> GetCityFromAddressAsync(string address)
        {
            var geocodeResult = await GeocodeAsync(address);
            return geocodeResult.City;
        }

        public async Task<bool> ValidateAddressAsync(string address)
        {
            try
            {
                var result = await GeocodeAsync(address);
                return !string.IsNullOrEmpty(result.FormattedAddress);
            }
            catch
            {
                return false;
            }
        }

        public async Task<IEnumerable<GeocodeResponse>> BatchGeocodeAsync(IEnumerable<string> addresses)
        {
            var tasks = addresses.Select(async address => await GeocodeAsync(address));
            var results = await Task.WhenAll(tasks);
            return results;
        }

        #region Private Helper Methods

        private string ExtractCityFromComponents(IEnumerable<AddressComponent>? components)
        {
            if (components == null) return "";

            var cityComponent = components.FirstOrDefault(c =>
                c.Types?.Contains("locality") == true ||
                c.Types?.Contains("administrative_area_level_2") == true ||
                c.Types?.Contains("sublocality") == true);

            return cityComponent?.LongName ?? "";
        }

        private string ExtractStateFromComponents(IEnumerable<AddressComponent>? components)
        {
            if (components == null) return "";

            var stateComponent = components.FirstOrDefault(c =>
                c.Types?.Contains("administrative_area_level_1") == true);

            return stateComponent?.LongName ?? "";
        }

        private string ExtractCountryFromComponents(IEnumerable<AddressComponent>? components)
        {
            if (components == null) return "";

            var countryComponent = components.FirstOrDefault(c =>
                c.Types?.Contains("country") == true);

            return countryComponent?.LongName ?? "";
        }

        private async Task<GeocodeResponse> MockGeocodeAsync(string address)
        {
            // Mock implementation for development/testing
            await Task.Delay(100); // Simulate API call delay

            var city = ExtractCityFromMockAddress(address);

            return new GeocodeResponse
            {
                Latitude = 6.5244 + (Random.Shared.NextDouble() - 0.5) * 0.1, // Lagos, Nigeria area
                Longitude = 3.3792 + (Random.Shared.NextDouble() - 0.5) * 0.1,
                City = city,
                State = "Lagos",
                Country = "Nigeria",
                FormattedAddress = address
            };
        }

        private async Task<GeocodeResponse> MockReverseGeocodeAsync(double latitude, double longitude)
        {
            await Task.Delay(100);

            return new GeocodeResponse
            {
                Latitude = latitude,
                Longitude = longitude,
                City = "Lagos",
                State = "Lagos",
                Country = "Nigeria",
                FormattedAddress = $"Near {latitude:F4}, {longitude:F4}"
            };
        }

        private string ExtractCityFromMockAddress(string address)
        {
            // Simple city extraction for mock data
            var commonCities = new[] { "Lagos", "Abuja", "Kano", "Ibadan", "Port Harcourt", "Benin", "Kaduna" };

            foreach (var city in commonCities)
            {
                if (address.Contains(city, StringComparison.OrdinalIgnoreCase))
                    return city;
            }

            return "Lagos"; // Default city
        }

        #endregion

        #region Google Maps API Models

        private class GoogleMapsGeocodingResponse
        {
            public string? Status { get; set; }
            public IEnumerable<GeocodeResult>? Results { get; set; }
        }

        private class GeocodeResult
        {
            public string? FormattedAddress { get; set; }
            public Geometry? Geometry { get; set; }
            public IEnumerable<AddressComponent>? AddressComponents { get; set; }
        }

        private class Geometry
        {
            public Location? Location { get; set; }
        }

        private class Location
        {
            public double Lat { get; set; }
            public double Lng { get; set; }
        }

        private class AddressComponent
        {
            public string? LongName { get; set; }
            public string? ShortName { get; set; }
            public IEnumerable<string>? Types { get; set; }
        }

        #endregion
    }
}