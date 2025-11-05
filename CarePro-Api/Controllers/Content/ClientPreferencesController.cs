using Application.DTOs;
using Application.Interfaces.Content;
using Infrastructure.Content.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CarePro_Api.Controllers.Content
{
    [Route("api/[controller]")]
    [ApiController]
    public class ClientPreferencesController : ControllerBase
    {
        private readonly IClientPreferenceService clientPreferenceService;
        private readonly IClientService clientService;
        private readonly ILogger<ClientPreferencesController> logger;

        public ClientPreferencesController(IClientPreferenceService clientPreferenceService, IClientService clientService, ILogger<ClientPreferencesController> logger)
        {
            this.clientPreferenceService = clientPreferenceService;
            this.clientService = clientService;
            this.logger = logger;
        }


        [HttpPost]
        // [Authorize(Roles = "Client")]
        public async Task<IActionResult> AddClientPreferenceAsync([FromBody] AddClientPreferenceRequest addClientPreferenceRequest)
        {
            try
            {
                // Pass Domain Object to Repository, to Persisit this
                var clientPreference = await clientPreferenceService.CreateClientPreferenceAsync(addClientPreferenceRequest);


                // Send DTO response back to ClientUser
                return Ok(clientPreference);

            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ApplicationException appEx)
            {
                // Handle application-specific exceptions
                return BadRequest(new { ErrorMessage = appEx.Message });
            }
            catch (HttpRequestException httpEx)
            {
                // Handle HTTP request-related exceptions
                return StatusCode(500, new { ErrorMessage = httpEx.Message });
            }
            catch (Exception ex)
            {
                // Handle other exceptions
                logger.LogError(ex, "An unexpected error occurred"); return StatusCode(500, new { ErrorMessage = "An error occurred on the server." });
            }

        }

        [HttpGet]
        [Route("clientId")]
        // [Authorize(Roles = "Caregiver, Client, Admin")]
        public async Task<IActionResult> GetCaregiverVerificationAsync(string clientId)
        {

            try
            {
                logger.LogInformation($"Retrieving Preferences for Client with ID '{clientId}'.");

                var clientPreference = await clientPreferenceService.GetClientPreferenceAsync(clientId);

                return Ok(clientPreference);

            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ApplicationException appEx)
            {
                // Handle application-specific exceptions
                return BadRequest(new { ErrorMessage = appEx.Message });
            }
            catch (HttpRequestException httpEx)
            {
                // Handle HTTP request-related exceptions
                return StatusCode(500, new { ErrorMessage = httpEx.Message });
            }
            catch (Exception ex)
            {
                // Handle other exceptions
                logger.LogError(ex, "An unexpected error occurred"); return StatusCode(500, new { ErrorMessage = "An error occurred on the server." });
            }

        }


        [HttpPut]
        [Route("preferenceId")]
        // [Authorize(Roles = "Caregiver, Admin")]
        public async Task<ActionResult<string>> UpdateVerificationAsync(string preferenceId, UpdateClientPreferenceRequest updateClientPreferenceRequest)
        {
            try
            {
                var result = await clientPreferenceService.UpdateClientPreferenceAsync(preferenceId, updateClientPreferenceRequest);
                logger.LogInformation($"Client Preference  with ID: {preferenceId} updated.");
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { Message = ex.Message }); // Returns 400 Bad Request
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }

        }


        // GET: api/ClientPreferences/notification-preferences/{clientId}
        [HttpGet]
        [Route("notification-preferences/{clientId}")]
        // [Authorize(Roles = "Client, Admin")]
        public async Task<IActionResult> GetNotificationPreferencesAsync(string clientId)
        {
            try
            {
                logger.LogInformation($"Retrieving notification preferences for Client with ID '{clientId}'.");

                var preferences = await clientPreferenceService.GetNotificationPreferencesAsync(clientId);

                return Ok(new NotificationPreferencesResponse
                {
                    Success = true,
                    Message = "Notification preferences retrieved successfully",
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
                logger.LogError(ex, "An unexpected error occurred while retrieving notification preferences");
                return StatusCode(500, new { success = false, message = "An error occurred on the server.", error = ex.Message });
            }
        }

        // PUT: api/ClientPreferences/notification-preferences/{clientId}
        [HttpPut]
        [Route("notification-preferences/{clientId}")]
        // [Authorize(Roles = "Client, Admin")]
        public async Task<IActionResult> UpdateNotificationPreferencesAsync(string clientId, [FromBody] UpdateNotificationPreferencesRequest updateRequest)
        {
            try
            {
                logger.LogInformation($"Updating notification preferences for Client with ID '{clientId}'.");

                var updatedPreferences = await clientPreferenceService.UpdateNotificationPreferencesAsync(clientId, updateRequest);

                return Ok(new NotificationPreferencesResponse
                {
                    Success = true,
                    Message = "Notification preferences updated successfully",
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
                logger.LogError(ex, "An unexpected error occurred while updating notification preferences");
                return StatusCode(500, new { success = false, message = "An error occurred on the server.", error = ex.Message });
            }
        }


    }
}
