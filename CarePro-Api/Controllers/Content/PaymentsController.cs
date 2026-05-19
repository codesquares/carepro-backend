using Application.DTOs;
using Application.Interfaces;
using Application.Interfaces.Content;
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
        private readonly IBookingCommitmentService _bookingCommitmentService;
        private readonly FlutterwaveService _flutterwaveService;
        private readonly IReceiptPdfService _receiptPdfService;
        private readonly ILogger<PaymentsController> _logger;

        public PaymentsController(
            IPendingPaymentService pendingPaymentService,
            IBookingCommitmentService bookingCommitmentService,
            FlutterwaveService flutterwaveService,
            IReceiptPdfService receiptPdfService,
            ILogger<PaymentsController> logger)
        {
            _pendingPaymentService = pendingPaymentService;
            _bookingCommitmentService = bookingCommitmentService;
            _flutterwaveService = flutterwaveService;
            _receiptPdfService = receiptPdfService;
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

            // Parse the payload — Flutterwave v3 wraps fields under "data": { ... }
            // Fall back to flat structure for legacy compatibility
            var jsonOptions = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            FlutterwaveWebhookPayload? payload;
            try
            {
                var envelope = System.Text.Json.JsonSerializer.Deserialize<FlutterwaveWebhookEnvelope>(rawBody, jsonOptions);
                payload = envelope?.Data
                    ?? System.Text.Json.JsonSerializer.Deserialize<FlutterwaveWebhookPayload>(rawBody, jsonOptions);
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

            // ── ROUTE: Booking commitment payments vs regular gig payments ──────
            if (txRef.StartsWith("CAREPRO-COMMIT-", StringComparison.OrdinalIgnoreCase))
            {
                var commitResult = await _bookingCommitmentService.CompleteCommitmentAsync(
                    txRef,
                    transactionId,
                    payload.Amount > 0 ? payload.Amount : payload.ChargedAmount
                );

                if (!commitResult.IsSuccess)
                {
                    _logger.LogError("Failed to complete commitment for TxRef: {TxRef}. Errors: {Errors}",
                        txRef, string.Join(", ", commitResult.Errors));
                    return BadRequest(new { success = false, message = "Commitment processing failed." });
                }

                _logger.LogInformation("Booking commitment completed successfully for TxRef: {TxRef}", txRef);
                return Ok(new { success = true, message = "Commitment processed successfully." });
            }
            // ── END ROUTE ─────────────────────────────────────────────────

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
                // Return generic message — never leak internal error details to external webhook callers
                return BadRequest(new { success = false, message = "Payment processing failed." });
            }

            _logger.LogInformation("Payment completed successfully for TxRef: {TxRef}", txRef);
            return Ok(new { success = true, message = "Payment processed successfully." });
        }

        /// <summary>
        /// Gets the status and breakdown of a payment by transaction reference.
        /// Call this after redirect from Flutterwave to display payment details.
        /// IDOR protected: only the payment owner or admin can view status.
        /// </summary>
        [HttpGet("status/{transactionReference}")]
        [Authorize]
        public async Task<IActionResult> GetPaymentStatus(string transactionReference)
        {
            var result = await _pendingPaymentService.GetPaymentStatusAsync(transactionReference);

            if (!result.IsSuccess)
            {
                return NotFound(new { success = false, message = "Payment not found." });
            }

            // IDOR protection: verify the caller owns this payment
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User.FindFirst("sub")?.Value
                ?? User.FindFirst("userId")?.Value;
            var role = User.FindFirstValue(ClaimTypes.Role);
            var isAdmin = role == "Admin" || role == "SuperAdmin";

            // GetPaymentStatusAsync doesn't expose clientId directly.
            // Use the underlying PendingPayment record for ownership check.
            var paymentRecord = await _pendingPaymentService.GetByTransactionReferenceAsync(transactionReference);
            if (paymentRecord != null && !isAdmin && paymentRecord.ClientId != currentUserId)
            {
                return Forbid();
            }

            return Ok(result.Value);
        }

        /// <summary>
        /// Verifies payment with Flutterwave. IDOR protected: only the payment owner or admin.
        /// Returns a sanitized response (no raw Flutterwave internals).
        /// </summary>
        [HttpGet("verify/{transactionId}")]
        [Authorize]
        [Obsolete("Use /status/{transactionReference} instead")]
        public async Task<IActionResult> VerifyPayment(string transactionId)
        {
            // IDOR: look up the PendingPayment that owns this Flutterwave transaction ID
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User.FindFirst("sub")?.Value
                ?? User.FindFirst("userId")?.Value;
            var role = User.FindFirstValue(ClaimTypes.Role);
            var isAdmin = role == "Admin" || role == "SuperAdmin";

            // Verify ownership via PendingPayment record
            var verification = await _flutterwaveService.VerifyTransactionAsync(transactionId);
            if (verification == null)
            {
                return NotFound(new { success = false, message = "Transaction not found." });
            }

            // Cross-check: find the PendingPayment by the tx_ref returned from Flutterwave
            if (!string.IsNullOrEmpty(verification.TxRef))
            {
                var paymentRecord = await _pendingPaymentService.GetByTransactionReferenceAsync(verification.TxRef);
                if (paymentRecord != null && !isAdmin && paymentRecord.ClientId != currentUserId)
                {
                    return Forbid();
                }
            }

            // Return sanitized response — only what the client needs
            return Ok(new
            {
                success = verification.Success,
                status = verification.Status,
                amount = verification.Amount,
                currency = verification.Currency,
                txRef = verification.TxRef,
                transactionId = transactionId
            });
        }

        // -----------------------------------------------------------------
        // Admin recovery tools (added May 2026)
        // -----------------------------------------------------------------

        /// <summary>
        /// Admin: manually resolve a payment that is confirmed as successful on Flutterwave
        /// but is still stuck in Pending on our end (e.g. webhook never arrived or was lost).
        ///
        /// Flow:
        ///   1. Verifies the transaction ID with Flutterwave — only proceeds if status is "successful".
        ///   2. Uses the tx_ref Flutterwave returns to look up the local PendingPayment.
        ///   3. Routes to CommitmentPayment or regular GigPayment based on tx_ref prefix.
        ///   4. Calls the same CompletePaymentAsync / CompleteCommitmentAsync the webhook calls —
        ///      so all downstream effects (order creation, subscription, billing record, notifications)
        ///      happen exactly as they would have via webhook.
        ///
        /// Amount override: Flutterwave's verified amount is used directly, which bypasses the
        /// stored-amount mismatch guard. The admin is the human check here — they have confirmed
        /// the money was received on the Flutterwave dashboard before calling this.
        /// For AmountMismatch-flagged records the admin must explicitly pass forceOverride=true.
        /// </summary>
        [HttpPost("admin/resolve/{flutterwaveTransactionId}")]
        [Authorize(Roles = "Admin, SuperAdmin")]
        public async Task<IActionResult> AdminResolveStuckPayment(
            string flutterwaveTransactionId,
            [FromQuery] bool forceOverride = false)
        {
            var adminId = User.FindFirst("userId")?.Value
                       ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

            _logger.LogWarning(
                "ADMIN ACTION: AdminResolveStuckPayment called by AdminId={AdminId}, FlwTxId={FlwTxId}, ForceOverride={ForceOverride}",
                adminId, flutterwaveTransactionId, forceOverride);

            // 1. Verify with Flutterwave.
            // Accept either a numeric Flutterwave transaction ID (e.g. "3296847")
            // or a tx_ref (e.g. "CAREPRO-PAY-abc123" or "CodeSquareLimit_DMKQPT...").
            FlutterwaveVerificationResult? verification;
            if (long.TryParse(flutterwaveTransactionId, out _))
            {
                verification = await _flutterwaveService.VerifyTransactionAsync(flutterwaveTransactionId);
            }
            else
            {
                // Input is a tx_ref — use Flutterwave's verify_by_reference endpoint
                verification = await _flutterwaveService.VerifyByTxRefAsync(flutterwaveTransactionId);
            }
            if (verification == null)
            {
                return NotFound(new { success = false, message = "Flutterwave returned no data for this transaction ID." });
            }
            if (!verification.Success || !string.Equals(verification.Status, "successful", StringComparison.OrdinalIgnoreCase))
            {
                var detail = !string.IsNullOrEmpty(verification.ErrorMessage)
                    ? verification.ErrorMessage
                    : string.IsNullOrEmpty(verification.Status)
                        ? "Flutterwave could not locate this transaction. Check that the tx_ref or transaction ID is correct and that the payment was completed on Flutterwave's side."
                        : $"Transaction status is '{verification.Status}' — only 'successful' transactions can be resolved.";
                return BadRequest(new
                {
                    success = false,
                    message = "Could not resolve payment.",
                    detail,
                    flutterwaveStatus = verification.Status
                });
            }

            var txRef = verification.TxRef;
            var confirmedAmount = verification.Amount;

            if (string.IsNullOrWhiteSpace(txRef))
            {
                return BadRequest(new { success = false, message = "Flutterwave did not return a tx_ref for this transaction." });
            }

            // 2. Route by tx_ref prefix — mirrors the webhook routing logic
            if (txRef.StartsWith("CAREPRO-COMMIT-", StringComparison.OrdinalIgnoreCase))
            {
                // For AmountMismatch-flagged commitments, admin must pass forceOverride=true
                // which causes us to reset the status first.
                var commitment = await _bookingCommitmentService.GetByTransactionReferenceAsync(txRef);
                if (commitment?.Status == Domain.Entities.BookingCommitmentStatus.AmountMismatch && !forceOverride)
                {
                    return Conflict(new
                    {
                        success = false,
                        message = "This commitment is flagged as AmountMismatch. Re-call with ?forceOverride=true to override.",
                        txRef,
                        confirmedAmount,
                        storedAmount = commitment.TotalCharged
                    });
                }

                if (commitment?.Status == Domain.Entities.BookingCommitmentStatus.AmountMismatch && forceOverride)
                {
                    _logger.LogWarning(
                        "ADMIN OVERRIDE: Resetting AmountMismatch on commitment TxRef={TxRef}, AdminId={AdminId}",
                        txRef, adminId);
                    await _bookingCommitmentService.ResetAmountMismatchAsync(txRef,
                        $"AmountMismatch overridden by admin {adminId} on {DateTime.UtcNow:u}");
                }

                var commitResult = await _bookingCommitmentService.CompleteCommitmentAsync(
                    txRef,
                    flutterwaveTransactionId,
                    confirmedAmount);

                if (!commitResult.IsSuccess)
                {
                    _logger.LogError(
                        "Admin resolve failed for commitment TxRef={TxRef}. Errors: {Errors}",
                        txRef, string.Join(", ", commitResult.Errors));
                    return BadRequest(new { success = false, message = "Commitment could not be completed.", errors = commitResult.Errors });
                }

                _logger.LogInformation(
                    "ADMIN ACTION: Commitment resolved successfully. TxRef={TxRef}, FlwTxId={FlwTxId}, AdminId={AdminId}",
                    txRef, flutterwaveTransactionId, adminId);

                return Ok(new
                {
                    success = true,
                    message = "Booking commitment resolved successfully.",
                    txRef,
                    flutterwaveTransactionId,
                    confirmedAmount
                });
            }
            else
            {
                // Regular gig payment — handle AmountMismatch override the same way
                var pendingPayment = await _pendingPaymentService.GetByTransactionReferenceAsync(txRef);
                if (pendingPayment?.Status == Domain.Entities.PendingPaymentStatus.AmountMismatch && !forceOverride)
                {
                    return Conflict(new
                    {
                        success = false,
                        message = "This payment is flagged as AmountMismatch. Re-call with ?forceOverride=true to override.",
                        txRef,
                        confirmedAmount,
                        storedAmount = pendingPayment.TotalAmount
                    });
                }

                if (pendingPayment?.Status == Domain.Entities.PendingPaymentStatus.AmountMismatch && forceOverride)
                {
                    _logger.LogWarning(
                        "ADMIN OVERRIDE: Resetting AmountMismatch on payment TxRef={TxRef}, AdminId={AdminId}",
                        txRef, adminId);
                    await _pendingPaymentService.ResetAmountMismatchAsync(txRef,
                        $"AmountMismatch overridden by admin {adminId} on {DateTime.UtcNow:u}");
                }

                var result = await _pendingPaymentService.CompletePaymentAsync(
                    txRef,
                    flutterwaveTransactionId,
                    confirmedAmount);

                if (!result.IsSuccess)
                {
                    _logger.LogError(
                        "Admin resolve failed for payment TxRef={TxRef}. Errors: {Errors}",
                        txRef, string.Join(", ", result.Errors));
                    return BadRequest(new { success = false, message = "Payment could not be completed.", errors = result.Errors });
                }

                _logger.LogInformation(
                    "ADMIN ACTION: Payment resolved successfully. TxRef={TxRef}, FlwTxId={FlwTxId}, AdminId={AdminId}",
                    txRef, flutterwaveTransactionId, adminId);

                return Ok(new
                {
                    success = true,
                    message = "Payment resolved successfully.",
                    txRef,
                    flutterwaveTransactionId,
                    confirmedAmount,
                    orderId = result.Value?.ClientOrderId
                });
            }
        }

        /// <summary>
        /// Admin: issue a full or partial refund for a Flutterwave transaction.
        /// amount is optional — omit to refund the full transaction amount.
        /// This calls Flutterwave's POST /v3/transactions/{id}/refund directly.
        /// </summary>
        [HttpPost("admin/refund/{flutterwaveTransactionId}")]
        [Authorize(Roles = "Admin, SuperAdmin")]
        public async Task<IActionResult> AdminRefundTransaction(
            string flutterwaveTransactionId,
            [FromQuery] decimal? amount = null)
        {
            var adminId = User.FindFirst("userId")?.Value
                       ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

            // Validate partial amount if provided
            if (amount.HasValue && amount.Value <= 0)
            {
                return BadRequest(new { success = false, message = "Refund amount must be greater than zero." });
            }

            _logger.LogWarning(
                "ADMIN ACTION: AdminRefundTransaction called by AdminId={AdminId}, FlwTxId={FlwTxId}, Amount={Amount}",
                adminId, flutterwaveTransactionId, amount?.ToString() ?? "full");

            // Verify the transaction exists and is successful before refunding
            FlutterwaveVerificationResult? verification;
            if (long.TryParse(flutterwaveTransactionId, out _))
            {
                verification = await _flutterwaveService.VerifyTransactionAsync(flutterwaveTransactionId);
            }
            else
            {
                verification = await _flutterwaveService.VerifyByTxRefAsync(flutterwaveTransactionId);
            }
            if (verification == null)
            {
                return NotFound(new { success = false, message = "Transaction not found on Flutterwave." });
            }
            if (!verification.Success || !string.Equals(verification.Status, "successful", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new
                {
                    success = false,
                    message = $"Cannot refund a transaction that is not successful. Flutterwave status: '{verification.Status}'.",
                    flutterwaveStatus = verification.Status
                });
            }

            // Guard: partial refund cannot exceed the original transaction amount
            if (amount.HasValue && amount.Value > verification.Amount)
            {
                return BadRequest(new
                {
                    success = false,
                    message = $"Refund amount ({amount.Value}) exceeds the original transaction amount ({verification.Amount})."
                });
            }

            var refundResult = await _flutterwaveService.RefundTransactionAsync(verification.TransactionId, amount);

            if (!refundResult.Success)
            {
                _logger.LogError(
                    "Admin refund failed. FlwTxId={FlwTxId}, AdminId={AdminId}, Error={Error}",
                    flutterwaveTransactionId, adminId, refundResult.ErrorMessage);
                return BadRequest(new
                {
                    success = false,
                    message = "Refund request was rejected by Flutterwave.",
                    detail = refundResult.ErrorMessage
                });
            }

            _logger.LogInformation(
                "ADMIN ACTION: Refund issued. FlwTxId={FlwTxId}, RefundId={RefundId}, AmountRefunded={Amount}, AdminId={AdminId}",
                flutterwaveTransactionId, refundResult.RefundId, refundResult.AmountRefunded, adminId);

            return Ok(new
            {
                success = true,
                message = "Refund issued successfully.",
                flutterwaveTransactionId,
                refundId = refundResult.RefundId,
                amountRefunded = refundResult.AmountRefunded,
                refundStatus = refundResult.Status
            });
        }

        // ── Receipt download endpoints ────────────────────────────────────

        /// <summary>
        /// Downloads a PDF receipt for a completed booking commitment fee payment.
        /// Only the client who made the payment can download their own receipt.
        /// </summary>
        [HttpGet("receipt/commitment/{txRef}")]
        [Authorize]
        public async Task<IActionResult> DownloadCommitmentReceipt(string txRef)
        {
            var clientId = User.FindFirst("userId")?.Value
                        ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

            var commitment = await _bookingCommitmentService.GetByTransactionReferenceAsync(txRef);

            if (commitment == null)
                return NotFound(new { success = false, message = "Receipt not found." });

            if (commitment.ClientId != clientId)
                return Forbid();

            if (commitment.Status != Domain.Entities.BookingCommitmentStatus.Completed)
                return BadRequest(new { success = false, message = "Receipt is only available for completed payments." });

            var receiptData = new CommitmentReceiptData
            {
                TransactionReference = commitment.TransactionReference,
                FlutterwaveTransactionId = commitment.FlutterwaveTransactionId,
                ClientName = "CarePro Client",
                ClientEmail = commitment.Email,
                CaregiverName = "CarePro Caregiver",
                GigTitle = "Care Service",
                CommitmentFee = commitment.Amount,
                GatewayFees = commitment.FlutterwaveFees,
                TotalCharged = commitment.TotalCharged,
                Currency = "NGN",
                PaidAt = commitment.CompletedAt ?? DateTime.UtcNow
            };

            var pdfBytes = _receiptPdfService.GenerateCommitmentReceipt(receiptData);
            var fileName = $"CarePro-Receipt-Commitment-{txRef}.pdf";

            return File(pdfBytes, "application/pdf", fileName);
        }

        /// <summary>
        /// Downloads a PDF receipt for a completed full gig order payment.
        /// Only the client who made the payment can download their own receipt.
        /// </summary>
        [HttpGet("receipt/order/{txRef}")]
        [Authorize]
        public async Task<IActionResult> DownloadOrderReceipt(string txRef)
        {
            var clientId = User.FindFirst("userId")?.Value
                        ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

            var payment = await _pendingPaymentService.GetByTransactionReferenceAsync(txRef);

            if (payment == null)
                return NotFound(new { success = false, message = "Receipt not found." });

            if (payment.ClientId != clientId)
                return Forbid();

            if (payment.Status != Domain.Entities.PendingPaymentStatus.Completed)
                return BadRequest(new { success = false, message = "Receipt is only available for completed payments." });

            var receiptData = new OrderReceiptData
            {
                TransactionReference = payment.TransactionReference,
                FlutterwaveTransactionId = payment.FlutterwaveTransactionId,
                ClientOrderId = payment.ClientOrderId,
                ClientName = "CarePro Client",
                ClientEmail = payment.Email,
                CaregiverName = "CarePro Caregiver",
                GigTitle = "Care Service",
                ServiceType = payment.ServiceType,
                FrequencyPerWeek = payment.FrequencyPerWeek,
                BasePrice = payment.BasePrice,
                OrderFee = payment.OrderFee,
                ServiceCharge = payment.ServiceCharge,
                GatewayFees = payment.FlutterwaveFees,
                CommitmentFeeDeducted = 0m,
                TotalCharged = payment.TotalAmount,
                Currency = payment.Currency,
                PaidAt = payment.CompletedAt ?? DateTime.UtcNow
            };

            var pdfBytes = _receiptPdfService.GenerateOrderReceipt(receiptData);
            var fileName = $"CarePro-Receipt-Order-{txRef}.pdf";

            return File(pdfBytes, "application/pdf", fileName);
        }
    }
}
