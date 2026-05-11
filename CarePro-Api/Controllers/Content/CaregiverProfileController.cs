using Application.DTOs;
using Application.Interfaces.Content;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace CarePro_Api.Controllers.Content
{
    /// <summary>
    /// Caregiver "Professional Profile" management — education, certifications/
    /// qualifications, and work experience. All write endpoints require the
    /// caller to be authenticated as a Caregiver. The caregiver's own ID is
    /// always taken from the JWT, never from the request body, to prevent IDOR.
    /// Public read of the same data is exposed via GET /api/Gigs/{gigId}.
    /// </summary>
    [Route("api/caregiver")]
    [ApiController]
    [Authorize(Roles = "Caregiver")]
    public class CaregiverProfileController : ControllerBase
    {
        private readonly ICaregiverProfileService profileService;
        private readonly ILogger<CaregiverProfileController> logger;

        public CaregiverProfileController(
            ICaregiverProfileService profileService,
            ILogger<CaregiverProfileController> logger)
        {
            this.profileService = profileService;
            this.logger = logger;
        }

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

        // ─────────────────────── EDUCATION ───────────────────────

        [HttpGet("education")]
        public async Task<IActionResult> GetEducation()
        {
            var guard = RequireCaregiver(out var caregiverId);
            if (guard != null) return guard;
            try
            {
                var items = await profileService.GetEducationAsync(caregiverId);
                return Ok(items);
            }
            catch (Exception ex) { return HandleException(ex, "GetEducation"); }
        }

        [HttpPost("education")]
        public async Task<IActionResult> AddEducation([FromBody] AddCaregiverEducationRequest request)
        {
            var guard = RequireCaregiver(out var caregiverId);
            if (guard != null) return guard;
            if (!ModelState.IsValid) return BadRequest(ModelState);
            try
            {
                var created = await profileService.AddEducationAsync(caregiverId, request);
                return CreatedAtAction(nameof(GetEducation), new { id = created.Id }, created);
            }
            catch (Exception ex) { return HandleException(ex, "AddEducation"); }
        }

        [HttpPut("education/{id}")]
        public async Task<IActionResult> UpdateEducation(string id, [FromBody] UpdateCaregiverEducationRequest request)
        {
            var guard = RequireCaregiver(out var caregiverId);
            if (guard != null) return guard;
            if (!ModelState.IsValid) return BadRequest(ModelState);
            try
            {
                var updated = await profileService.UpdateEducationAsync(caregiverId, id, request);
                return Ok(updated);
            }
            catch (Exception ex) { return HandleException(ex, "UpdateEducation"); }
        }

        [HttpDelete("education/{id}")]
        public async Task<IActionResult> DeleteEducation(string id)
        {
            var guard = RequireCaregiver(out var caregiverId);
            if (guard != null) return guard;
            try
            {
                await profileService.DeleteEducationAsync(caregiverId, id);
                return NoContent();
            }
            catch (Exception ex) { return HandleException(ex, "DeleteEducation"); }
        }

        // ─────────────── CERTIFICATIONS / QUALIFICATIONS ───────────────

        [HttpGet("certifications")]
        public async Task<IActionResult> GetCertifications()
        {
            var guard = RequireCaregiver(out var caregiverId);
            if (guard != null) return guard;
            try
            {
                var items = await profileService.GetQualificationsAsync(caregiverId);
                return Ok(items);
            }
            catch (Exception ex) { return HandleException(ex, "GetCertifications"); }
        }

        [HttpPost("certifications")]
        public async Task<IActionResult> AddCertification([FromBody] AddCaregiverQualificationRequest request)
        {
            var guard = RequireCaregiver(out var caregiverId);
            if (guard != null) return guard;
            if (!ModelState.IsValid) return BadRequest(ModelState);
            try
            {
                var created = await profileService.AddQualificationAsync(caregiverId, request);
                return CreatedAtAction(nameof(GetCertifications), new { id = created.Id }, created);
            }
            catch (Exception ex) { return HandleException(ex, "AddCertification"); }
        }

        [HttpPut("certifications/{id}")]
        public async Task<IActionResult> UpdateCertification(string id, [FromBody] UpdateCaregiverQualificationRequest request)
        {
            var guard = RequireCaregiver(out var caregiverId);
            if (guard != null) return guard;
            if (!ModelState.IsValid) return BadRequest(ModelState);
            try
            {
                var updated = await profileService.UpdateQualificationAsync(caregiverId, id, request);
                return Ok(updated);
            }
            catch (Exception ex) { return HandleException(ex, "UpdateCertification"); }
        }

        [HttpDelete("certifications/{id}")]
        public async Task<IActionResult> DeleteCertification(string id)
        {
            var guard = RequireCaregiver(out var caregiverId);
            if (guard != null) return guard;
            try
            {
                await profileService.DeleteQualificationAsync(caregiverId, id);
                return NoContent();
            }
            catch (Exception ex) { return HandleException(ex, "DeleteCertification"); }
        }

        // ─────────────────── WORK EXPERIENCE ───────────────────

        [HttpGet("work-experience")]
        public async Task<IActionResult> GetWorkExperience()
        {
            var guard = RequireCaregiver(out var caregiverId);
            if (guard != null) return guard;
            try
            {
                var items = await profileService.GetWorkExperienceAsync(caregiverId);
                return Ok(items);
            }
            catch (Exception ex) { return HandleException(ex, "GetWorkExperience"); }
        }

        [HttpPost("work-experience")]
        public async Task<IActionResult> AddWorkExperience([FromBody] AddCaregiverWorkExperienceRequest request)
        {
            var guard = RequireCaregiver(out var caregiverId);
            if (guard != null) return guard;
            if (!ModelState.IsValid) return BadRequest(ModelState);
            try
            {
                var created = await profileService.AddWorkExperienceAsync(caregiverId, request);
                return CreatedAtAction(nameof(GetWorkExperience), new { id = created.Id }, created);
            }
            catch (Exception ex) { return HandleException(ex, "AddWorkExperience"); }
        }

        [HttpPut("work-experience/{id}")]
        public async Task<IActionResult> UpdateWorkExperience(string id, [FromBody] UpdateCaregiverWorkExperienceRequest request)
        {
            var guard = RequireCaregiver(out var caregiverId);
            if (guard != null) return guard;
            if (!ModelState.IsValid) return BadRequest(ModelState);
            try
            {
                var updated = await profileService.UpdateWorkExperienceAsync(caregiverId, id, request);
                return Ok(updated);
            }
            catch (Exception ex) { return HandleException(ex, "UpdateWorkExperience"); }
        }

        [HttpDelete("work-experience/{id}")]
        public async Task<IActionResult> DeleteWorkExperience(string id)
        {
            var guard = RequireCaregiver(out var caregiverId);
            if (guard != null) return guard;
            try
            {
                await profileService.DeleteWorkExperienceAsync(caregiverId, id);
                return NoContent();
            }
            catch (Exception ex) { return HandleException(ex, "DeleteWorkExperience"); }
        }
    }
}
