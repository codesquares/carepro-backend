using Application.DTOs;
using Application.Interfaces.Authentication;
using Application.Interfaces.Content;
using Domain.Entities;
using Infrastructure.Content.Data;
using Infrastructure.Content.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace CarePro_Api.Controllers.Content
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "Client")]
    public class ClientPreferencesController : ControllerBase
    {
        private readonly IClientPreferenceService clientPreferenceService;
        private readonly IClientService clientService;
        private readonly ITokenHandler tokenHandler;
        private readonly CareProDbContext dbContext;
        private readonly ILogger<ClientPreferencesController> logger;

        public ClientPreferencesController(
            IClientPreferenceService clientPreferenceService,
            IClientService clientService,
            ITokenHandler tokenHandler,
            CareProDbContext dbContext,
            ILogger<ClientPreferencesController> logger)
        {
            this.clientPreferenceService = clientPreferenceService;
            this.clientService = clientService;
            this.tokenHandler = tokenHandler;
            this.dbContext = dbContext;
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

        // GET: api/ClientPreferences/unsubscribe?token=...
        [AllowAnonymous]
        [HttpGet("unsubscribe")]
        public async Task<IActionResult> Unsubscribe([FromQuery] string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return BadRequest("Missing unsubscribe token.");
            }

            var parsed = tokenHandler.ValidateEmailUnsubscribeToken(token);
            if (!parsed.IsValid || string.IsNullOrWhiteSpace(parsed.UserId))
            {
                return BadRequest("Invalid or expired unsubscribe token.");
            }

            var preference = await dbContext.ClientPreferences
                .FirstOrDefaultAsync(x => x.ClientId == parsed.UserId);

            if (preference == null)
            {
                preference = new ClientPreference
                {
                    Id = MongoDB.Bson.ObjectId.GenerateNewId(),
                    ClientId = parsed.UserId,
                    Data = new List<string>(),
                    NotificationPreferences = new NotificationPreferences(),
                    CreatedAt = DateTime.UtcNow
                };
                await dbContext.ClientPreferences.AddAsync(preference);
            }
            else if (preference.NotificationPreferences == null)
            {
                preference.NotificationPreferences = new NotificationPreferences();
            }

            preference.NotificationPreferences.MarketingEmails = false;
            preference.NotificationPreferences.Promotions = false;
            preference.UpdatedOn = DateTime.UtcNow;

            dbContext.ClientPreferences.Update(preference);
            await dbContext.SaveChangesAsync();

            logger.LogInformation("Email unsubscribe applied for user {UserId} scope {Scope}", parsed.UserId, parsed.PreferenceScope);

            var html = @"<html><body style='font-family:Arial,sans-serif;max-width:640px;margin:32px auto;'>
<h2>You are unsubscribed</h2>
<p>Your lifecycle and promotional email preferences have been updated.</p>
<p>You will still receive required transactional emails for account, payment, and order operations.</p>
</body></html>";

            return Content(html, "text/html");
        }

        // POST: api/ClientPreferences/unsubscribe?token=...
        // Supports mailbox-provider one-click unsubscribe POST flows.
        [AllowAnonymous]
        [HttpPost("unsubscribe")]
        public Task<IActionResult> UnsubscribePost([FromQuery] string token)
        {
            return Unsubscribe(token);
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
