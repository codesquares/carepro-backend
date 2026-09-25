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
    /// Caregiver vetting data capture (Phase 2): professional classification,
    /// guarantors, address history, social media handles. All write endpoints
    /// require the caller to be an authenticated Caregiver; the caregiver id is
    /// always taken from the JWT, never the request body, to prevent IDOR.
    /// </summary>
    [Route("api/caregiver/vetting")]
    [ApiController]
    [Authorize(Roles = "Caregiver")]
    public class CaregiverVettingController : ControllerBase
    {
        private readonly ICaregiverVettingService vettingService;
        private readonly IGuarantorService guarantorService;
        private readonly ICaregiverReadinessService readinessService;
        private readonly ILogger<CaregiverVettingController> logger;

        public CaregiverVettingController(
            ICaregiverVettingService vettingService,
            IGuarantorService guarantorService,
            ICaregiverReadinessService readinessService,
            ILogger<CaregiverVettingController> logger)
        {
            this.vettingService = vettingService;
            this.guarantorService = guarantorService;
            this.readinessService = readinessService;
            this.logger = logger;
        }

        private string? RequestOrigin() =>
            Request.Headers.TryGetValue("Origin", out var o) ? o.ToString() : null;

        private string? GetCurrentCaregiverId() =>
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirst("sub")?.Value
            ?? User.FindFirst("userId")?.Value;

        private IActionResult? RequireCaregiver(out string caregiverId)
        {
            caregiverId = GetCurrentCaregiverId() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(caregiverId))
                return Unauthorized(new { message = "Caregiver identity not found in token." });
            return null;
        }

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
                case UnauthorizedAccessException ua:
                    logger.LogWarning("IDOR attempt during {Action}: {Message}", action, ua.Message);
                    return StatusCode(403, new { message = ua.Message });
                default:
                    logger.LogError(ex, "Unexpected error during {Action}", action);
                    return StatusCode(500, new { message = "An unexpected error occurred." });
            }
        }

        // ─────────────────── READINESS ───────────────────

        /// <summary>
        /// The caller's own hire-readiness state, including IneligibilityReasons
        /// (e.g. "guarantors_incomplete", "address_history_incomplete",
        /// "caregiver_type_not_set"). Caregiver id is always taken from the JWT —
        /// there is no route parameter, so a caregiver can never query another
        /// caregiver's readiness. Wraps the existing ICaregiverReadinessService,
        /// which until now was only consumed server-to-server.
        /// </summary>
        [HttpGet("readiness")]
        public async Task<IActionResult> GetReadiness()
        {
            var guard = RequireCaregiver(out var caregiverId);
            if (guard != null) return guard;
            try
            {
                return Ok(await readinessService.GetReadinessAsync(caregiverId));
            }
            catch (Exception ex) { return HandleException(ex, "GetReadiness"); }
        }

        // ─────────────────── 2.1  CLASSIFICATION ───────────────────

        [HttpGet("classification")]
        public async Task<IActionResult> GetClassification()
        {
            var guard = RequireCaregiver(out var caregiverId);
            if (guard != null) return guard;
            try
            {
                return Ok(await vettingService.GetClassificationAsync(caregiverId));
            }
            catch (Exception ex) { return HandleException(ex, "GetClassification"); }
        }

        [HttpPut("classification")]
        public async Task<IActionResult> SetClassification([FromBody] SetCaregiverClassificationRequest request)
        {
            var guard = RequireCaregiver(out var caregiverId);
            if (guard != null) return guard;
            if (!ModelState.IsValid) return BadRequest(ModelState);
            try
            {
                return Ok(await vettingService.SetClassificationAsync(caregiverId, request));
            }
            catch (Exception ex) { return HandleException(ex, "SetClassification"); }
        }

        // ─────────────────── 2.2  GUARANTORS ───────────────────

        [HttpGet("guarantors")]
        public async Task<IActionResult> GetGuarantors()
        {
            var guard = RequireCaregiver(out var caregiverId);
            if (guard != null) return guard;
            try
            {
                return Ok(await guarantorService.GetGuarantorsAsync(caregiverId));
            }
            catch (Exception ex) { return HandleException(ex, "GetGuarantors"); }
        }

        [HttpPost("guarantors")]
        public async Task<IActionResult> AddGuarantor([FromBody] AddGuarantorRequest request)
        {
            var guard = RequireCaregiver(out var caregiverId);
            if (guard != null) return guard;
            if (!ModelState.IsValid) return BadRequest(ModelState);
            try
            {
                var created = await guarantorService.AddGuarantorAsync(caregiverId, request);
                return CreatedAtAction(nameof(GetGuarantors), new { id = created.Id }, created);
            }
            catch (Exception ex) { return HandleException(ex, "AddGuarantor"); }
        }

        [HttpPut("guarantors/{id}")]
        public async Task<IActionResult> UpdateGuarantor(string id, [FromBody] UpdateGuarantorRequest request)
        {
            var guard = RequireCaregiver(out var caregiverId);
            if (guard != null) return guard;
            if (!ModelState.IsValid) return BadRequest(ModelState);
            try
            {
                return Ok(await guarantorService.UpdateGuarantorAsync(caregiverId, id, request));
            }
            catch (Exception ex) { return HandleException(ex, "UpdateGuarantor"); }
        }

        [HttpDelete("guarantors/{id}")]
        public async Task<IActionResult> DeleteGuarantor(string id)
        {
            var guard = RequireCaregiver(out var caregiverId);
            if (guard != null) return guard;
            try
            {
                await guarantorService.DeleteGuarantorAsync(caregiverId, id);
                return NoContent();
            }
            catch (Exception ex) { return HandleException(ex, "DeleteGuarantor"); }
        }

        [HttpPost("guarantors/{id}/send-confirmation")]
        public async Task<IActionResult> SendGuarantorConfirmation(string id)
        {
            var guard = RequireCaregiver(out var caregiverId);
            if (guard != null) return guard;
            try
            {
                return Ok(await guarantorService.SendConfirmationLinkAsync(caregiverId, id, RequestOrigin()));
            }
            catch (Exception ex) { return HandleException(ex, "SendGuarantorConfirmation"); }
        }

        // ─────────────────── 2.3  ADDRESS HISTORY ───────────────────

        [HttpGet("address-history")]
        public async Task<IActionResult> GetAddressHistory()
        {
            var guard = RequireCaregiver(out var caregiverId);
            if (guard != null) return guard;
            try
            {
                return Ok(await vettingService.GetAddressHistoryAsync(caregiverId));
            }
            catch (Exception ex) { return HandleException(ex, "GetAddressHistory"); }
        }

        [HttpGet("address-history/coverage")]
        public async Task<IActionResult> GetAddressHistoryCoverage()
        {
            var guard = RequireCaregiver(out var caregiverId);
            if (guard != null) return guard;
            try
            {
                return Ok(await vettingService.GetAddressHistoryCoverageAsync(caregiverId));
            }
            catch (Exception ex) { return HandleException(ex, "GetAddressHistoryCoverage"); }
        }

        [HttpPost("address-history")]
        public async Task<IActionResult> AddAddressHistory([FromBody] AddCaregiverAddressHistoryRequest request)
        {
            var guard = RequireCaregiver(out var caregiverId);
            if (guard != null) return guard;
            if (!ModelState.IsValid) return BadRequest(ModelState);
            try
            {
                var created = await vettingService.AddAddressHistoryAsync(caregiverId, request);
                return CreatedAtAction(nameof(GetAddressHistory), new { id = created.Id }, created);
            }
            catch (Exception ex) { return HandleException(ex, "AddAddressHistory"); }
        }

        [HttpPut("address-history/{id}")]
        public async Task<IActionResult> UpdateAddressHistory(string id, [FromBody] UpdateCaregiverAddressHistoryRequest request)
        {
            var guard = RequireCaregiver(out var caregiverId);
            if (guard != null) return guard;
            if (!ModelState.IsValid) return BadRequest(ModelState);
            try
            {
                return Ok(await vettingService.UpdateAddressHistoryAsync(caregiverId, id, request));
            }
            catch (Exception ex) { return HandleException(ex, "UpdateAddressHistory"); }
        }

        [HttpDelete("address-history/{id}")]
        public async Task<IActionResult> DeleteAddressHistory(string id)
        {
            var guard = RequireCaregiver(out var caregiverId);
            if (guard != null) return guard;
            try
            {
                await vettingService.DeleteAddressHistoryAsync(caregiverId, id);
                return NoContent();
            }
            catch (Exception ex) { return HandleException(ex, "DeleteAddressHistory"); }
        }

        // ─────────────────── 2.4  SOCIAL MEDIA HANDLES ───────────────────

        [HttpGet("social-media")]
        public async Task<IActionResult> GetSocialMediaHandles()
        {
            var guard = RequireCaregiver(out var caregiverId);
            if (guard != null) return guard;
            try
            {
                return Ok(await vettingService.GetSocialMediaHandlesAsync(caregiverId));
            }
            catch (Exception ex) { return HandleException(ex, "GetSocialMediaHandles"); }
        }

        [HttpPost("social-media")]
        public async Task<IActionResult> AddSocialMediaHandle([FromBody] AddCaregiverSocialMediaHandleRequest request)
        {
            var guard = RequireCaregiver(out var caregiverId);
            if (guard != null) return guard;
            if (!ModelState.IsValid) return BadRequest(ModelState);
            try
            {
                var created = await vettingService.AddSocialMediaHandleAsync(caregiverId, request);
                return CreatedAtAction(nameof(GetSocialMediaHandles), new { id = created.Id }, created);
            }
            catch (Exception ex) { return HandleException(ex, "AddSocialMediaHandle"); }
        }

        [HttpPut("social-media/{id}")]
        public async Task<IActionResult> UpdateSocialMediaHandle(string id, [FromBody] UpdateCaregiverSocialMediaHandleRequest request)
        {
            var guard = RequireCaregiver(out var caregiverId);
            if (guard != null) return guard;
            if (!ModelState.IsValid) return BadRequest(ModelState);
            try
            {
                return Ok(await vettingService.UpdateSocialMediaHandleAsync(caregiverId, id, request));
            }
            catch (Exception ex) { return HandleException(ex, "UpdateSocialMediaHandle"); }
        }

        [HttpDelete("social-media/{id}")]
        public async Task<IActionResult> DeleteSocialMediaHandle(string id)
        {
            var guard = RequireCaregiver(out var caregiverId);
            if (guard != null) return guard;
            try
            {
                await vettingService.DeleteSocialMediaHandleAsync(caregiverId, id);
                return NoContent();
            }
            catch (Exception ex) { return HandleException(ex, "DeleteSocialMediaHandle"); }
        }

        /// <summary>
        /// Self-serve endpoint a guarantor hits by clicking the link in their email.
        /// No authentication — the signed token IS the authorisation. POST (not GET)
        /// so email link-prefetchers cannot auto-confirm.
        /// </summary>
        [HttpPost("guarantors/confirm")]
        [AllowAnonymous]
        public async Task<IActionResult> ConfirmGuarantor([FromQuery] string token)
        {
            try
            {
                var result = await guarantorService.ConfirmByTokenAsync(token);
                return result.Success ? Ok(result) : BadRequest(result);
            }
            catch (Exception ex) { return HandleException(ex, "ConfirmGuarantor"); }
        }
    }
}
