using Application.DTOs;
using Application.Interfaces.Content;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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

        public AdminCaregiversController(
            IAdminCaregiverService adminCaregiverService,
            ILogger<AdminCaregiversController> logger)
        {
            _adminCaregiverService = adminCaregiverService;
            _logger = logger;
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
    }
}
