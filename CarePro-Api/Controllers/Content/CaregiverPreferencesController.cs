using Application.DTOs;
using Application.Interfaces.Content;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace CarePro_Api.Controllers.Content
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "Caregiver")]
    public class CaregiverPreferencesController : ControllerBase
    {
        private readonly ICaregiverPreferenceService _caregiverPreferenceService;
        private readonly ILogger<CaregiverPreferencesController> _logger;

        public CaregiverPreferencesController(
            ICaregiverPreferenceService caregiverPreferenceService,
            ILogger<CaregiverPreferencesController> logger)
        {
            _caregiverPreferenceService = caregiverPreferenceService;
            _logger = logger;
        }

        // GET: api/CaregiverPreferences/notification-preferences/{caregiverId}
        [HttpGet]
        [Route("notification-preferences/{caregiverId}")]
        public async Task<IActionResult> GetNotificationPreferencesAsync(string caregiverId)
        {
            try
            {
                _logger.LogInformation("Retrieving notification preferences for Caregiver with ID '{CaregiverId}'.", caregiverId);

                var preferences = await _caregiverPreferenceService.GetNotificationPreferencesAsync(caregiverId);

                return Ok(new CaregiverNotificationPreferencesResponse
                {
                    Success = true,
                    Message = "Caregiver notification preferences retrieved successfully",
                    Data = preferences
                });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { success = false, message = ex.Message });
            }
            catch (ApplicationException appEx)
            {
                return BadRequest(new { success = false, message = appEx.Message });
            }
            catch (HttpRequestException httpEx)
            {
                return StatusCode(500, new { success = false, message = "HTTP request error", error = httpEx.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unexpected error occurred while retrieving caregiver notification preferences");
                return StatusCode(500, new { success = false, message = "An error occurred on the server.", error = ex.Message });
            }
        }

        // PUT: api/CaregiverPreferences/notification-preferences/{caregiverId}
        [HttpPut]
        [Route("notification-preferences/{caregiverId}")]
        public async Task<IActionResult> UpdateNotificationPreferencesAsync(
            string caregiverId,
            [FromBody] UpdateCaregiverNotificationPreferencesRequest updateRequest)
        {
            try
            {
                _logger.LogInformation("Updating notification preferences for Caregiver with ID '{CaregiverId}'.", caregiverId);

                var updatedPreferences = await _caregiverPreferenceService.UpdateNotificationPreferencesAsync(caregiverId, updateRequest);

                return Ok(new CaregiverNotificationPreferencesResponse
                {
                    Success = true,
                    Message = "Caregiver notification preferences updated successfully",
                    Data = updatedPreferences
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { success = false, message = ex.Message });
            }
            catch (ApplicationException appEx)
            {
                return BadRequest(new { success = false, message = appEx.Message });
            }
            catch (HttpRequestException httpEx)
            {
                return StatusCode(500, new { success = false, message = "HTTP request error", error = httpEx.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unexpected error occurred while updating caregiver notification preferences");
                return StatusCode(500, new { success = false, message = "An error occurred on the server.", error = ex.Message });
            }
        }
    }
}
