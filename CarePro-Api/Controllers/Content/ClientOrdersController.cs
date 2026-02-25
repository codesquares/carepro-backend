using Application.DTOs;
using Application.Interfaces;
using Application.Interfaces.Content;
using Infrastructure.Content.Data;
using Infrastructure.Content.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace CarePro_Api.Controllers.Content
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class ClientOrdersController : ControllerBase
    {
        private readonly IClientOrderService clientOrderService;
        private readonly ILogger<ClientOrdersController> logger;

        public ClientOrdersController(IClientOrderService clientOrderService, ILogger<ClientOrdersController> logger)
        {
            this.clientOrderService = clientOrderService;
            this.logger = logger;
        }

        // ── Security: IDOR helper ──
        private string? GetCurrentUserId() =>
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirst("sub")?.Value
            ?? User.FindFirst("userId")?.Value;

        private bool IsAdminOrSuperAdmin()
        {
            var role = User.FindFirstValue(ClaimTypes.Role);
            return role == "Admin" || role == "SuperAdmin";
        }

        private bool IsAuthorizedForUser(string userId)
        {
            if (IsAdminOrSuperAdmin()) return true;
            return GetCurrentUserId() == userId;
        }

        /// <summary>
        /// DEPRECATED: Direct order creation is not allowed.
        /// Orders can only be created through the secure payment pipeline (/api/payments/initiate → webhook → order).
        /// This endpoint is preserved only for internal/admin use.
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "Admin, SuperAdmin")]
        public async Task<IActionResult> AddClientOrderAsync([FromBody] AddClientOrderRequest addClientOrderRequest)
        {
            var result = await clientOrderService.CreateClientOrderAsync(addClientOrderRequest);

            if (!result.IsSuccess)
            {
                return BadRequest(new { Errors = result.Errors });
            }

            return Ok(result.Value);
        }



        [HttpGet]
        [Route("clientUserId")]
        [Authorize(Roles = "Client, Admin, SuperAdmin")]
        public async Task<IActionResult> GetAllClientOrdersAsync(string clientUserId)
        {
            if (!IsAuthorizedForUser(clientUserId))
                return Forbid();

            try
            {
                var clientOrders = await clientOrderService.GetAllClientOrderAsync(clientUserId);
                return Ok(clientOrders);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An unexpected error occurred");
                return StatusCode(500, new { ErrorMessage = "An error occurred on the server." });
            }
        }



        [HttpGet]
        [Route("CaregiverOrders/caregiverId")]
        [Authorize(Roles = "Caregiver, Admin, SuperAdmin")]
        public async Task<IActionResult> GetCaregiverOrdersAsync(string caregiverId)
        {
            if (!IsAuthorizedForUser(caregiverId))
                return Forbid();

            try
            {
                var clientOrders = await clientOrderService.GetCaregiverOrdersAsync(caregiverId);
                return Ok(clientOrders);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An unexpected error occurred");
                return StatusCode(500, new { ErrorMessage = "An error occurred on the server." });
            }
        }



        [HttpGet]
        [Route("gigId")]
        [Authorize(Roles = "Caregiver, Client, Admin, SuperAdmin")]
        public async Task<IActionResult> GetAllClientOrdersByGigIdAsync(string gigId)
        {
            // Gig-based lookup is scoped by the gig — no direct user ID to check.
            // The service returns order records tied to the gig; acceptable for participants.
            try
            {
                var clientOrders = await clientOrderService.GetAllClientOrdersByGigIdAsync(gigId);
                return Ok(clientOrders);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An unexpected error occurred");
                return StatusCode(500, new { ErrorMessage = "An error occurred on the server." });
            }
        }



        [HttpGet]
        [Route("caregiverId")]
        [Authorize(Roles = "Caregiver, Admin, SuperAdmin")]
        public async Task<IActionResult> GetAllCaregiverOrdersAsync(string caregiverId)
        {
            if (!IsAuthorizedForUser(caregiverId))
                return Forbid();

            try
            {
                var caregiverOrders = await clientOrderService.GetAllCaregiverOrderAsync(caregiverId);
                return Ok(caregiverOrders);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An unexpected error occurred");
                return StatusCode(500, new { ErrorMessage = "An error occurred on the server." });
            }
        }


        [HttpGet]
        [Route("orderId")]
        [Authorize(Roles = "Caregiver, Client, Admin, SuperAdmin")]
        public async Task<IActionResult> GetOrderAsync(string orderId)
        {
            try
            {
                var clientOrder = await clientOrderService.GetClientOrderAsync(orderId);

                // IDOR: verify caller is either the client or the caregiver on this order
                if (!IsAdminOrSuperAdmin())
                {
                    var currentUserId = GetCurrentUserId();
                    if (currentUserId != clientOrder.ClientId && currentUserId != clientOrder.CaregiverId)
                        return Forbid();
                }

                return Ok(clientOrder);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An unexpected error occurred");
                return StatusCode(500, new { ErrorMessage = "An error occurred on the server." });
            }
        }


        /// <summary>
        /// Gets ALL orders (admin-only for dashboards/reporting).
        /// </summary>
        [HttpGet]
        [Route("all")]
        [Authorize(Roles = "Admin, SuperAdmin")]
        public async Task<IActionResult> GetAllOrdersAsync()
        {
            try
            {
                var orders = await clientOrderService.GetAllOrdersAsync();
                return Ok(orders);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An unexpected error occurred");
                return StatusCode(500, new { ErrorMessage = "An error occurred on the server." });
            }
        }


        [HttpPut]
        [Route("UpdateClientOrderStatus/orderId")]
        [Authorize(Roles = "Caregiver, Admin, SuperAdmin")]
        public async Task<ActionResult<string>> UpdateClientOrderStatusAsync(string orderId, UpdateClientOrderStatusRequest updateClientOrderStatusRequest)
        {
            try
            {
                // Override the UserId from the body with the JWT-authenticated identity
                updateClientOrderStatusRequest.UserId = GetCurrentUserId();

                var result = await clientOrderService.UpdateClientOrderStatusAsync(orderId, updateClientOrderStatusRequest);
                logger.LogInformation("Client Order Status with ID: {OrderId} updated by user {UserId}.", orderId, updateClientOrderStatusRequest.UserId);
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error updating order status for {OrderId}", orderId);
                return StatusCode(500, new { message = "An error occurred on the server." });
            }
        }



        [HttpPut]
        [Route("ClientApproveOrderStatus/orderId")]
        [Authorize(Roles = "Client, Admin, SuperAdmin")]
        public async Task<ActionResult<string>> UpdateOrderStatusToApproveApproveAsync(string orderId)
        {
            try
            {
                var result = await clientOrderService.UpdateOrderStatusToApproveAsync(orderId);
                logger.LogInformation("Client Order Status with ID: {OrderId} approved by user {UserId}.", orderId, GetCurrentUserId());
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error approving order {OrderId}", orderId);
                return StatusCode(500, new { message = "An error occurred on the server." });
            }
        }


        [HttpPut]
        [Route("UpdateClientOrderStatusHasDispute/orderId")]
        [Authorize(Roles = "Client, Admin, SuperAdmin")]
        public async Task<ActionResult<string>> UpdateClientOrderStatusHasDisputeAsync(string orderId, UpdateClientOrderStatusHasDisputeRequest updateClientOrderStatusHasDisputeRequest)
        {
            try
            {
                // Override UserId from body with JWT identity
                updateClientOrderStatusHasDisputeRequest.UserId = GetCurrentUserId();

                var result = await clientOrderService.UpdateClientOrderStatusHasDisputeAsync(orderId, updateClientOrderStatusHasDisputeRequest);
                logger.LogInformation("Dispute raised on Order {OrderId} by user {UserId}.", orderId, updateClientOrderStatusHasDisputeRequest.UserId);
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error raising dispute on order {OrderId}", orderId);
                return StatusCode(500, new { message = "An error occurred on the server." });
            }
        }
    }
}
