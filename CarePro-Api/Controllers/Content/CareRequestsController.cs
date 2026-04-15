using Application.DTOs;
using Application.Interfaces.Content;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CarePro_Api.Controllers.Content
{
    [Route("api/[controller]")]
    [ApiController]
    public class CareRequestsController : ControllerBase
    {
        private readonly ICareRequestService _careRequestService;
        private readonly ICareRequestMatchingService _matchingService;
        private readonly ICareRequestResponseService _responseService;
        private readonly ILogger<CareRequestsController> _logger;

        public CareRequestsController(
            ICareRequestService careRequestService,
            ICareRequestMatchingService matchingService,
            ICareRequestResponseService responseService,
            ILogger<CareRequestsController> logger)
        {
            _careRequestService = careRequestService;
            _matchingService = matchingService;
            _responseService = responseService;
            _logger = logger;
        }

        /// <summary>
        /// Create a new care request
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "Client")]
        public async Task<IActionResult> CreateCareRequest([FromBody] CreateCareRequestDTO createCareRequestDTO)
        {
            try
            {
                _logger.LogInformation($"Creating care request for ClientId: {createCareRequestDTO.ClientId}");

                var careRequest = await _careRequestService.CreateCareRequestAsync(createCareRequestDTO);

                return Ok(new CareRequestResponse
                {
                    Success = true,
                    Message = "Care request created successfully",
                    Data = careRequest
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating care request");
                return StatusCode(500, new { success = false, message = "An error occurred while creating the care request." });
            }
        }

        /// <summary>
        /// Get all care requests for a specific client
        /// </summary>
        [HttpGet("client/{clientId}")]
        [Authorize(Roles = "Client, Admin")]
        public async Task<IActionResult> GetCareRequestsByClientId(string clientId)
        {
            try
            {
                // Enforce ownership: Clients can only fetch their own requests
                var isAdmin = User.IsInRole("Admin");
                if (!isAdmin)
                {
                    var authenticatedUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                                              ?? User.FindFirst("sub")?.Value
                                              ?? User.FindFirst("userId")?.Value;

                    if (string.IsNullOrEmpty(authenticatedUserId) || authenticatedUserId != clientId)
                    {
                        _logger.LogWarning("Client {AuthUserId} attempted to access requests for ClientId: {ClientId}", authenticatedUserId, clientId);
                        return Forbid();
                    }
                }

                _logger.LogInformation("Retrieving care requests for ClientId: {ClientId}", clientId);

                var careRequests = await _careRequestService.GetCareRequestsByClientIdAsync(clientId);

                return Ok(new CareRequestListResponse
                {
                    Success = true,
                    Message = "Care requests retrieved successfully",
                    Data = careRequests,
                    TotalCount = careRequests.Count
                });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving care requests for ClientId: {clientId}");
                return StatusCode(500, new { success = false, message = "An error occurred while retrieving care requests." });
            }
        }

        /// <summary>
        /// Get a single care request by ID
        /// </summary>
        [HttpGet("{id}")]
        [Authorize(Roles = "Client, Caregiver, Admin")]
        public async Task<IActionResult> GetCareRequestById(string id)
        {
            try
            {
                _logger.LogInformation($"Retrieving care request with ID: {id}");

                var careRequest = await _careRequestService.GetCareRequestByIdAsync(id);

                return Ok(new CareRequestResponse
                {
                    Success = true,
                    Message = "Care request retrieved successfully",
                    Data = careRequest
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving care request with ID: {id}");
                return StatusCode(500, new { success = false, message = "An error occurred while retrieving the care request." });
            }
        }

        /// <summary>
        /// Update a care request
        /// </summary>
        [HttpPut("{id}")]
        [Authorize(Roles = "Client")]
        public async Task<IActionResult> UpdateCareRequest(string id, [FromBody] UpdateCareRequestDTO updateCareRequestDTO)
        {
            try
            {
                _logger.LogInformation($"Updating care request with ID: {id}");

                var careRequest = await _careRequestService.UpdateCareRequestAsync(id, updateCareRequestDTO);

                return Ok(new CareRequestResponse
                {
                    Success = true,
                    Message = "Care request updated successfully",
                    Data = careRequest
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { success = false, message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating care request with ID: {id}");
                return StatusCode(500, new { success = false, message = "An error occurred while updating the care request." });
            }
        }

        /// <summary>
        /// Cancel a care request
        /// </summary>
        [HttpPut("{id}/cancel")]
        [Authorize(Roles = "Client, Admin")]
        public async Task<IActionResult> CancelCareRequest(string id)
        {
            try
            {
                _logger.LogInformation($"Cancelling care request with ID: {id}");

                var careRequest = await _careRequestService.CancelCareRequestAsync(id);

                return Ok(new CareRequestResponse
                {
                    Success = true,
                    Message = "Care request cancelled successfully",
                    Data = careRequest
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { success = false, message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error cancelling care request with ID: {id}");
                return StatusCode(500, new { success = false, message = "An error occurred while cancelling the care request." });
            }
        }

        /// <summary>
        /// Get all pending care requests (for admin/caregiver matching)
        /// </summary>
        [HttpGet("pending")]
        [Authorize(Roles = "Caregiver, Admin")]
        public async Task<IActionResult> GetPendingCareRequests()
        {
            try
            {
                _logger.LogInformation("Retrieving pending care requests");

                var pendingRequests = await _careRequestService.GetPendingCareRequestsAsync();

                return Ok(new CareRequestListResponse
                {
                    Success = true,
                    Message = "Pending care requests retrieved successfully",
                    Data = pendingRequests,
                    TotalCount = pendingRequests.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving pending care requests");
                return StatusCode(500, new { success = false, message = "An error occurred while retrieving pending care requests." });
            }
        }

        /// <summary>
        /// Update the status of a care request (Admin only)
        /// </summary>
        [HttpPut("{id}/status")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateCareRequestStatus(string id, [FromBody] UpdateStatusRequest request)
        {
            try
            {
                _logger.LogInformation($"Updating status of care request with ID: {id} to {request.Status}");

                var careRequest = await _careRequestService.UpdateCareRequestStatusAsync(id, request.Status);

                return Ok(new CareRequestResponse
                {
                    Success = true,
                    Message = $"Care request status updated to '{request.Status}' successfully",
                    Data = careRequest
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating status of care request with ID: {id}");
                return StatusCode(500, new { success = false, message = "An error occurred while updating the care request status." });
            }
        }

        /// <summary>
        /// Get caregiver matches for a care request. Only the owning client or an admin can access.
        /// </summary>
        [HttpGet("{id}/matches")]
        [Authorize(Roles = "Client, Admin")]
        public async Task<IActionResult> GetCareRequestMatches(string id)
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                             ?? User.FindFirst("sub")?.Value
                             ?? User.FindFirst("userId")?.Value;

                if (string.IsNullOrEmpty(userId))
                    return Unauthorized(new { success = false, message = "Unable to identify user." });

                _logger.LogInformation("Getting matches for CareRequest {Id} by user {UserId}", id, userId);

                var result = await _matchingService.GetMatchesForCareRequestAsync(id, userId);
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { success = false, message = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving matches for CareRequest {Id}", id);
                return StatusCode(500, new { success = false, message = "An error occurred while retrieving matches." });
            }
        }

        /// <summary>
        /// Manually trigger matching for a care request (Admin only)
        /// </summary>
        [HttpPost("{id}/match")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> TriggerMatching(string id)
        {
            try
            {
                _logger.LogInformation("Admin triggered matching for CareRequest {Id}", id);
                var result = await _matchingService.FindMatchesForCareRequestAsync(id);
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error triggering matching for CareRequest {Id}", id);
                return StatusCode(500, new { success = false, message = "An error occurred while matching." });
            }
        }

        // ── Caregiver Browse & Respond Endpoints ─────────────────────────

        /// <summary>
        /// Get paginated care requests matching the caregiver's profile (browse page).
        /// </summary>
        [HttpGet("caregiver/matched")]
        [Authorize(Roles = "Caregiver")]
        public async Task<IActionResult> GetMatchedRequestsForCaregiver(
            [FromQuery] string? serviceType,
            [FromQuery] decimal? budgetMin,
            [FromQuery] decimal? budgetMax,
            [FromQuery] string? location,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            try
            {
                var caregiverId = GetAuthenticatedUserId();
                if (caregiverId == null)
                    return Unauthorized(new { success = false, message = "Unable to identify user." });

                var result = await _responseService.GetMatchedRequestsForCaregiverAsync(
                    caregiverId, serviceType, budgetMin, budgetMax, location, page, pageSize);

                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving matched requests for caregiver");
                return StatusCode(500, new { success = false, message = "An error occurred while retrieving matched requests." });
            }
        }

        /// <summary>
        /// Get a single care request detail from caregiver's perspective (anonymized client).
        /// </summary>
        [HttpGet("{id}/caregiver-view")]
        [Authorize(Roles = "Caregiver")]
        public async Task<IActionResult> GetCaregiverView(string id)
        {
            try
            {
                var caregiverId = GetAuthenticatedUserId();
                if (caregiverId == null)
                    return Unauthorized(new { success = false, message = "Unable to identify user." });

                var result = await _responseService.GetCaregiverViewAsync(id, caregiverId);
                return Ok(new { success = true, data = result });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving caregiver view for CareRequest {Id}", id);
                return StatusCode(500, new { success = false, message = "An error occurred while retrieving the request detail." });
            }
        }

        /// <summary>
        /// Caregiver responds (shows interest) to a care request.
        /// </summary>
        [HttpPost("{id}/respond")]
        [Authorize(Roles = "Caregiver")]
        public async Task<IActionResult> RespondToCareRequest(string id, [FromBody] RespondToCareRequestDTO dto)
        {
            try
            {
                var caregiverId = GetAuthenticatedUserId();
                if (caregiverId == null)
                    return Unauthorized(new { success = false, message = "Unable to identify user." });

                var result = await _responseService.RespondToRequestAsync(id, caregiverId, dto);
                if (!result.Success)
                    return BadRequest(new { success = false, message = result.Message });

                return Ok(new { success = true, data = result });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { success = false, message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error responding to CareRequest {Id}", id);
                return StatusCode(500, new { success = false, message = "An error occurred while responding to the request." });
            }
        }

        // ── Client Detail & Management Endpoints ─────────────────────────

        /// <summary>
        /// Get full client-side request detail with responders grouped by status.
        /// </summary>
        [HttpGet("{id}/detail")]
        [Authorize(Roles = "Client, Admin")]
        public async Task<IActionResult> GetRequestDetailForClient(string id)
        {
            try
            {
                var clientId = GetAuthenticatedUserId();
                if (clientId == null)
                    return Unauthorized(new { success = false, message = "Unable to identify user." });

                var result = await _responseService.GetRequestDetailForClientAsync(id, clientId);
                return Ok(new { success = true, data = result });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { success = false, message = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving detail for CareRequest {Id}", id);
                return StatusCode(500, new { success = false, message = "An error occurred while retrieving the request detail." });
            }
        }

        /// <summary>
        /// Client shortlists a responder.
        /// </summary>
        [HttpPut("{id}/responses/{responseId}/shortlist")]
        [Authorize(Roles = "Client")]
        public async Task<IActionResult> ShortlistResponse(string id, string responseId)
        {
            try
            {
                var clientId = GetAuthenticatedUserId();
                if (clientId == null)
                    return Unauthorized(new { success = false, message = "Unable to identify user." });

                var result = await _responseService.ShortlistResponseAsync(id, responseId, clientId);
                return Ok(new { success = true, data = result });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { success = false, message = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { success = false, message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error shortlisting response {ResponseId} for CareRequest {Id}", responseId, id);
                return StatusCode(500, new { success = false, message = "An error occurred while shortlisting." });
            }
        }

        /// <summary>
        /// Client removes a responder from shortlist (back to pending).
        /// </summary>
        [HttpPut("{id}/responses/{responseId}/remove-shortlist")]
        [Authorize(Roles = "Client")]
        public async Task<IActionResult> RemoveShortlist(string id, string responseId)
        {
            try
            {
                var clientId = GetAuthenticatedUserId();
                if (clientId == null)
                    return Unauthorized(new { success = false, message = "Unable to identify user." });

                var result = await _responseService.RemoveShortlistAsync(id, responseId, clientId);
                return Ok(new { success = true, data = result });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { success = false, message = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { success = false, message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing shortlist for response {ResponseId} on CareRequest {Id}", responseId, id);
                return StatusCode(500, new { success = false, message = "An error occurred while removing shortlist." });
            }
        }

        /// <summary>
        /// Client hires a responder — generates a special gig scoped to client+caregiver.
        /// </summary>
        [HttpPost("{id}/responses/{responseId}/hire")]
        [Authorize(Roles = "Client")]
        public async Task<IActionResult> HireResponder(string id, string responseId)
        {
            try
            {
                var clientId = GetAuthenticatedUserId();
                if (clientId == null)
                    return Unauthorized(new { success = false, message = "Unable to identify user." });

                var result = await _responseService.HireResponderAsync(id, responseId, clientId);
                if (!result.Success)
                    return BadRequest(new { success = false, message = result.Message });

                return Ok(new { success = true, data = result });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { success = false, message = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { success = false, message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error hiring response {ResponseId} for CareRequest {Id}", responseId, id);
                return StatusCode(500, new { success = false, message = "An error occurred while hiring." });
            }
        }

        // ── Lifecycle Endpoints ──────────────────────────────────────────

        /// <summary>
        /// Pause a care request — hides from caregiver browse, stops matching notifications.
        /// </summary>
        [HttpPut("{id}/pause")]
        [Authorize(Roles = "Client")]
        public async Task<IActionResult> PauseCareRequest(string id)
        {
            try
            {
                var clientId = GetAuthenticatedUserId();
                if (clientId == null)
                    return Unauthorized(new { success = false, message = "Unable to identify user." });

                var result = await _careRequestService.PauseCareRequestAsync(id, clientId);
                return Ok(new CareRequestResponse { Success = true, Message = "Care request paused successfully", Data = result });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { success = false, message = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { success = false, message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error pausing CareRequest {Id}", id);
                return StatusCode(500, new { success = false, message = "An error occurred while pausing the request." });
            }
        }

        /// <summary>
        /// Reopen a paused care request — makes it visible again.
        /// </summary>
        [HttpPut("{id}/reopen")]
        [Authorize(Roles = "Client")]
        public async Task<IActionResult> ReopenCareRequest(string id)
        {
            try
            {
                var clientId = GetAuthenticatedUserId();
                if (clientId == null)
                    return Unauthorized(new { success = false, message = "Unable to identify user." });

                var result = await _careRequestService.ReopenCareRequestAsync(id, clientId);
                return Ok(new CareRequestResponse { Success = true, Message = "Care request reopened successfully", Data = result });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { success = false, message = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { success = false, message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reopening CareRequest {Id}", id);
                return StatusCode(500, new { success = false, message = "An error occurred while reopening the request." });
            }
        }

        /// <summary>
        /// Close a care request — fulfilled/done, notifies all pending responders.
        /// </summary>
        [HttpPut("{id}/close")]
        [Authorize(Roles = "Client")]
        public async Task<IActionResult> CloseCareRequest(string id)
        {
            try
            {
                var clientId = GetAuthenticatedUserId();
                if (clientId == null)
                    return Unauthorized(new { success = false, message = "Unable to identify user." });

                var result = await _careRequestService.CloseCareRequestAsync(id, clientId);
                return Ok(new CareRequestResponse { Success = true, Message = "Care request closed successfully", Data = result });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { success = false, message = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { success = false, message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error closing CareRequest {Id}", id);
                return StatusCode(500, new { success = false, message = "An error occurred while closing the request." });
            }
        }

        /// <summary>
        /// Soft-delete a care request (sets DeletedAt). Only allowed if no active hires.
        /// </summary>
        [HttpDelete("{id}")]
        [Authorize(Roles = "Client")]
        public async Task<IActionResult> SoftDeleteCareRequest(string id)
        {
            try
            {
                var clientId = GetAuthenticatedUserId();
                if (clientId == null)
                    return Unauthorized(new { success = false, message = "Unable to identify user." });

                await _careRequestService.SoftDeleteCareRequestAsync(id, clientId);
                return Ok(new { success = true, message = "Care request deleted successfully" });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { success = false, message = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { success = false, message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting CareRequest {Id}", id);
                return StatusCode(500, new { success = false, message = "An error occurred while deleting the request." });
            }
        }

        // ── Helper ───────────────────────────────────────────────────────

        private string? GetAuthenticatedUserId()
        {
            return User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                   ?? User.FindFirst("sub")?.Value
                   ?? User.FindFirst("userId")?.Value;
        }
    }

    /// <summary>
    /// Request model for updating status
    /// </summary>
    public class UpdateStatusRequest
    {
        public string Status { get; set; } = string.Empty;
    }
}
