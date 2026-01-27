using Application.DTOs;
using Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text;

namespace CarePro_Api.Controllers.Content
{
    [ApiController]
    [Route("api/payments")]
    public class PaymentsController : ControllerBase
    {
        private readonly IPendingPaymentService _pendingPaymentService;
        private readonly FlutterwaveService _flutterwaveService;
        private readonly ILogger<PaymentsController> _logger;

        public PaymentsController(
            IPendingPaymentService pendingPaymentService,
            FlutterwaveService flutterwaveService,
            ILogger<PaymentsController> logger)
        {
            _pendingPaymentService = pendingPaymentService;
            _flutterwaveService = flutterwaveService;
            _logger = logger;
        }

        /// <summary>
        /// Initiates a secure payment. All pricing is calculated server-side.
        /// </summary>
        [HttpPost("initiate")]
        [Authorize]
        public async Task<IActionResult> InitiatePayment([FromBody] InitiatePaymentRequest request)
        {
            // Get client ID from the authenticated user
            var clientId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                ?? User.FindFirst("sub")?.Value
                ?? User.FindFirst("userId")?.Value;

            if (string.IsNullOrEmpty(clientId))
            {
                return Unauthorized(new { success = false, message = "User not authenticated." });
            }

            var result = await _pendingPaymentService.CreatePendingPaymentAsync(request, clientId);

            if (!result.IsSuccess)
            {
                return BadRequest(new { success = false, message = string.Join(", ", result.Errors) });
            }

            return Ok(result.Value);
        }

        /// <summary>
        /// Flutterwave v4 webhook endpoint. Receives payment notifications.
        /// This endpoint is NOT authenticated but verifies the Flutterwave signature using HMAC-SHA256.
        /// </summary>
        [HttpPost("webhook")]
        [AllowAnonymous]
        public async Task<IActionResult> FlutterwaveWebhook()
        {
            // Read the raw body for signature verification
            string rawBody;
            using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
            {
                rawBody = await reader.ReadToEndAsync();
            }

            // Get the signature from headers (v4 uses 'flutterwave-signature')
            var signature = Request.Headers["flutterwave-signature"].FirstOrDefault() 
                ?? Request.Headers["verif-hash"].FirstOrDefault(); // fallback for v3 compatibility
            
            // Verify webhook signature
            if (!_flutterwaveService.VerifyWebhookSignature(rawBody, signature))
            {
                _logger.LogWarning("Invalid webhook signature received. Signature: {Signature}", signature);
                return Unauthorized(new { success = false, message = "Invalid signature." });
            }

            // DEBUG: Log the raw webhook payload to see what Flutterwave is sending
            _logger.LogInformation("Raw webhook payload: {RawBody}", rawBody);

            // Parse the payload
            FlutterwaveWebhookPayload? payload;
            try
            {
                payload = System.Text.Json.JsonSerializer.Deserialize<FlutterwaveWebhookPayload>(rawBody, 
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse webhook payload");
                return BadRequest(new { success = false, message = "Invalid payload format." });
            }

            if (payload == null)
            {
                _logger.LogWarning("Webhook received with null payload");
                return BadRequest(new { success = false, message = "Invalid payload." });
            }

            _logger.LogInformation(
                "Webhook received. TxRef: {TxRef}, Status: {Status}, Amount: {Amount}",
                payload.TxRef, payload.Status, payload.Amount);

            // Flutterwave v3 sends status directly at root level
            var status = payload.Status?.ToLower() ?? string.Empty;
            
            // Only process successful payments
            if (status != "successful")
            {
                _logger.LogInformation("Ignoring non-successful webhook. Status: {Status}", status);
                return Ok(new { success = true, message = "Webhook received." });
            }

            // Get transaction reference and ID from flat structure
            var txRef = payload.TxRef ?? string.Empty;
            var transactionId = payload.Id.ToString();

            // Verify the transaction with Flutterwave API for extra security
            var verification = await _flutterwaveService.VerifyTransactionAsync(transactionId);
            if (verification == null || !verification.Success || 
                verification.Status.ToLower() != "successful")
            {
                _logger.LogWarning("Transaction verification failed for TxRef: {TxRef}", txRef);
                return BadRequest(new { success = false, message = "Transaction verification failed." });
            }

            // Complete the payment (this will verify amounts and create the order)
            var result = await _pendingPaymentService.CompletePaymentAsync(
                txRef,
                transactionId,
                payload.Amount > 0 ? payload.Amount : payload.ChargedAmount
            );

            if (!result.IsSuccess)
            {
                _logger.LogError("Failed to complete payment for TxRef: {TxRef}. Errors: {Errors}",
                    txRef, string.Join(", ", result.Errors));
                return BadRequest(new { success = false, message = string.Join(", ", result.Errors) });
            }

            _logger.LogInformation("Payment completed successfully for TxRef: {TxRef}", txRef);
            return Ok(new { success = true, message = "Payment processed successfully." });
        }

        /// <summary>
        /// Gets the status and breakdown of a payment by transaction reference.
        /// Call this after redirect from Flutterwave to display payment details.
        /// </summary>
        [HttpGet("status/{transactionReference}")]
        [Authorize]
        public async Task<IActionResult> GetPaymentStatus(string transactionReference)
        {
            var result = await _pendingPaymentService.GetPaymentStatusAsync(transactionReference);

            if (!result.IsSuccess)
            {
                return NotFound(new { success = false, message = string.Join(", ", result.Errors) });
            }

            return Ok(result.Value);
        }

        /// <summary>
        /// Legacy verify endpoint - use /status/{transactionReference} instead
        /// </summary>
        [HttpGet("verify/{transactionId}")]
        [Authorize]
        [Obsolete("Use /status/{transactionReference} instead")]
        public async Task<IActionResult> VerifyPayment(string transactionId)
        {
            var response = await _flutterwaveService.VerifyPayment(transactionId);
            return Ok(response);
        }
    }
}
