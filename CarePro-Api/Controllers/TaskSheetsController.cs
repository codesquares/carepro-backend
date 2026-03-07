using Application.DTOs;
using Application.Interfaces.Content;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CarePro_Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "Client, Caregiver, Admin, SuperAdmin")]
    public class TaskSheetsController : ControllerBase
    {
        private readonly ITaskSheetService _taskSheetService;
        private readonly ILogger<TaskSheetsController> _logger;

        public TaskSheetsController(ITaskSheetService taskSheetService, ILogger<TaskSheetsController> logger)
        {
            _taskSheetService = taskSheetService;
            _logger = logger;
        }

        /// <summary>
        /// Get all task sheets for a given order.
        /// </summary>
        [HttpGet("by-order/{orderId}")]
        public async Task<IActionResult> GetTaskSheetsByOrder(string orderId, [FromQuery] int? billingCycleNumber)
        {
            try
            {
                var userId = GetCurrentUserId();
                bool isAdmin = IsAdminOrSuperAdmin();

                if (string.IsNullOrEmpty(userId) && !isAdmin)
                    return Unauthorized(new { error = "Authorization required." });

                var result = await _taskSheetService.GetTaskSheetsByOrderAsync(orderId, billingCycleNumber, userId ?? "", isAdmin);
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
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning("Invalid request for GetTaskSheetsByOrder: {Message}", ex.Message);
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving task sheets for order {OrderId}", orderId);
                return StatusCode(500, new { error = "An error occurred while retrieving task sheets." });
            }
        }

        /// <summary>
        /// Create a new task sheet for an order, pre-populated with the order's gigPackageDetails.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CreateTaskSheet([FromBody] CreateTaskSheetRequest request)
        {
            try
            {
                var userId = GetCurrentUserId();
                if (string.IsNullOrEmpty(userId))
                    return Unauthorized(new { error = "Caregiver authorization required." });

                var result = await _taskSheetService.CreateTaskSheetAsync(request.OrderId, userId);

                _logger.LogInformation("TaskSheet created for Order: {OrderId} by Caregiver: {CaregiverId}",
                    request.OrderId, userId);

                return StatusCode(201, result);
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
                _logger.LogWarning("Invalid request for CreateTaskSheet: {Message}", ex.Message);
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating task sheet for order {OrderId}", request.OrderId);
                return StatusCode(500, new { error = "An error occurred while creating the task sheet." });
            }
        }

        /// <summary>
        /// Update a task sheet — toggle task completion, add new custom tasks.
        /// </summary>
        [HttpPut("{taskSheetId}")]
        public async Task<IActionResult> UpdateTaskSheet(string taskSheetId, [FromBody] UpdateTaskSheetRequest request)
        {
            try
            {
                var userId = GetCurrentUserId();
                if (string.IsNullOrEmpty(userId))
                    return Unauthorized(new { error = "Caregiver authorization required." });

                var result = await _taskSheetService.UpdateTaskSheetAsync(taskSheetId, request, userId);
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
                _logger.LogWarning("Invalid request for UpdateTaskSheet: {Message}", ex.Message);
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating task sheet {TaskSheetId}", taskSheetId);
                return StatusCode(500, new { error = "An error occurred while updating the task sheet." });
            }
        }

        /// <summary>
        /// Mark a task sheet as submitted. This finalizes the sheet.
        /// Requires a prior check-in. Optionally accepts a client signature (base64 PNG).
        /// </summary>
        [HttpPut("{taskSheetId}/submit")]
        public async Task<IActionResult> SubmitTaskSheet(string taskSheetId, [FromBody] SubmitTaskSheetRequest? request)
        {
            try
            {
                var userId = GetCurrentUserId();
                if (string.IsNullOrEmpty(userId))
                    return Unauthorized(new { error = "Caregiver authorization required." });

                var result = await _taskSheetService.SubmitTaskSheetAsync(taskSheetId, request ?? new SubmitTaskSheetRequest(), userId);
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error submitting task sheet {TaskSheetId}", taskSheetId);
                return StatusCode(500, new { error = "An error occurred while submitting the task sheet." });
            }
        }

        // ── Private helpers ──

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
