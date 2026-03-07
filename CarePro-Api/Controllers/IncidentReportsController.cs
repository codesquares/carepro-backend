using Application.DTOs;
using Application.Interfaces.Content;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CarePro_Api.Controllers
{
    [ApiController]
    [Route("api/incident-reports")]
    [Authorize(Roles = "Caregiver, Admin, SuperAdmin")]
    public class IncidentReportsController : ControllerBase
    {
        private readonly IIncidentReportService _incidentReportService;
        private readonly ILogger<IncidentReportsController> _logger;

        public IncidentReportsController(IIncidentReportService incidentReportService, ILogger<IncidentReportsController> logger)
        {
            _incidentReportService = incidentReportService;
            _logger = logger;
        }

        /// <summary>
        /// Create an incident report. Only the assigned caregiver can create. Immutable after creation.
        /// Critical and serious incidents trigger admin notifications automatically.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateIncidentReportRequest request)
        {
            try
            {
                var userId = GetCurrentUserId();
                if (string.IsNullOrEmpty(userId))
                    return Unauthorized(new { error = "Caregiver authorization required." });

                var result = await _incidentReportService.CreateAsync(request, userId);
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
                _logger.LogWarning("Invalid incident report request: {Message}", ex.Message);
                return BadRequest(new { error = ex.Message });
            }
            catch (FormatException)
            {
                return BadRequest(new { error = "Invalid base64 format in one or more photos." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating incident report");
                return StatusCode(500, new { error = "An error occurred while creating the incident report." });
            }
        }

        /// <summary>
        /// Get incident reports by order.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetByOrder([FromQuery] string orderId)
        {
            try
            {
                var userId = GetCurrentUserId();
                bool isAdmin = IsAdminOrSuperAdmin();

                if (string.IsNullOrEmpty(userId) && !isAdmin)
                    return Unauthorized(new { error = "Authorization required." });

                var result = await _incidentReportService.GetByOrderAsync(orderId, userId ?? "", isAdmin);
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
                _logger.LogError(ex, "Error retrieving incident reports");
                return StatusCode(500, new { error = "An error occurred while retrieving incident reports." });
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
