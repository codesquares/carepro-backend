using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Application.Interfaces.Content;
using Application.DTOs;

namespace CarePro_Api.Controllers.Content
{
    /// <summary>
    /// DEPRECATED: This legacy webhook controller has been superseded by PaymentsController.FlutterwaveWebhook
    /// which implements proper signature verification and server-to-server payment verification.
    /// All endpoints are disabled and return 410 Gone.
    /// </summary>
    [ApiController]
    [Route("api/webhook")]
    public class WebhookController : ControllerBase
    {
        private readonly ILogger<WebhookController> _logger;

        public WebhookController(IContractService contractService, ILogger<WebhookController> logger)
        {
            _logger = logger;
        }

        [HttpPost("flutterwave")]
        public IActionResult ReceiveFlutterwaveWebhook()
        {
            _logger.LogWarning("SECURITY: Legacy webhook endpoint /api/webhook/flutterwave called. This endpoint is disabled. Use /api/payments/webhook instead.");
            return StatusCode(410, new { message = "This endpoint is deprecated. Use /api/payments/webhook." });
        }

        [HttpPost("contract-payment")]
        public IActionResult HandleContractPayment()
        {
            _logger.LogWarning("SECURITY: Legacy webhook endpoint /api/webhook/contract-payment called. This endpoint is disabled.");
            return StatusCode(410, new { message = "This endpoint is deprecated." });
        }
    }

}
