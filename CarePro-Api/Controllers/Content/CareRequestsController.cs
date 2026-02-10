using Application.DTOs;
using Application.Interfaces.Content;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CarePro_Api.Controllers.Content
{
    [Route("api/[controller]")]
    [ApiController]
    public class CareRequestsController : ControllerBase
    {
        private readonly ICareRequestService _careRequestService;
        private readonly ILogger<CareRequestsController> _logger;

        public CareRequestsController(ICareRequestService careRequestService, ILogger<CareRequestsController> logger)
        {
            _careRequestService = careRequestService;
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
                _logger.LogInformation($"Retrieving care requests for ClientId: {clientId}");

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
    }

    /// <summary>
    /// Request model for updating status
    /// </summary>
    public class UpdateStatusRequest
    {
        public string Status { get; set; } = string.Empty;
    }
}
