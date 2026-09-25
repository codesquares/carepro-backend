using Application.DTOs;
using Application.Interfaces.Content;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;

namespace CarePro_Api.Controllers.Content
{
    /// <summary>
    /// Operations-staff endpoints for caregiver vetting (Phase 2). Routes live under
    /// api/Admin/Caregivers/Vetting so the caregiver-facing CaregiverVettingController
    /// is untouched. Gated by OperationsPolicy — same as the admin matching tool and
    /// the other staff-facing caregiver actions. The acting admin id is always taken
    /// from the JWT, never the request body.
    /// </summary>
    [Route("api/Admin/Caregivers/Vetting")]
    [ApiController]
    [Authorize(Policy = "OperationsPolicy")]
    public class AdminCaregiverVettingController : ControllerBase
    {
        private readonly IGuarantorService _guarantorService;
        private readonly ICaregiverVettingService _vettingService;
        private readonly ILogger<AdminCaregiverVettingController> _logger;

        public AdminCaregiverVettingController(
            IGuarantorService guarantorService,
            ICaregiverVettingService vettingService,
            ILogger<AdminCaregiverVettingController> logger)
        {
            _guarantorService = guarantorService;
            _vettingService = vettingService;
            _logger = logger;
        }

        private (string? adminId, string adminEmail) CurrentAdmin()
        {
            var adminId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                          ?? User.FindFirst("sub")?.Value
                          ?? User.FindFirst("userId")?.Value;
            var adminEmail = User.FindFirst(ClaimTypes.Email)?.Value
                             ?? User.FindFirst("email")?.Value
                             ?? string.Empty;
            return (adminId, adminEmail);
        }

        private string? RequestOrigin() =>
            Request.Headers.TryGetValue("Origin", out var o) ? o.ToString() : null;

        private IActionResult HandleException(Exception ex, string action)
        {
            switch (ex)
            {
                case ArgumentException ae:
                    return BadRequest(new { message = ae.Message });
                case InvalidOperationException ioe:
                    return Conflict(new { message = ioe.Message });
                case KeyNotFoundException knf:
                    return NotFound(new { message = knf.Message });
                default:
                    _logger.LogError(ex, "Unexpected error during {Action}", action);
                    return StatusCode(500, new { message = "An unexpected error occurred." });
            }
        }

        // ─────────────────── 9.1  EXPERIENCE TIER (payroll) ───────────────────

        /// <summary>Staff view of a caregiver's payroll experience tier.</summary>
        [HttpGet("caregivers/{caregiverId}/experience-tier")]
        public async Task<IActionResult> GetExperienceTier(string caregiverId)
        {
            try
            {
                return Ok(await _vettingService.GetExperienceTierAsync(caregiverId));
            }
            catch (Exception ex) { return HandleException(ex, "AdminGetExperienceTier"); }
        }

        /// <summary>
        /// Staff sets a caregiver's payroll experience tier (Junior/Mid/Senior). Drives
        /// CaregiverPayRate lookup — admin-assigned, not self-declared by the caregiver.
        /// </summary>
        [HttpPut("caregivers/{caregiverId}/experience-tier")]
        public async Task<IActionResult> SetExperienceTier(string caregiverId, [FromBody] SetCaregiverExperienceTierRequest request)
        {
            if (request == null) return BadRequest(new { message = "Request body is required." });
            if (!ModelState.IsValid) return BadRequest(ModelState);

            try
            {
                return Ok(await _vettingService.SetExperienceTierAsync(caregiverId, request));
            }
            catch (Exception ex) { return HandleException(ex, "AdminSetExperienceTier"); }
        }

        /// <summary>Staff view of a caregiver's guarantors.</summary>
        [HttpGet("caregivers/{caregiverId}/guarantors")]
        public async Task<IActionResult> GetGuarantors(string caregiverId)
        {
            try
            {
                return Ok(await _guarantorService.AdminGetGuarantorsForCaregiverAsync(caregiverId));
            }
            catch (Exception ex) { return HandleException(ex, "AdminGetGuarantors"); }
        }

        /// <summary>
        /// Staff manually confirms a guarantor (email bounced / link lost / verified by phone),
        /// bypassing the self-serve link flow. Requires a reason; recorded in AdminAuditLogs
        /// and attributed to the acting admin. Idempotent: a no-op if already confirmed.
        /// </summary>
        [HttpPut("guarantors/{guarantorId}/confirm")]
        public async Task<IActionResult> ConfirmGuarantor(string guarantorId, [FromBody] AdminConfirmGuarantorRequest request)
        {
            if (request == null) return BadRequest(new { message = "Request body is required." });
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var (adminId, adminEmail) = CurrentAdmin();
            if (string.IsNullOrEmpty(adminId))
                return Unauthorized(new { message = "Unable to identify admin user." });

            try
            {
                var result = await _guarantorService.AdminConfirmGuarantorAsync(
                    guarantorId, adminId, adminEmail, request.Reason);
                return Ok(result);
            }
            catch (Exception ex) { return HandleException(ex, "AdminConfirmGuarantor"); }
        }

        /// <summary>
        /// Staff resends a guarantor's confirmation link on the caregiver's behalf.
        /// Bypasses the resend cooldown; still subject to the lifetime attempt cap.
        /// </summary>
        [HttpPost("guarantors/{guarantorId}/resend-confirmation")]
        public async Task<IActionResult> ResendConfirmation(string guarantorId)
        {
            var (adminId, adminEmail) = CurrentAdmin();
            if (string.IsNullOrEmpty(adminId))
                return Unauthorized(new { message = "Unable to identify admin user." });

            try
            {
                var result = await _guarantorService.AdminResendConfirmationLinkAsync(
                    guarantorId, adminId, adminEmail, RequestOrigin());
                return Ok(result);
            }
            catch (Exception ex) { return HandleException(ex, "AdminResendGuarantorConfirmation"); }
        }
    }
}
