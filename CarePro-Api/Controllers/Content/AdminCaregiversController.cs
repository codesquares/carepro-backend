using Application.DTOs;
using Application.Interfaces.Content;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CarePro_Api.Controllers.Content
{
    /// <summary>
    /// Admin-only endpoints that operate on caregiver records and require
    /// elevated auditing (e.g. fixing a legal name after a verification
    /// mismatch). Routes intentionally live under api/Admin/Caregivers so the
    /// existing caregiver-facing CareGiversController is untouched.
    /// </summary>
    [Route("api/Admin/Caregivers")]
    [ApiController]
    [Authorize(Policy = "OperationsPolicy")]
    public class AdminCaregiversController : ControllerBase
    {
        private readonly IAdminCaregiverService _adminCaregiverService;
        private readonly ILogger<AdminCaregiversController> _logger;
        private readonly IUserDeletionService _userDeletionService;

        public AdminCaregiversController(
            IAdminCaregiverService adminCaregiverService,
            ILogger<AdminCaregiversController> logger,
            IUserDeletionService userDeletionService)
        {
            _adminCaregiverService = adminCaregiverService;
            _logger = logger;
            _userDeletionService = userDeletionService;
        }

        /// <summary>
        /// Edits a caregiver's legal name (FirstName / MiddleName / LastName)
        /// after the admin has confirmed the change. Mirrors First/Last to the
        /// linked AppUser. Records before/after in the AdminAuditLogs
        /// collection. Reason and Confirmed=true are required.
        /// </summary>
        [HttpPut("{caregiverId}/Name")]
        public async Task<IActionResult> UpdateCaregiverLegalName(
            string caregiverId,
            [FromBody] AdminUpdateCaregiverNameRequest request)
        {
            try
            {
                if (request == null)
                    return BadRequest(new { error = "Request body is required" });

                var result = await _adminCaregiverService
                    .UpdateCaregiverLegalNameAsync(caregiverId, request);

                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex,
                    "Validation error editing caregiver name {CaregiverId}", caregiverId);
                return BadRequest(new { error = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                _logger.LogWarning(ex,
                    "Caregiver not found for name edit: {CaregiverId}", caregiverId);
                return NotFound(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error editing caregiver name for {CaregiverId}", caregiverId);
                return StatusCode(500, new { error = "Failed to edit caregiver name" });
            }
        }

        [HttpPut("BulkClearMiddleName")]
        public async Task<IActionResult> BulkClearCaregiverMiddleName(
            [FromBody] AdminBulkClearMiddleNameRequest request)
        {
            try
            {
                if (request == null)
                    return BadRequest(new { error = "Request body is required" });

                var result = await _adminCaregiverService.BulkClearCaregiverMiddleNameAsync(request);
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Validation error on caregiver bulk clear middle name");
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during caregiver bulk clear middle name");
                return StatusCode(500, new { error = "Failed to bulk clear caregiver middle names" });
            }
        }

        /// <summary>
        /// Admin permanently schedules a caregiver account for deletion.
        /// Bypasses wallet balance blocker. Soft-deletes the account immediately
        /// and queues it for GDPR anonymisation after the standard grace period.
        /// </summary>
        [HttpDelete("{caregiverId}/account")]
        public async Task<IActionResult> AdminDeleteCaregiverAccount(
            string caregiverId,
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
                var result = await _userDeletionService.AdminDeleteCaregiverAccountAsync(caregiverId, adminId, adminEmail, request.Reason);

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
                _logger.LogError(ex, "Admin error deleting caregiver account {CaregiverId}", caregiverId);
                return StatusCode(500, new { message = "An error occurred while processing the deletion request." });
            }
        }
    }
}
