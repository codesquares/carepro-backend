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
    public class RefundRequestsController : ControllerBase
    {
        private readonly IRefundRequestService _refundRequestService;
        private readonly ILogger<RefundRequestsController> _logger;

        public RefundRequestsController(
            IRefundRequestService refundRequestService,
            ILogger<RefundRequestsController> logger)
        {
            _refundRequestService = refundRequestService;
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
        /// Client submits a refund request to withdraw wallet funds to their bank account.
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "Client")]
        public async Task<IActionResult> CreateRefundRequest([FromBody] CreateRefundRequestDTO request)
        {
            try
            {
                var clientId = GetCurrentUserId();
                if (string.IsNullOrEmpty(clientId))
                    return Unauthorized(new { error = "Client authorization required." });

                var result = await _refundRequestService.CreateRefundRequestAsync(request, clientId);
                if (!result.IsSuccess)
                    return BadRequest(new { errors = result.Errors });

                return Ok(result.Value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating refund request");
                return StatusCode(500, new { error = "An error occurred while submitting the refund request." });
            }
        }

        /// <summary>
        /// Client views their own refund requests.
        /// </summary>
        [HttpGet("my-requests")]
        [Authorize(Roles = "Client")]
        public async Task<IActionResult> GetMyRefundRequests()
        {
            try
            {
                var clientId = GetCurrentUserId();
                if (string.IsNullOrEmpty(clientId))
                    return Unauthorized(new { error = "Client authorization required." });

                var requests = await _refundRequestService.GetClientRefundRequestsAsync(clientId);
                return Ok(requests);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching client refund requests");
                return StatusCode(500, new { error = "An error occurred while fetching refund requests." });
            }
        }

        /// <summary>
        /// Get a single refund request by ID.
        /// Clients can only view their own; admins can view any.
        /// </summary>
        [HttpGet("{requestId}")]
        [Authorize(Roles = "Client, Admin, SuperAdmin")]
        public async Task<IActionResult> GetRefundRequest(string requestId)
        {
            try
            {
                string? clientId = IsAdminOrSuperAdmin() ? null : GetCurrentUserId();
                var request = await _refundRequestService.GetRefundRequestAsync(requestId, clientId);
                return Ok(request);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { error = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching refund request {RequestId}", requestId);
                return StatusCode(500, new { error = "An error occurred while fetching the refund request." });
            }
        }

        /// <summary>
        /// Admin: get all refund requests. Optional ?status=Pending filter.
        /// </summary>
        [HttpGet("all")]
        [Authorize(Roles = "Admin, SuperAdmin")]
        public async Task<IActionResult> GetAllRefundRequests([FromQuery] string? status = null)
        {
            try
            {
                var requests = await _refundRequestService.GetAllRefundRequestsAsync(status);
                return Ok(requests);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching all refund requests");
                return StatusCode(500, new { error = "An error occurred while fetching refund requests." });
            }
        }

        /// <summary>
        /// Admin reviews (approves or rejects) a refund request.
        /// On approval, the client's wallet is debited.
        /// </summary>
        [HttpPut("{requestId}/review")]
        [Authorize(Roles = "Admin, SuperAdmin")]
        public async Task<IActionResult> ReviewRefundRequest(string requestId, [FromBody] ReviewRefundRequestDTO review)
        {
            try
            {
                var adminId = GetCurrentUserId();
                if (string.IsNullOrEmpty(adminId))
                    return Unauthorized(new { error = "Admin authorization required." });

                var result = await _refundRequestService.ReviewRefundRequestAsync(requestId, review, adminId);
                if (!result.IsSuccess)
                    return BadRequest(new { errors = result.Errors });

                return Ok(result.Value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reviewing refund request {RequestId}", requestId);
                return StatusCode(500, new { error = "An error occurred while reviewing the refund request." });
            }
        }

        /// <summary>
        /// Admin marks an approved refund as completed (after bank transfer is done).
        /// </summary>
        [HttpPut("{requestId}/complete")]
        [Authorize(Roles = "Admin, SuperAdmin")]
        public async Task<IActionResult> CompleteRefundRequest(string requestId)
        {
            try
            {
                var adminId = GetCurrentUserId();
                if (string.IsNullOrEmpty(adminId))
                    return Unauthorized(new { error = "Admin authorization required." });

                var result = await _refundRequestService.CompleteRefundRequestAsync(requestId, adminId);
                if (!result.IsSuccess)
                    return BadRequest(new { errors = result.Errors });

                return Ok(result.Value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error completing refund request {RequestId}", requestId);
                return StatusCode(500, new { error = "An error occurred while completing the refund request." });
            }
        }
    }
}
