using Application.DTOs;
using Application.Interfaces.Content;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CarePro_Api.Controllers
{
    [ApiController]
    [Route("api/observation-reports")]
    [Authorize(Roles = "Caregiver, Admin, SuperAdmin")]
    public class ObservationReportsController : ControllerBase
    {
        private readonly IObservationReportService _observationReportService;
        private readonly ILogger<ObservationReportsController> _logger;

        public ObservationReportsController(IObservationReportService observationReportService, ILogger<ObservationReportsController> logger)
        {
            _observationReportService = observationReportService;
            _logger = logger;
        }

        /// <summary>
        /// Create an observation report. Only the assigned caregiver can create. Immutable after creation.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateObservationReportRequest request)
        {
            try
            {
                var userId = GetCurrentUserId();
                if (string.IsNullOrEmpty(userId))
                    return Unauthorized(new { error = "Caregiver authorization required." });

                var result = await _observationReportService.CreateAsync(request, userId);
                return StatusCode(201, result);
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { error = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning("Invalid observation report request: {Message}", ex.Message);
                return BadRequest(new { error = ex.Message });
            }
            catch (FormatException)
            {
                return BadRequest(new { error = "Invalid base64 format in one or more photos." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating observation report");
                return StatusCode(500, new { error = "An error occurred while creating the observation report." });
            }
        }

        /// <summary>
        /// Get observation reports by order. Optionally filter by taskSheetId.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetByOrder([FromQuery] string orderId, [FromQuery] string? taskSheetId)
        {
            try
            {
                var userId = GetCurrentUserId();
                bool isAdmin = IsAdminOrSuperAdmin();

                if (string.IsNullOrEmpty(userId) && !isAdmin)
                    return Unauthorized(new { error = "Authorization required." });

                var result = await _observationReportService.GetByOrderAsync(orderId, taskSheetId, userId ?? "", isAdmin);
                return Ok(result);
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { error = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving observation reports");
                return StatusCode(500, new { error = "An error occurred while retrieving observation reports." });
            }
        }

        private string? GetCurrentUserId() =>
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirst("sub")?.Value
            ?? User.FindFirst("userId")?.Value;

        private bool IsAdminOrSuperAdmin()
        {
            var role = User.FindFirstValue(ClaimTypes.Role);
            return role == "Admin" || role == "SuperAdmin";
        }
    }
}
