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
    /// Phase 4 — the caregiver side of internal assignment: view assignments,
    /// accept (finalizes to the client) or decline (back to staff, no auto-reassign).
    /// </summary>
    [Route("api/caregiver/assignments")]
    [ApiController]
    [Authorize(Roles = "Caregiver")]
    public class CaregiverAssignmentsController : ControllerBase
    {
        private readonly IAssignmentService _assignmentService;
        private readonly ILogger<CaregiverAssignmentsController> _logger;

        public CaregiverAssignmentsController(
            IAssignmentService assignmentService,
            ILogger<CaregiverAssignmentsController> logger)
        {
            _assignmentService = assignmentService;
            _logger = logger;
        }

        private string? CurrentCaregiverId() =>
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirst("sub")?.Value
            ?? User.FindFirst("userId")?.Value;

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
                    _logger.LogWarning("IDOR attempt during {Action}: {Message}", action, ua.Message);
                    return StatusCode(403, new { message = ua.Message });
                default:
                    _logger.LogError(ex, "Unexpected error during {Action}", action);
                    return StatusCode(500, new { message = "An unexpected error occurred." });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetMine()
        {
            var caregiverId = CurrentCaregiverId();
            if (string.IsNullOrWhiteSpace(caregiverId))
                return Unauthorized(new { message = "Caregiver identity not found in token." });
            try
            {
                var items = await _assignmentService.GetMyAssignmentsAsync(caregiverId);
                return Ok(new { success = true, data = items, count = items.Count });
            }
            catch (Exception ex) { return HandleException(ex, "GetMine"); }
        }

        [HttpPost("{id}/accept")]
        public async Task<IActionResult> Accept(string id)
        {
            var caregiverId = CurrentCaregiverId();
            if (string.IsNullOrWhiteSpace(caregiverId))
                return Unauthorized(new { message = "Caregiver identity not found in token." });
            try
            {
                return Ok(await _assignmentService.AcceptAsync(id, caregiverId));
            }
            catch (Exception ex) { return HandleException(ex, "AcceptAssignment"); }
        }

        [HttpPost("{id}/decline")]
        public async Task<IActionResult> Decline(string id, [FromBody] DeclineAssignmentRequest? request)
        {
            var caregiverId = CurrentCaregiverId();
            if (string.IsNullOrWhiteSpace(caregiverId))
                return Unauthorized(new { message = "Caregiver identity not found in token." });
            try
            {
                return Ok(await _assignmentService.DeclineAsync(id, caregiverId, request?.Reason));
            }
            catch (Exception ex) { return HandleException(ex, "DeclineAssignment"); }
        }
    }
}
