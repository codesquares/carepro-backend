using Application.DTOs;
using Application.Interfaces.Content;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CarePro_Api.Controllers
{
    [ApiController]
    [Route("api/visit-checkin")]
    [Authorize(Roles = "Caregiver, Admin, SuperAdmin")]
    public class VisitCheckinsController : ControllerBase
    {
        private readonly IVisitCheckinService _visitCheckinService;
        private readonly ILogger<VisitCheckinsController> _logger;

        public VisitCheckinsController(IVisitCheckinService visitCheckinService, ILogger<VisitCheckinsController> logger)
        {
            _visitCheckinService = visitCheckinService;
            _logger = logger;
        }

        /// <summary>
        /// Record caregiver arrival at client's location. Idempotent — returns existing check-in if already checked in.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Checkin([FromBody] VisitCheckinRequest request)
        {
            try
            {
                var userId = GetCurrentUserId();
                if (string.IsNullOrEmpty(userId))
                    return Unauthorized(new { error = "Caregiver authorization required." });

                var result = await _visitCheckinService.CheckinAsync(request, userId);
                return Ok(result);
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning("Invalid checkin request: {Message}", ex.Message);
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during visit check-in");
                return StatusCode(500, new { error = "An error occurred during check-in." });
            }
        }

        private string? GetCurrentUserId() =>
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirst("sub")?.Value
            ?? User.FindFirst("userId")?.Value;
    }
}
