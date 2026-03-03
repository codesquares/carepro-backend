using Application.DTOs;
using Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text;

namespace CarePro_Api.Controllers.Content
{
    [ApiController]
    [Route("api/booking-commitment")]
    public class BookingCommitmentController : ControllerBase
    {
        private readonly IBookingCommitmentService _commitmentService;
        private readonly FlutterwaveService _flutterwaveService;
        private readonly ILogger<BookingCommitmentController> _logger;

        public BookingCommitmentController(
            IBookingCommitmentService commitmentService,
            FlutterwaveService flutterwaveService,
            ILogger<BookingCommitmentController> logger)
        {
            _commitmentService = commitmentService;
            _flutterwaveService = flutterwaveService;
            _logger = logger;
        }

        /// <summary>
        /// Initiates a ₦5,000 booking commitment fee payment to unlock messaging access for a gig.
        /// The fee is later deducted from the full gig payment.
        /// </summary>
        [HttpPost("initiate")]
        [Authorize]
        public async Task<IActionResult> InitiateCommitment([FromBody] BookingCommitmentRequest request)
        {
            var clientId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value
                ?? User.FindFirst("userId")?.Value;

            if (string.IsNullOrEmpty(clientId))
                return Unauthorized(new { success = false, message = "User not authenticated." });

            var result = await _commitmentService.InitiateCommitmentAsync(request, clientId);

            if (!result.IsSuccess)
                return BadRequest(new { success = false, message = string.Join(", ", result.Errors) });

            return Ok(result.Value);
        }

        /// <summary>
        /// Flutterwave webhook endpoint for booking commitment payments.
        /// Uses the same signature verification as the main payment webhook.
        /// </summary>
        [HttpPost("webhook")]
        [AllowAnonymous]
        public async Task<IActionResult> CommitmentWebhook()
        {
            // Read raw body for signature verification
            string rawBody;
            using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
            {
                rawBody = await reader.ReadToEndAsync();
            }

            // Verify Flutterwave signature
            var signature = Request.Headers["flutterwave-signature"].FirstOrDefault()
                ?? Request.Headers["verif-hash"].FirstOrDefault();

            if (!_flutterwaveService.VerifyWebhookSignature(rawBody, signature))
            {
                _logger.LogWarning("Invalid commitment webhook signature received.");
                return Unauthorized(new { success = false, message = "Invalid signature." });
            }

            _logger.LogInformation("Commitment webhook raw payload: {RawBody}", rawBody);

            // Parse payload
            FlutterwaveWebhookPayload? payload;
            try
            {
                payload = System.Text.Json.JsonSerializer.Deserialize<FlutterwaveWebhookPayload>(rawBody,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse commitment webhook payload");
                return BadRequest(new { success = false, message = "Invalid payload format." });
            }

            if (payload == null)
                return BadRequest(new { success = false, message = "Invalid payload." });

            _logger.LogInformation(
                "Commitment webhook received. TxRef: {TxRef}, Status: {Status}, Amount: {Amount}",
                payload.TxRef, payload.Status, payload.Amount);

            var status = payload.Status?.ToLower() ?? string.Empty;
            if (status != "successful")
            {
                _logger.LogInformation("Ignoring non-successful commitment webhook. Status: {Status}", status);
                return Ok(new { success = true, message = "Webhook received." });
            }

            var txRef = payload.TxRef ?? string.Empty;
            var transactionId = payload.Id.ToString();

            // Only process commitment tx_refs (safety check)
            if (!txRef.StartsWith("CAREPRO-COMMIT-"))
            {
                _logger.LogInformation("Non-commitment TxRef received on commitment webhook: {TxRef}. Ignoring.", txRef);
                return Ok(new { success = true, message = "Webhook received." });
            }

            // Verify with Flutterwave API
            var verification = await _flutterwaveService.VerifyTransactionAsync(transactionId);
            if (verification == null || !verification.Success ||
                verification.Status.ToLower() != "successful")
            {
                _logger.LogWarning("Commitment transaction verification failed for TxRef: {TxRef}", txRef);
                return BadRequest(new { success = false, message = "Transaction verification failed." });
            }

            // Complete the commitment
            var result = await _commitmentService.CompleteCommitmentAsync(
                txRef,
                transactionId,
                payload.Amount > 0 ? payload.Amount : payload.ChargedAmount
            );

            if (!result.IsSuccess)
            {
                _logger.LogError("Failed to complete commitment for TxRef: {TxRef}. Errors: {Errors}",
                    txRef, string.Join(", ", result.Errors));
                return BadRequest(new { success = false, message = "Commitment processing failed." });
            }

            _logger.LogInformation("Commitment completed successfully for TxRef: {TxRef}", txRef);
            return Ok(new { success = true, message = "Commitment processed successfully." });
        }

        /// <summary>
        /// Checks whether the authenticated client has unlocked messaging access for a specific gig.
        /// </summary>
        [HttpGet("check/{gigId}")]
        [Authorize]
        public async Task<IActionResult> CheckCommitment(string gigId)
        {
            var clientId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value
                ?? User.FindFirst("userId")?.Value;

            if (string.IsNullOrEmpty(clientId))
                return Unauthorized(new { success = false, message = "User not authenticated." });

            var status = await _commitmentService.GetCommitmentStatusAsync(clientId, gigId);
            return Ok(status);
        }

        /// <summary>
        /// Gets the status of a commitment payment by transaction reference.
        /// IDOR protected: only the commitment owner can view.
        /// </summary>
        [HttpGet("status/{transactionReference}")]
        [Authorize]
        public async Task<IActionResult> GetCommitmentStatus(string transactionReference)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User.FindFirst("sub")?.Value
                ?? User.FindFirst("userId")?.Value;
            var role = User.FindFirstValue(ClaimTypes.Role);
            var isAdmin = role == "Admin" || role == "SuperAdmin";

            var commitment = await _commitmentService.GetByTransactionReferenceAsync(transactionReference);
            if (commitment == null)
                return NotFound(new { success = false, message = "Commitment not found." });

            // IDOR protection
            if (!isAdmin && commitment.ClientId != currentUserId)
                return Forbid();

            return Ok(new
            {
                success = commitment.Status == Domain.Entities.BookingCommitmentStatus.Completed,
                status = commitment.Status.ToString().ToLower(),
                transactionReference = commitment.TransactionReference,
                flutterwaveTransactionId = commitment.FlutterwaveTransactionId,
                gigId = commitment.GigId,
                caregiverId = commitment.CaregiverId,
                amount = commitment.Amount,
                flutterwaveFees = commitment.FlutterwaveFees,
                totalCharged = commitment.TotalCharged,
                completedAt = commitment.CompletedAt,
                isAppliedToOrder = commitment.IsAppliedToOrder,
                errorMessage = commitment.ErrorMessage
            });
        }
    }
}
