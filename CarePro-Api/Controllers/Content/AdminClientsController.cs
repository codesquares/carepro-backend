using Application.DTOs;
using Application.Interfaces.Content;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CarePro_Api.Controllers.Content
{
    [Route("api/Admin/Clients")]
    [ApiController]
    [Authorize(Policy = "OperationsPolicy")]
    public class AdminClientsController : ControllerBase
    {
        private readonly IClientService _clientService;
        private readonly ILogger<AdminClientsController> _logger;
        private readonly IUserDeletionService _userDeletionService;

        public AdminClientsController(
            IClientService clientService,
            ILogger<AdminClientsController> logger,
            IUserDeletionService userDeletionService)
        {
            _clientService = clientService;
            _logger = logger;
            _userDeletionService = userDeletionService;
        }

        [HttpPut("BulkClearMiddleName")]
        public async Task<IActionResult> BulkClearClientMiddleName(
            [FromBody] AdminBulkClearMiddleNameRequest request)
        {
            try
            {
                if (request == null)
                    return BadRequest(new { error = "Request body is required" });

                var result = await _clientService.BulkClearClientMiddleNameAsync(request);
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Validation error on client bulk clear middle name");
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during client bulk clear middle name");
                return StatusCode(500, new { error = "Failed to bulk clear client middle names" });
            }
        }

        /// <summary>
        /// Admin permanently schedules a client account for deletion.
        /// Soft-deletes the account immediately and queues it for GDPR
        /// anonymisation after the standard 30-day grace period.
        /// </summary>
        [HttpDelete("{clientId}/account")]
        public async Task<IActionResult> AdminDeleteClientAccount(
            string clientId,
            [FromBody] RequestAccountDeletionRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.Reason))
                return BadRequest(new { message = "A reason is required for admin account deletion." });

            var adminId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                          ?? User.FindFirst("sub")?.Value
                          ?? User.FindFirst("userId")?.Value;
            var adminEmail = User.FindFirst(ClaimTypes.Email)?.Value
                             ?? User.FindFirst("email")?.Value
                             ?? string.Empty;

            if (string.IsNullOrEmpty(adminId))
                return Unauthorized(new { message = "Unable to identify admin user." });

            try
            {
                var result = await _userDeletionService.AdminDeleteClientAccountAsync(clientId, adminId, adminEmail, request.Reason);

                if (!result.Success)
                    return BadRequest(new { success = false, message = result.Message, blockers = result.Blockers });

                return Ok(new { success = true, message = result.Message, permanentDeletionDate = result.PermanentDeletionDate });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Admin error deleting client account {ClientId}", clientId);
                return StatusCode(500, new { message = "An error occurred while processing the deletion request." });
            }
        }
    }
}
