using Application.DTOs;
using Application.Interfaces.Content;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CarePro_Api.Controllers.Content
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class DisputesController : ControllerBase
    {
        private readonly IDisputeService _disputeService;
        private readonly ILogger<DisputesController> _logger;

        public DisputesController(IDisputeService disputeService, ILogger<DisputesController> logger)
        {
            _disputeService = disputeService;
            _logger = logger;
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

        /// <summary>
        /// Client raises a dispute on an order or a specific visit.
        /// </summary>
        [HttpPost("raise")]
        [Authorize(Roles = "Client, Admin, SuperAdmin")]
        public async Task<IActionResult> RaiseDisputeAsync([FromBody] RaiseDisputeRequest request)
        {
            try
            {
                var userId = GetCurrentUserId();
                if (string.IsNullOrWhiteSpace(userId))
                    return Unauthorized();

                var result = await _disputeService.RaiseDisputeAsync(request, userId);
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error raising dispute");
                return StatusCode(500, new { message = "An error occurred on the server." });
            }
        }

        /// <summary>
        /// Client reviews a specific visit (task sheet): approve or dispute.
        /// If disputed, a Dispute record is automatically created.
        /// </summary>
        [HttpPost("visit/{taskSheetId}/review")]
        [Authorize(Roles = "Client, Admin, SuperAdmin")]
        public async Task<IActionResult> ReviewVisitAsync(string taskSheetId, [FromBody] ReviewVisitRequest request)
        {
            try
            {
                var userId = GetCurrentUserId();
                if (string.IsNullOrWhiteSpace(userId))
                    return Unauthorized();

                var result = await _disputeService.ReviewVisitAsync(taskSheetId, request, userId);

                if (result == null)
                    return Ok(new { message = "Visit approved successfully." });

                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reviewing visit {TaskSheetId}", taskSheetId);
                return StatusCode(500, new { message = "An error occurred on the server." });
            }
        }

        /// <summary>
        /// Admin resolves a dispute with action, notes, and summary.
        /// </summary>
        [HttpPut("{disputeId}/resolve")]
        [Authorize(Roles = "Admin, SuperAdmin")]
        public async Task<IActionResult> ResolveDisputeAsync(string disputeId, [FromBody] ResolveDisputeRequest request)
        {
            try
            {
                var adminId = GetCurrentUserId();
                if (string.IsNullOrWhiteSpace(adminId))
                    return Unauthorized();

                var result = await _disputeService.ResolveDisputeAsync(disputeId, request, adminId);
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resolving dispute {DisputeId}", disputeId);
                return StatusCode(500, new { message = "An error occurred on the server." });
            }
        }

        /// <summary>
        /// Admin marks a dispute as under review.
        /// </summary>
        [HttpPut("{disputeId}/review")]
        [Authorize(Roles = "Admin, SuperAdmin")]
        public async Task<IActionResult> MarkUnderReviewAsync(string disputeId)
        {
            try
            {
                var adminId = GetCurrentUserId();
                if (string.IsNullOrWhiteSpace(adminId))
                    return Unauthorized();

                var result = await _disputeService.MarkUnderReviewAsync(disputeId, adminId);
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking dispute {DisputeId} under review", disputeId);
                return StatusCode(500, new { message = "An error occurred on the server." });
            }
        }

        /// <summary>
        /// Admin dismisses a dispute.
        /// </summary>
        [HttpPut("{disputeId}/dismiss")]
        [Authorize(Roles = "Admin, SuperAdmin")]
        public async Task<IActionResult> DismissDisputeAsync(string disputeId, [FromBody] ResolveDisputeRequest request)
        {
            try
            {
                var adminId = GetCurrentUserId();
                if (string.IsNullOrWhiteSpace(adminId))
                    return Unauthorized();

                var result = await _disputeService.DismissDisputeAsync(disputeId, request, adminId);
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error dismissing dispute {DisputeId}", disputeId);
                return StatusCode(500, new { message = "An error occurred on the server." });
            }
        }

        /// <summary>
        /// Get a single dispute by ID.
        /// </summary>
        [HttpGet("{disputeId}")]
        [Authorize(Roles = "Client, Caregiver, Admin, SuperAdmin")]
        public async Task<IActionResult> GetDisputeByIdAsync(string disputeId)
        {
            try
            {
                var result = await _disputeService.GetDisputeByIdAsync(disputeId);

                // IDOR: non-admin can only see disputes they're involved in
                if (!IsAdminOrSuperAdmin())
                {
                    var currentUserId = GetCurrentUserId();
                    if (currentUserId != result.ClientId && currentUserId != result.CaregiverId)
                        return Forbid();
                }

                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting dispute {DisputeId}", disputeId);
                return StatusCode(500, new { message = "An error occurred on the server." });
            }
        }

        /// <summary>
        /// Get all disputes for an order.
        /// </summary>
        [HttpGet("order/{orderId}")]
        [Authorize(Roles = "Client, Caregiver, Admin, SuperAdmin")]
        public async Task<IActionResult> GetDisputesByOrderIdAsync(string orderId)
        {
            try
            {
                var result = await _disputeService.GetDisputesByOrderIdAsync(orderId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting disputes for order {OrderId}", orderId);
                return StatusCode(500, new { message = "An error occurred on the server." });
            }
        }

        /// <summary>
        /// Get all disputes for a task sheet (visit).
        /// </summary>
        [HttpGet("visit/{taskSheetId}")]
        [Authorize(Roles = "Client, Caregiver, Admin, SuperAdmin")]
        public async Task<IActionResult> GetDisputesByTaskSheetIdAsync(string taskSheetId)
        {
            try
            {
                var result = await _disputeService.GetDisputesByTaskSheetIdAsync(taskSheetId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting disputes for task sheet {TaskSheetId}", taskSheetId);
                return StatusCode(500, new { message = "An error occurred on the server." });
            }
        }

        /// <summary>
        /// Admin: get all disputes with optional filters.
        /// </summary>
        [HttpGet("all")]
        [Authorize(Roles = "Admin, SuperAdmin")]
        public async Task<IActionResult> GetAllDisputesAsync(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? status = null,
            [FromQuery] string? disputeType = null)
        {
            try
            {
                var result = await _disputeService.GetAllDisputesAsync(page, pageSize, status, disputeType);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all disputes");
                return StatusCode(500, new { message = "An error occurred on the server." });
            }
        }
    }
}
