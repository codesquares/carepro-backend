using Application.DTOs;
using Application.Interfaces.Content;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;

namespace CarePro_Api.Controllers.Content
{
    [Route("api/[controller]")]
    [ApiController]
    public class LocationController : ControllerBase
    {
        private readonly ILocationService _locationService;
        private readonly IGeocodingService _geocodingService;
        private readonly ILogger<LocationController> _logger;

        public LocationController(
            ILocationService locationService,
            IGeocodingService geocodingService,
            ILogger<LocationController> logger)
        {
            _locationService = locationService;
            _geocodingService = geocodingService;
            _logger = logger;
        }

        /// <summary>
        /// Set or update a user's location
        /// </summary>
        [HttpPost("set-location")]
        public async Task<IActionResult> SetUserLocation([FromBody] SetLocationRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var result = await _locationService.SetUserLocationAsync(request);

                _logger.LogInformation($"Location set successfully for user {request.UserId}");

                return Ok(new
                {
                    Success = true,
                    Message = "Location set successfully",
                    Data = result
                });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Invalid request data for setting location");
                return BadRequest(new { Message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Invalid operation for setting location");
                return BadRequest(new { Message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting user location");
                return StatusCode(500, new { Message = "An error occurred while setting the location" });
            }
        }

        /// <summary>
        /// Get a user's current location
        /// </summary>
        [HttpGet("user-location")]
        public async Task<IActionResult> GetUserLocation([FromQuery, Required] string userId, [FromQuery, Required] string userType)
        {
            try
            {
                if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(userType))
                {
                    return BadRequest(new { Message = "UserId and UserType are required" });
                }

                var location = await _locationService.GetUserLocationAsync(userId, userType);

                if (location == null)
                {
                    return NotFound(new { Message = "Location not found for the specified user" });
                }

                return Ok(new
                {
                    Success = true,
                    Data = location
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting location for user {userId}");
                return StatusCode(500, new { Message = "An error occurred while retrieving the location" });
            }
        }

        /// <summary>
        /// Update a user's location
        /// </summary>
        [HttpPut("update-location")]
        public async Task<IActionResult> UpdateUserLocation([FromBody] UpdateUserLocationRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var result = await _locationService.UpdateUserLocationAsync(request);

                return Ok(new
                {
                    Success = true,
                    Message = "Location updated successfully",
                    Data = result
                });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Invalid operation for updating location");
                return NotFound(new { Message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating user location");
                return StatusCode(500, new { Message = "An error occurred while updating the location" });
            }
        }

        /// <summary>
        /// Delete a user's location
        /// </summary>
        [HttpDelete("delete-location")]
        public async Task<IActionResult> DeleteUserLocation([FromQuery, Required] string userId, [FromQuery, Required] string userType)
        {
            try
            {
                if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(userType))
                {
                    return BadRequest(new { Message = "UserId and UserType are required" });
                }

                var success = await _locationService.DeleteUserLocationAsync(userId, userType);

                if (!success)
                {
                    return NotFound(new { Message = "Location not found for the specified user" });
                }

                return Ok(new
                {
                    Success = true,
                    Message = "Location deleted successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting location for user {userId}");
                return StatusCode(500, new { Message = "An error occurred while deleting the location" });
            }
        }

        /// <summary>
        /// Find nearby caregivers based on client's service location
        /// </summary>
        [HttpPost("nearby-caregivers")]
        public async Task<IActionResult> FindNearbyCaregivers([FromBody] ProximitySearchRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var caregivers = await _locationService.FindNearbyCaregivers(request);

                return Ok(new
                {
                    Success = true,
                    Message = $"Found {caregivers.Count()} nearby caregivers",
                    Data = caregivers
                });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Invalid request for finding nearby caregivers");
                return BadRequest(new { Message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error finding nearby caregivers");
                return StatusCode(500, new { Message = "An error occurred while finding nearby caregivers" });
            }
        }

        /// <summary>
        /// Get caregivers in a specific city
        /// </summary>
        [HttpGet("caregivers-by-city")]
        public async Task<IActionResult> GetCaregiversByCity([FromQuery, Required] string city, [FromQuery] int maxResults = 50)
        {
            try
            {
                if (string.IsNullOrEmpty(city))
                {
                    return BadRequest(new { Message = "City is required" });
                }

                var caregivers = await _locationService.GetCaregiversByCity(city, maxResults);

                return Ok(new
                {
                    Success = true,
                    Message = $"Found {caregivers.Count()} caregivers in {city}",
                    Data = caregivers
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting caregivers for city {city}");
                return StatusCode(500, new { Message = "An error occurred while retrieving caregivers" });
            }
        }

        /// <summary>
        /// Calculate distance between two points
        /// </summary>
        [HttpPost("calculate-distance")]
        public async Task<IActionResult> CalculateDistance([FromBody] DistanceCalculationRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var distance = await _locationService.CalculateDistanceAsync(request);

                return Ok(new
                {
                    Success = true,
                    Data = distance
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating distance");
                return StatusCode(500, new { Message = "An error occurred while calculating distance" });
            }
        }

        /// <summary>
        /// Geocode an address to get coordinates and location details
        /// </summary>
        [HttpPost("geocode")]
        public async Task<IActionResult> GeocodeAddress([FromBody] GeocodeRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var result = await _geocodingService.GeocodeAsync(request.Address);

                return Ok(new
                {
                    Success = true,
                    Data = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error geocoding address: {request.Address}");
                return StatusCode(500, new { Message = "An error occurred while geocoding the address" });
            }
        }

        /// <summary>
        /// Reverse geocode coordinates to get address details
        /// </summary>
        [HttpPost("reverse-geocode")]
        public async Task<IActionResult> ReverseGeocode([FromBody] ReverseGeocodeRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var result = await _geocodingService.ReverseGeocodeAsync(request.Latitude, request.Longitude);

                return Ok(new
                {
                    Success = true,
                    Data = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error reverse geocoding coordinates: {request.Latitude}, {request.Longitude}");
                return StatusCode(500, new { Message = "An error occurred while reverse geocoding the coordinates" });
            }
        }

        /// <summary>
        /// Get location history for a user
        /// </summary>
        [HttpGet("user-location-history")]
        public async Task<IActionResult> GetUserLocationHistory([FromQuery, Required] string userId, [FromQuery, Required] string userType)
        {
            try
            {
                if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(userType))
                {
                    return BadRequest(new { Message = "UserId and UserType are required" });
                }

                var locations = await _locationService.GetUserLocationHistory(userId, userType);

                return Ok(new
                {
                    Success = true,
                    Data = locations
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting location history for user {userId}");
                return StatusCode(500, new { Message = "An error occurred while retrieving location history" });
            }
        }

        /// <summary>
        /// Validate if an address is valid
        /// </summary>
        [HttpPost("validate-address")]
        public async Task<IActionResult> ValidateAddress([FromBody] GeocodeRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var isValid = await _geocodingService.ValidateAddressAsync(request.Address);

                return Ok(new
                {
                    Success = true,
                    IsValid = isValid,
                    Message = isValid ? "Address is valid" : "Address is invalid"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error validating address: {request.Address}");
                return StatusCode(500, new { Message = "An error occurred while validating the address" });
            }
        }
    }
}