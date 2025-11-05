using Application.DTOs;
using Application.Interfaces.Content;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CarePro_Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class OrderTasksController : ControllerBase
    {
        private readonly IOrderTasksService _orderTasksService;
        private readonly ILogger<OrderTasksController> _logger;

        public OrderTasksController(IOrderTasksService orderTasksService, ILogger<OrderTasksController> logger)
        {
            _orderTasksService = orderTasksService;
            _logger = logger;
        }

        /// <summary>
        /// Create new order tasks definition (draft state)
        /// </summary>
        [HttpPost]
        public async Task<ActionResult<OrderTasksResponseDTO>> CreateOrderTasks([FromBody] CreateOrderTasksRequestDTO request)
        {
            try
            {
                // Validate client authorization
                var clientId = GetClientIdFromToken();
                if (string.IsNullOrEmpty(clientId))
                    return Unauthorized("Client authorization required");

                // Ensure the request is for the authenticated client
                if (request.ClientId != clientId)
                    return Forbid("Cannot create order tasks for another client");

                var result = await _orderTasksService.CreateOrderTasksAsync(request);

                _logger.LogInformation("OrderTasks created: {OrderTasksId} for Client: {ClientId}",
                    result.Id, clientId);

                return CreatedAtAction(nameof(GetOrderTasks), new { id = result.Id }, result);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning("Invalid request for CreateOrderTasks: {Message}", ex.Message);
                return BadRequest(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning("Invalid operation for CreateOrderTasks: {Message}", ex.Message);
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating order tasks");
                return StatusCode(500, "An error occurred while creating order tasks");
            }
        }

        /// <summary>
        /// Update existing order tasks (only in draft state)
        /// </summary>
        [HttpPut("{id}")]
        public async Task<ActionResult<OrderTasksResponseDTO>> UpdateOrderTasks(string id, [FromBody] UpdateOrderTasksRequestDTO request)
        {
            try
            {
                // Validate client authorization
                var clientId = GetClientIdFromToken();
                if (string.IsNullOrEmpty(clientId))
                    return Unauthorized("Client authorization required");

                // Set the ID from route
                request.OrderTasksId = id;

                // Validate ownership by checking existing order tasks
                var existingOrderTasks = await _orderTasksService.GetOrderTasksByIdAsync(id);
                if (existingOrderTasks.ClientId != clientId)
                    return Forbid("Cannot update order tasks for another client");

                var result = await _orderTasksService.UpdateOrderTasksAsync(request);

                _logger.LogInformation("OrderTasks updated: {OrderTasksId}", id);

                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning("Invalid request for UpdateOrderTasks: {Message}", ex.Message);
                return BadRequest(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning("Invalid operation for UpdateOrderTasks: {Message}", ex.Message);
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating order tasks {OrderTasksId}", id);
                return StatusCode(500, "An error occurred while updating order tasks");
            }
        }

        /// <summary>
        /// Get order tasks by ID
        /// </summary>
        [HttpGet("{id}")]
        public async Task<ActionResult<OrderTasksResponseDTO>> GetOrderTasks(string id)
        {
            try
            {
                var orderTasks = await _orderTasksService.GetOrderTasksByIdAsync(id);

                // Validate authorization
                var clientId = GetClientIdFromToken();
                var caregiverId = GetCaregiverIdFromToken();

                // Allow access if user is the client or the assigned caregiver
                if (!string.IsNullOrEmpty(clientId) && orderTasks.ClientId == clientId)
                {
                    return Ok(orderTasks);
                }
                else if (!string.IsNullOrEmpty(caregiverId) && orderTasks.CaregiverId == caregiverId)
                {
                    return Ok(orderTasks);
                }
                else
                {
                    return Forbid("Access denied");
                }
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning("OrderTasks not found: {OrderTasksId}", id);
                return NotFound(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving order tasks {OrderTasksId}", id);
                return StatusCode(500, "An error occurred while retrieving order tasks");
            }
        }

        /// <summary>
        /// Get all order tasks for the authenticated client
        /// </summary>
        [HttpGet("my-orders")]
        public async Task<ActionResult<List<OrderTasksResponseDTO>>> GetMyOrderTasks()
        {
            try
            {
                var clientId = GetClientIdFromToken();
                if (string.IsNullOrEmpty(clientId))
                    return Unauthorized("Client authorization required");

                var orderTasksList = await _orderTasksService.GetOrderTasksByClientIdAsync(clientId);

                _logger.LogInformation("Retrieved {Count} order tasks for Client: {ClientId}",
                    orderTasksList.Count, clientId);

                return Ok(orderTasksList);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving order tasks for client");
                return StatusCode(500, "An error occurred while retrieving order tasks");
            }
        }

        /// <summary>
        /// Delete order tasks (only in draft state)
        /// </summary>
        [HttpDelete("{id}")]
        public async Task<ActionResult> DeleteOrderTasks(string id)
        {
            try
            {
                // Validate client authorization
                var clientId = GetClientIdFromToken();
                if (string.IsNullOrEmpty(clientId))
                    return Unauthorized("Client authorization required");

                // Validate ownership
                var existingOrderTasks = await _orderTasksService.GetOrderTasksByIdAsync(id);
                if (existingOrderTasks.ClientId != clientId)
                    return Forbid("Cannot delete order tasks for another client");

                var deleted = await _orderTasksService.DeleteOrderTasksAsync(id);

                if (!deleted)
                    return NotFound("Order tasks not found");

                _logger.LogInformation("OrderTasks deleted: {OrderTasksId}", id);
                return NoContent();
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning("Invalid operation for DeleteOrderTasks: {Message}", ex.Message);
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting order tasks {OrderTasksId}", id);
                return StatusCode(500, "An error occurred while deleting order tasks");
            }
        }

        /// <summary>
        /// Calculate pricing for order tasks
        /// </summary>
        [HttpGet("{id}/pricing")]
        public async Task<ActionResult<OrderTasksPricingDTO>> CalculatePricing(string id)
        {
            try
            {
                var pricing = await _orderTasksService.CalculatePricingAsync(id);
                return Ok(pricing);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning("OrderTasks not found for pricing: {OrderTasksId}", id);
                return NotFound(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating pricing for order tasks {OrderTasksId}", id);
                return StatusCode(500, "An error occurred while calculating pricing");
            }
        }

        /// <summary>
        /// Estimate pricing for a potential order (before creating)
        /// </summary>
        [HttpPost("estimate-pricing")]
        public async Task<ActionResult<OrderTasksPricingDTO>> EstimatePricing([FromBody] CreateOrderTasksRequestDTO request)
        {
            try
            {
                // Validate client authorization
                var clientId = GetClientIdFromToken();
                if (string.IsNullOrEmpty(clientId))
                    return Unauthorized("Client authorization required");

                // Ensure the request is for the authenticated client
                if (request.ClientId != clientId)
                    return Forbid("Cannot estimate pricing for another client");

                var pricing = await _orderTasksService.EstimatePricingAsync(request);
                return Ok(pricing);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning("Invalid request for EstimatePricing: {Message}", ex.Message);
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error estimating pricing");
                return StatusCode(500, "An error occurred while estimating pricing");
            }
        }

        /// <summary>
        /// Mark order tasks as ready for payment
        /// </summary>
        [HttpPost("{id}/prepare-payment")]
        public async Task<ActionResult> PrepareForPayment(string id)
        {
            try
            {
                // Validate client authorization
                var clientId = GetClientIdFromToken();
                if (string.IsNullOrEmpty(clientId))
                    return Unauthorized("Client authorization required");

                // Validate ownership
                var existingOrderTasks = await _orderTasksService.GetOrderTasksByIdAsync(id);
                if (existingOrderTasks.ClientId != clientId)
                    return Forbid("Cannot prepare payment for another client's order tasks");

                var updated = await _orderTasksService.MarkAsPendingPaymentAsync(id);

                if (!updated)
                    return NotFound("Order tasks not found");

                _logger.LogInformation("OrderTasks prepared for payment: {OrderTasksId}", id);
                return Ok(new { message = "Order tasks prepared for payment", orderTasksId = id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error preparing order tasks for payment {OrderTasksId}", id);
                return StatusCode(500, "An error occurred while preparing for payment");
            }
        }

        /// <summary>
        /// Get order tasks by client order ID (for internal linking)
        /// </summary>
        [HttpGet("by-order/{clientOrderId}")]
        public async Task<ActionResult<OrderTasksResponseDTO>> GetOrderTasksByClientOrderId(string clientOrderId)
        {
            try
            {
                var orderTasks = await _orderTasksService.GetOrderTasksByClientOrderIdAsync(clientOrderId);

                if (orderTasks == null)
                    return NotFound("Order tasks not found for the specified client order");

                // Validate authorization
                var clientId = GetClientIdFromToken();
                var caregiverId = GetCaregiverIdFromToken();

                // Allow access if user is the client or the assigned caregiver
                if (!string.IsNullOrEmpty(clientId) && orderTasks.ClientId == clientId)
                {
                    return Ok(orderTasks);
                }
                else if (!string.IsNullOrEmpty(caregiverId) && orderTasks.CaregiverId == caregiverId)
                {
                    return Ok(orderTasks);
                }
                else
                {
                    return Forbid("Access denied");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving order tasks by client order ID {ClientOrderId}", clientOrderId);
                return StatusCode(500, "An error occurred while retrieving order tasks");
            }
        }

        // Helper methods for extracting user identity from JWT tokens
        private string? GetClientIdFromToken()
        {
            return User.FindFirst("ClientId")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        }

        private string? GetCaregiverIdFromToken()
        {
            return User.FindFirst("CareGiverId")?.Value;
        }

        private string? GetUserRole()
        {
            return User.FindFirst(ClaimTypes.Role)?.Value;
        }
    }
}