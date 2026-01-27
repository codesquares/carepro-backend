using Application.DTOs;
using Application.Interfaces.Content;
using Infrastructure.Content.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CarePro_Api.Controllers.Content
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "Client,Admin")]
    public class ClientRecommendationsController : ControllerBase
    {
        private readonly IClientRecommendationService clientRecommendationService;
        private readonly IClientService clientService;
        private readonly ILogger<ClientRecommendationsController> logger;

        public ClientRecommendationsController(IClientRecommendationService clientRecommendationService, IClientService clientService, ILogger<ClientRecommendationsController> logger)
        {
            this.clientRecommendationService = clientRecommendationService;
            this.clientService = clientService;
            this.logger = logger;
        }

        /// <summary>
        /// Get client recommendations by clientId
        /// </summary>
        [HttpGet("client/{clientId}")]
        // [Authorize(Roles = "Client, Admin")]
        public async Task<IActionResult> GetClientRecommendationAsync(string clientId)
        {
            try
            {
                logger.LogInformation($"Retrieving recommendation for client with ID '{clientId}'.");

                var clientRecommendation = await clientRecommendationService.GetClientRecommendationAsync(clientId);

                return Ok(clientRecommendation);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new ClientRecommendationErrorResponse
                {
                    Success = false,
                    Error = "Client not found",
                    Message = ex.Message
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An unexpected error occurred");
                return StatusCode(500, new ClientRecommendationErrorResponse
                {
                    Success = false,
                    Error = "Server error",
                    Message = "Failed to retrieve recommendations due to server error"
                });
            }
        }

        /// <summary>
        /// Create new recommendations for a client
        /// </summary>
        [HttpPost("{clientId}")]
        // [Authorize(Roles = "Client, Admin")]
        public async Task<IActionResult> CreateClientRecommendationAsync(string clientId, [FromBody] CreateClientRecommendationRequest request)
        {
            try
            {
                // Validate request
                if (!ModelState.IsValid)
                {
                    var errors = ModelState.Values
                        .SelectMany(v => v.Errors)
                        .Select(e => e.ErrorMessage)
                        .ToList();

                    return BadRequest(new ClientRecommendationErrorResponse
                    {
                        Success = false,
                        Error = "Invalid request data",
                        Message = "Detailed validation error message",
                        Details = errors
                    });
                }

                logger.LogInformation($"Creating recommendations for client with ID '{clientId}'.");

                var response = await clientRecommendationService.CreateClientRecommendationAsync(clientId, request);

                return StatusCode(201, response);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new ClientRecommendationErrorResponse
                {
                    Success = false,
                    Error = "Client not found",
                    Message = ex.Message
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new ClientRecommendationErrorResponse
                {
                    Success = false,
                    Error = "Invalid request data",
                    Message = ex.Message
                });
            }
            catch (ApplicationException ex)
            {
                return StatusCode(500, new ClientRecommendationErrorResponse
                {
                    Success = false,
                    Error = "Server error",
                    Message = ex.Message
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An unexpected error occurred");
                return StatusCode(500, new ClientRecommendationErrorResponse
                {
                    Success = false,
                    Error = "Server error",
                    Message = "Failed to save recommendations due to server error"
                });
            }
        }

        /// <summary>
        /// Update existing recommendations for a client
        /// </summary>
        [HttpPut("client/{clientId}")]
        // [Authorize(Roles = "Client, Admin")]
        public async Task<IActionResult> UpdateClientRecommendationAsync(string clientId, [FromBody] UpdateClientRecommendationRequest request)
        {
            try
            {
                // Validate request
                if (!ModelState.IsValid)
                {
                    var errors = ModelState.Values
                        .SelectMany(v => v.Errors)
                        .Select(e => e.ErrorMessage)
                        .ToList();

                    return BadRequest(new ClientRecommendationErrorResponse
                    {
                        Success = false,
                        Error = "Invalid request data",
                        Message = "Detailed validation error message",
                        Details = errors
                    });
                }

                logger.LogInformation($"Updating recommendations for client with ID '{clientId}'.");

                var response = await clientRecommendationService.UpdateClientRecommendationAsync(clientId, request);

                return Ok(response);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new ClientRecommendationErrorResponse
                {
                    Success = false,
                    Error = "Recommendation not found",
                    Message = ex.Message
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new ClientRecommendationErrorResponse
                {
                    Success = false,
                    Error = "Invalid request data",
                    Message = ex.Message
                });
            }
            catch (ApplicationException ex)
            {
                return StatusCode(500, new ClientRecommendationErrorResponse
                {
                    Success = false,
                    Error = "Server error",
                    Message = ex.Message
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An unexpected error occurred");
                return StatusCode(500, new ClientRecommendationErrorResponse
                {
                    Success = false,
                    Error = "Server error",
                    Message = "Failed to update recommendations due to server error"
                });
            }
        }

        /// <summary>
        /// Delete a recommendation by ID (soft delete)
        /// </summary>
        [HttpDelete("{recommendationId}")]
        // [Authorize(Roles = "Client, Admin")]
        public async Task<IActionResult> DeleteClientRecommendationAsync(string recommendationId)
        {
            try
            {
                logger.LogInformation($"Deleting recommendation with ID '{recommendationId}'.");

                var result = await clientRecommendationService.DeleteClientRecommendationAsync(recommendationId);

                return Ok(new
                {
                    Success = true,
                    Message = "Recommendation deleted successfully"
                });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new ClientRecommendationErrorResponse
                {
                    Success = false,
                    Error = "Recommendation not found",
                    Message = ex.Message
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new ClientRecommendationErrorResponse
                {
                    Success = false,
                    Error = "Invalid request data",
                    Message = ex.Message
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An unexpected error occurred");
                return StatusCode(500, new ClientRecommendationErrorResponse
                {
                    Success = false,
                    Error = "Server error",
                    Message = "Failed to delete recommendation due to server error"
                });
            }
        }
    }
}
