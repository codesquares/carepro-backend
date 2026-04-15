using Microsoft.AspNetCore.Mvc;
using Application.Interfaces.Content;
using Application.DTOs;
using Microsoft.AspNetCore.Authorization;

namespace CarePro_Api.Controllers.Content
{
    [ApiController]
    [Route("api/negotiations")]
    [Authorize]
    public class NegotiationController : ControllerBase
    {
        private readonly IOrderNegotiationService _negotiationService;
        private readonly ILogger<NegotiationController> _logger;

        public NegotiationController(IOrderNegotiationService negotiationService, ILogger<NegotiationController> logger)
        {
            _negotiationService = negotiationService;
            _logger = logger;
        }

        /// <summary>
        /// Start a negotiation session. Either party can call this.
        /// </summary>
        [HttpPost]
        public async Task<ActionResult<OrderNegotiationDTO>> CreateNegotiation([FromBody] CreateNegotiationDTO request)
        {
            try
            {
                var userId = GetUserIdFromToken();
                if (string.IsNullOrEmpty(userId))
                    return Unauthorized("Authorization required.");

                var result = await _negotiationService.CreateNegotiationAsync(userId, request);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating negotiation for order {OrderId}", request.OrderId);
                return StatusCode(500, "Failed to create negotiation.");
            }
        }

        /// <summary>
        /// Get the active negotiation for an order.
        /// </summary>
        [HttpGet("by-order/{orderId}")]
        public async Task<ActionResult<OrderNegotiationDTO>> GetByOrder(string orderId)
        {
            try
            {
                var result = await _negotiationService.GetNegotiationByOrderIdAsync(orderId);
                if (result == null)
                    return NotFound($"No active negotiation found for order {orderId}.");
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting negotiation for order {OrderId}", orderId);
                return StatusCode(500, "Error retrieving negotiation.");
            }
        }

        /// <summary>
        /// Get a negotiation by its ID.
        /// </summary>
        [HttpGet("{id}")]
        public async Task<ActionResult<OrderNegotiationDTO>> GetById(string id)
        {
            try
            {
                var result = await _negotiationService.GetNegotiationByIdAsync(id);
                if (result == null)
                    return NotFound($"Negotiation {id} not found.");
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting negotiation {NegotiationId}", id);
                return StatusCode(500, "Error retrieving negotiation.");
            }
        }

        /// <summary>
        /// Client updates their input and optionally submits for caregiver review.
        /// Resets caregiverAgreed to false.
        /// </summary>
        [HttpPut("{id}/client-update")]
        public async Task<ActionResult<OrderNegotiationDTO>> ClientUpdate(string id, [FromBody] ClientNegotiationUpdateDTO update)
        {
            try
            {
                var userId = GetUserIdFromToken();
                if (string.IsNullOrEmpty(userId))
                    return Unauthorized("Authorization required.");

                var result = await _negotiationService.ClientUpdateAsync(id, userId, update);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in client update for negotiation {NegotiationId}", id);
                return StatusCode(500, "Failed to update negotiation.");
            }
        }

        /// <summary>
        /// Caregiver updates their input and optionally submits for client review.
        /// Resets clientAgreed to false.
        /// </summary>
        [HttpPut("{id}/caregiver-update")]
        public async Task<ActionResult<OrderNegotiationDTO>> CaregiverUpdate(string id, [FromBody] CaregiverNegotiationUpdateDTO update)
        {
            try
            {
                var userId = GetUserIdFromToken();
                if (string.IsNullOrEmpty(userId))
                    return Unauthorized("Authorization required.");

                var result = await _negotiationService.CaregiverUpdateAsync(id, userId, update);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in caregiver update for negotiation {NegotiationId}", id);
                return StatusCode(500, "Failed to update negotiation.");
            }
        }

        /// <summary>
        /// Client marks they agree with the current state.
        /// If caregiver has also agreed, status becomes BothAgreed.
        /// </summary>
        [HttpPut("{id}/client-agree")]
        public async Task<ActionResult<OrderNegotiationDTO>> ClientAgree(string id, [FromBody] NegotiationAgreeDTO dto)
        {
            try
            {
                if (!dto.ConfirmAgreed)
                    return BadRequest("confirmAgreed must be true.");

                var userId = GetUserIdFromToken();
                if (string.IsNullOrEmpty(userId))
                    return Unauthorized("Authorization required.");

                var result = await _negotiationService.ClientAgreeAsync(id, userId);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in client agree for negotiation {NegotiationId}", id);
                return StatusCode(500, "Failed to process agreement.");
            }
        }

        /// <summary>
        /// Caregiver marks they agree. If client has also agreed, status becomes BothAgreed.
        /// </summary>
        [HttpPut("{id}/caregiver-agree")]
        public async Task<ActionResult<OrderNegotiationDTO>> CaregiverAgree(string id, [FromBody] NegotiationAgreeDTO dto)
        {
            try
            {
                if (!dto.ConfirmAgreed)
                    return BadRequest("confirmAgreed must be true.");

                var userId = GetUserIdFromToken();
                if (string.IsNullOrEmpty(userId))
                    return Unauthorized("Authorization required.");

                var result = await _negotiationService.CaregiverAgreeAsync(id, userId);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in caregiver agree for negotiation {NegotiationId}", id);
                return StatusCode(500, "Failed to process agreement.");
            }
        }

        /// <summary>
        /// Either party cancels the negotiation before agreement.
        /// </summary>
        [HttpPut("{id}/abandon")]
        public async Task<ActionResult<OrderNegotiationDTO>> Abandon(string id, [FromBody] NegotiationAbandonDTO dto)
        {
            try
            {
                var userId = GetUserIdFromToken();
                if (string.IsNullOrEmpty(userId))
                    return Unauthorized("Authorization required.");

                var result = await _negotiationService.AbandonAsync(id, userId, dto);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error abandoning negotiation {NegotiationId}", id);
                return StatusCode(500, "Failed to abandon negotiation.");
            }
        }

        /// <summary>
        /// Convert a BothAgreed negotiation into a formal contract.
        /// Calls LLM to generate the contract document.
        /// </summary>
        [HttpPost("{id}/convert-to-contract")]
        public async Task<ActionResult<OrderNegotiationDTO>> ConvertToContract(string id)
        {
            try
            {
                var userId = GetUserIdFromToken();
                if (string.IsNullOrEmpty(userId))
                    return Unauthorized("Authorization required.");

                var result = await _negotiationService.ConvertToContractAsync(id, userId);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error converting negotiation {NegotiationId} to contract", id);
                return StatusCode(500, "Failed to convert negotiation to contract.");
            }
        }

        private string? GetUserIdFromToken()
        {
            var userIdClaim = User.FindFirst("userId") ?? User.FindFirst("sub") ?? User.FindFirst("id");
            return userIdClaim?.Value;
        }
    }
}
