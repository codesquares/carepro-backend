using Application.DTOs;
using Application.Interfaces;
using Application.Interfaces.Content;
using Infrastructure.Content.Data;
using Infrastructure.Content.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CarePro_Api.Controllers.Content
{
    [Route("api/[controller]")]
    [ApiController]
    public class ClientOrdersController : ControllerBase
    {
        private readonly CareProDbContext careProDbContext;
        private readonly IClientOrderService clientOrderService;
        private readonly ILogger<ClientOrdersController> logger;

        public ClientOrdersController(CareProDbContext careProDbContext, IClientOrderService clientOrderService, ILogger<ClientOrdersController> logger)
        {
            this.careProDbContext = careProDbContext;
            this.clientOrderService = clientOrderService;
            this.logger = logger;
        }

      
        /// ENDPOINT TO CREATE  ClientOrder Services TO THE DATABASE
        [HttpPost]
        // [Authorize(Roles = "Client")]
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
        // [Authorize(Roles = "Caregiver, Client, Admin")]
        public async Task<IActionResult> GetAllClientOrdersAsync(string clientUserId)
        {           

            try
            {
                logger.LogInformation($"Retrieving all Orders for Client available");

                var clientOrders = await clientOrderService.GetAllClientOrderAsync(clientUserId);

                return Ok(clientOrders);

            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ApplicationException appEx)
            {
                // Handle application-specific exceptions
                return BadRequest(new { ErrorMessage = appEx.Message });
            }
            catch (HttpRequestException httpEx)
            {
                // Handle HTTP request-related exceptions
                return StatusCode(500, new { ErrorMessage = httpEx.Message });
            }
            catch (Exception ex)
            {
                // Handle other exceptions
                return StatusCode(500, new { ex /*ErrorMessage = "An error occurred on the server."*/ });
            }

        }



        [HttpGet]
        [Route("CaregiverOrders/caregiverId")]
        // [Authorize(Roles = "Caregiver, Client, Admin")]
        public async Task<IActionResult> GetCaregiverOrdersAsync(string caregiverId)
        {

            try
            {
                logger.LogInformation($"Retrieving all Orders for Client available");

                var clientOrders = await clientOrderService.GetCaregiverOrdersAsync(caregiverId);

                return Ok(clientOrders);

            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ApplicationException appEx)
            {
                // Handle application-specific exceptions
                return BadRequest(new { ErrorMessage = appEx.Message });
            }
            catch (HttpRequestException httpEx)
            {
                // Handle HTTP request-related exceptions
                return StatusCode(500, new { ErrorMessage = httpEx.Message });
            }
            catch (Exception ex)
            {
                // Handle other exceptions
                return StatusCode(500, new { ex /*ErrorMessage = "An error occurred on the server."*/ });
            }

        }



        [HttpGet]
        [Route("gigId")]
        // [Authorize(Roles = "Caregiver, Client, Admin")]
        public async Task<IActionResult> GetAllClientOrdersByGigIdAsync(string gigId)
        {

            try
            {
                logger.LogInformation($"Retrieving all Orders for Client available");

                var clientOrders = await clientOrderService.GetAllClientOrdersByGigIdAsync(gigId);

                return Ok(clientOrders);

            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ApplicationException appEx)
            {
                // Handle application-specific exceptions
                return BadRequest(new { ErrorMessage = appEx.Message });
            }
            catch (HttpRequestException httpEx)
            {
                // Handle HTTP request-related exceptions
                return StatusCode(500, new { ErrorMessage = httpEx.Message });
            }
            catch (Exception ex)
            {
                // Handle other exceptions
                return StatusCode(500, new { ex /*ErrorMessage = "An error occurred on the server."*/ });
            }

        }



        [HttpGet]
        [Route("caregiverId")]
        // [Authorize(Roles = "Caregiver, Client, Admin")]
        public async Task<IActionResult> GetAllCaregiverOrdersAsync(string caregiverId)
        {

            try
            {
                logger.LogInformation($"Retrieving all Orders for Client available");

                var caregiverOrders = await clientOrderService.GetAllCaregiverOrderAsync(caregiverId);

                return Ok(caregiverOrders);

            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }            
            catch (ApplicationException appEx)
            {
                // Handle application-specific exceptions
                return BadRequest(new { ErrorMessage = appEx.Message });
            }
            catch (HttpRequestException httpEx)
            {
                // Handle HTTP request-related exceptions
                return StatusCode(500, new { ErrorMessage = httpEx.Message });
            }
            catch (Exception ex)
            {
                // Handle other exceptions
                return StatusCode(500, new { ex /*ErrorMessage = "An error occurred on the server."*/ });
            }

        }


        [HttpGet]
        [Route("orderId")]
        // [Authorize(Roles = "Caregiver, Client, Admin")]
        public async Task<IActionResult> GetOrderAsync(string orderId)
        {

            try
            {
                logger.LogInformation($"Retrieving all Orders for Client available");

                var clientOrder = await clientOrderService.GetClientOrderAsync(orderId);

                return Ok(clientOrder);

            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ApplicationException appEx)
            {
                // Handle application-specific exceptions
                return BadRequest(new { ErrorMessage = appEx.Message });
            }
            catch (HttpRequestException httpEx)
            {
                // Handle HTTP request-related exceptions
                return StatusCode(500, new { ErrorMessage = httpEx.Message });
            }
            catch (Exception ex)
            {
                // Handle other exceptions
                return StatusCode(500, new { ex /*ErrorMessage = "An error occurred on the server."*/ });
            }

        }



        [HttpPut]
        [Route("UpdateClientOrderStatus/orderId")]
        // [Authorize(Roles = "Caregiver, Admin")]
        public async Task<ActionResult<string>> UpdateClientOrderStatusAsync(string orderId, UpdateClientOrderStatusRequest updateClientOrderStatusRequest )
        {
            try
            {
                var result = await clientOrderService.UpdateClientOrderStatusAsync(orderId, updateClientOrderStatusRequest);
                logger.LogInformation($"Client Order Status with ID: {orderId} updated.");
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { Message = ex.Message }); // Returns 400 Bad Request
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
            
        }



        [HttpPut]
        [Route("ClientApproveOrderStatus/orderId")]
        // [Authorize(Roles = "Client, Admin")]
        public async Task<ActionResult<string>> UpdateOrderStatusToApproveApproveAsync(string orderId)
        {
            try
            {
                var result = await clientOrderService.UpdateOrderStatusToApproveAsync(orderId);
                logger.LogInformation($"Client Order Status with ID: {orderId} updated.");
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { Message = ex.Message }); // Returns 400 Bad Request
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }

        }


        [HttpPut]
        [Route("UpdateClientOrderStatusHasDispute/orderId")]
        // [Authorize(Roles = "Caregiver, Admin")]
        public async Task<ActionResult<string>> UpdateClientOrderStatusHasDisputeAsync(string orderId, UpdateClientOrderStatusHasDisputeRequest  updateClientOrderStatusHasDisputeRequest)
        {
            try
            {
                var result = await clientOrderService.UpdateClientOrderStatusHasDisputeAsync(orderId, updateClientOrderStatusHasDisputeRequest);
                logger.LogInformation($"Client Order Status with ID: {orderId} updated.");
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { Message = ex.Message }); // Returns 400 Bad Request
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
            
        }


       
    }
}
