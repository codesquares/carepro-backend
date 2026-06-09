using Application.Commands;
using Application.DTOs;
using Application.Interfaces;
using Application.Interfaces.Content;
using Application.Interfaces.Email;
using Domain.Entities;
using Infrastructure.Content.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;

namespace Infrastructure.Content.Services
{
    public class BookingCommitmentService : IBookingCommitmentService
    {
        private readonly CareProDbContext _dbContext;
        private readonly IGigServices _gigServices;
        private readonly FlutterwaveService _flutterwaveService;
        private readonly IMediator _mediator;
        private readonly IEmailService _emailService;
        private readonly IReceiptPdfService _receiptPdfService;
        private readonly ILogger<BookingCommitmentService> _logger;

        /// <summary>
        /// Fixed booking commitment fee in NGN
        /// </summary>
        public const decimal COMMITMENT_FEE = 5000m;

        /// <summary>
        /// Flutterwave local card fee rate (1.4%, capped at 2000 NGN)
        /// </summary>
        private const decimal FLUTTERWAVE_FEE_RATE = 0.014m;
        private const decimal FLUTTERWAVE_FEE_CAP = 2000m;

        public BookingCommitmentService(
            CareProDbContext dbContext,
            IGigServices gigServices,
            FlutterwaveService flutterwaveService,
            IMediator mediator,
            IEmailService emailService,
            IReceiptPdfService receiptPdfService,
            ILogger<BookingCommitmentService> logger)
        {
            _dbContext = dbContext;
            _gigServices = gigServices;
            _flutterwaveService = flutterwaveService;
            _mediator = mediator;
            _emailService = emailService;
            _receiptPdfService = receiptPdfService;
            _logger = logger;
        }

        public async Task<Result<BookingCommitmentResponse>> InitiateCommitmentAsync(BookingCommitmentRequest request, string clientId)
        {
            var errors = new List<string>();

            // Validate request
            if (string.IsNullOrEmpty(request.GigId))
                errors.Add("GigId is required.");
            if (string.IsNullOrEmpty(request.Email))
                errors.Add("Email is required.");
            if (string.IsNullOrEmpty(request.RedirectUrl))
                errors.Add("RedirectUrl is required.");

            if (errors.Any())
                return Result<BookingCommitmentResponse>.Failure(errors);

            // Fetch gig to validate it exists and is active
            var gig = await _gigServices.GetGigAsync(request.GigId);
            if (gig == null)
                return Result<BookingCommitmentResponse>.Failure(new List<string> { "Gig not found." });

            if (gig.Status?.ToLower() != "active" && gig.Status?.ToLower() != "published")
                return Result<BookingCommitmentResponse>.Failure(new List<string> { "This gig is not currently available." });

            // Prevent client from committing to their own gig
            if (gig.CaregiverId == clientId)
                return Result<BookingCommitmentResponse>.Failure(new List<string> { "You cannot pay a commitment fee for your own gig." });

            // ── EXISTING ORDER GUARD ─────────────────────────────────────────
            // Block commitment only if the client has a genuinely active order (In Progress, Disputed).
            // Cancelled, Terminated, and Completed orders are terminal — client can re-commit.
            var terminalStatuses = new[] { "Completed", "Cancelled", "Terminated" };
            var existingActiveOrder = await _dbContext.ClientOrders
                .FirstOrDefaultAsync(o => o.ClientId == clientId
                                       && o.GigId == request.GigId
                                       && o.ClientOrderStatus != null
                                       && !terminalStatuses.Contains(o.ClientOrderStatus));

            if (existingActiveOrder != null)
            {
                _logger.LogWarning(
                    "Commitment fee blocked — active order exists. ClientId: {ClientId}, GigId: {GigId}, OrderId: {OrderId}, Status: {Status}",
                    clientId, request.GigId, existingActiveOrder.Id, existingActiveOrder.ClientOrderStatus);
                return Result<BookingCommitmentResponse>.Failure(new List<string>
                {
                    "You already have an active order for this gig. A commitment fee is not required."
                });
            }

            // ── RECURRING SUBSCRIPTION GUARD ─────────────────────────────────
            // For recurring orders, "Completed" means a billing cycle ended — block if the subscription is still active.
            var completedRecurringOrder = await _dbContext.ClientOrders
                .FirstOrDefaultAsync(o => o.ClientId == clientId
                                       && o.GigId == request.GigId
                                       && o.ClientOrderStatus == "Completed"
                                       && o.SubscriptionId != null);

            if (completedRecurringOrder != null)
            {
                var activeSubscription = await _dbContext.Subscriptions
                    .FirstOrDefaultAsync(s => s.Id == completedRecurringOrder.SubscriptionId
                                           && (s.Status == SubscriptionStatus.Active
                                               || s.Status == SubscriptionStatus.PendingCancellation
                                               || s.Status == SubscriptionStatus.Charging));

                if (activeSubscription != null)
                {
                    _logger.LogWarning(
                        "Commitment fee blocked — active recurring subscription exists. ClientId: {ClientId}, GigId: {GigId}, SubscriptionId: {SubscriptionId}",
                        clientId, request.GigId, completedRecurringOrder.SubscriptionId);
                    return Result<BookingCommitmentResponse>.Failure(new List<string>
                    {
                        "You have an active recurring subscription for this gig. A commitment fee is not required."
                    });
                }
            }

            // Check if client already has an unused (completed, not yet applied) commitment for this gig
            var existingCompleted = await _dbContext.BookingCommitments
                .FirstOrDefaultAsync(bc => bc.ClientId == clientId
                                        && bc.GigId == request.GigId
                                        && bc.Status == BookingCommitmentStatus.Completed
                                        && !bc.IsAppliedToOrder);

            if (existingCompleted != null)
            {
                return Result<BookingCommitmentResponse>.Success(new BookingCommitmentResponse
                {
                    Success = true,
                    Message = "You have already unlocked access to this gig.",
                    TransactionReference = existingCompleted.TransactionReference,
                    Amount = existingCompleted.Amount,
                    FlutterwaveFees = existingCompleted.FlutterwaveFees,
                    TotalCharged = existingCompleted.TotalCharged,
                    Currency = "NGN"
                });
            }

            // Check for a fresh pending commitment (reuse if < 1 hour old)
            var existingPending = await _dbContext.BookingCommitments
                .FirstOrDefaultAsync(bc => bc.ClientId == clientId
                                        && bc.GigId == request.GigId
                                        && bc.Status == BookingCommitmentStatus.Pending);

            if (existingPending != null)
            {
                var age = DateTime.UtcNow - existingPending.CreatedAt;
                if (age.TotalMinutes < 20 && !string.IsNullOrEmpty(existingPending.PaymentLink))
                {
                    _logger.LogInformation(
                        "Returning existing commitment payment link. TxRef: {TxRef}, Age: {AgeMinutes}m",
                        existingPending.TransactionReference, (int)age.TotalMinutes);

                    return Result<BookingCommitmentResponse>.Success(new BookingCommitmentResponse
                    {
                        Success = true,
                        Message = "A commitment payment is already in progress. Use the existing payment link.",
                        TransactionReference = existingPending.TransactionReference,
                        PaymentLink = existingPending.PaymentLink,
                        Amount = existingPending.Amount,
                        FlutterwaveFees = existingPending.FlutterwaveFees,
                        TotalCharged = existingPending.TotalCharged,
                        Currency = "NGN"
                    });
                }
                else
                {
                    // Stale — expire it
                    existingPending.Status = BookingCommitmentStatus.Expired;
                    existingPending.ErrorMessage = "Expired: superseded by a new commitment attempt.";
                    _dbContext.BookingCommitments.Update(existingPending);
                    await _dbContext.SaveChangesAsync();

                    _logger.LogInformation(
                        "Expired stale commitment. TxRef: {TxRef}, Age: {AgeHours}h",
                        existingPending.TransactionReference, (int)age.TotalHours);

                    // ── Notify client that their previous commitment has expired ──
                    try
                    {
                        await _mediator.Send(new SendNotificationCommand(
                            RecipientId: existingPending.ClientId,
                            SenderId: "system",
                            Type: NotificationTypes.BookingCommitmentExpired,
                            Content: "Your previous booking commitment has expired (it was more than 24 hours old). A new commitment has been started.",
                            Title: "Booking Commitment Expired",
                            RelatedEntityId: existingPending.Id.ToString()));
                    }
                    catch (Exception notifEx)
                    {
                        _logger.LogError(notifEx, "Failed to send commitment-expired notification for Commitment {CommitmentId}", existingPending.Id);
                    }
                }
            }

            // Calculate amounts
            decimal amount = COMMITMENT_FEE;
            decimal flutterwaveFees = CalculateFlutterwaveFees(amount);
            decimal totalCharged = amount + flutterwaveFees;

            // Generate unique transaction reference
            string transactionReference = $"CAREPRO-COMMIT-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8].ToUpper()}";

            var commitment = new BookingCommitment
            {
                Id = ObjectId.GenerateNewId(),
                ClientId = clientId,
                CaregiverId = gig.CaregiverId,
                GigId = request.GigId,
                Amount = amount,
                FlutterwaveFees = flutterwaveFees,
                TotalCharged = totalCharged,
                TransactionReference = transactionReference,
                Email = request.Email,
                RedirectUrl = request.RedirectUrl,
                Status = BookingCommitmentStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };

            // Call Flutterwave to initiate payment
            try
            {
                var flutterwaveResponse = await _flutterwaveService.InitiatePayment(
                    totalCharged,
                    request.Email,
                    "NGN",
                    transactionReference,
                    request.RedirectUrl
                );

                var paymentLink = ExtractPaymentLink(flutterwaveResponse);
                if (string.IsNullOrEmpty(paymentLink))
                {
                    _logger.LogError("Failed to get commitment payment link from Flutterwave. Response: {Response}", flutterwaveResponse);
                    return Result<BookingCommitmentResponse>.Failure(new List<string> { "Failed to initialize payment with Flutterwave." });
                }

                commitment.PaymentLink = paymentLink;

                _dbContext.BookingCommitments.Add(commitment);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation(
                    "Commitment payment initiated. TxRef: {TxRef}, GigId: {GigId}, Amount: {Amount}",
                    transactionReference, request.GigId, totalCharged);

                return Result<BookingCommitmentResponse>.Success(new BookingCommitmentResponse
                {
                    Success = true,
                    Message = "Booking commitment payment initiated successfully.",
                    TransactionReference = transactionReference,
                    PaymentLink = paymentLink,
                    Amount = amount,
                    FlutterwaveFees = flutterwaveFees,
                    TotalCharged = totalCharged,
                    Currency = "NGN"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initiating commitment payment for GigId: {GigId}", request.GigId);
                return Result<BookingCommitmentResponse>.Failure(new List<string> { "An error occurred while processing the commitment payment." });
            }
        }

        public async Task<Result<BookingCommitment>> CompleteCommitmentAsync(string transactionReference, string flutterwaveTransactionId, decimal paidAmount)
        {
            var commitment = await GetByTransactionReferenceAsync(transactionReference);
            if (commitment == null)
            {
                _logger.LogWarning("Commitment completion attempted for unknown TxRef: {TxRef}", transactionReference);
                return Result<BookingCommitment>.Failure(new List<string> { "Commitment record not found." });
            }

            // Idempotency guard
            if (commitment.Status == BookingCommitmentStatus.Completed)
            {
                _logger.LogWarning(
                    "Duplicate CompleteCommitment attempt for TxRef: {TxRef}. Already completed at {CompletedAt}.",
                    transactionReference, commitment.CompletedAt);
                return Result<BookingCommitment>.Success(commitment);
            }

            if (commitment.Status == BookingCommitmentStatus.AmountMismatch)
            {
                _logger.LogWarning("CompleteCommitment retry blocked for flagged TxRef: {TxRef}", transactionReference);
                return Result<BookingCommitment>.Failure(new List<string> { "This commitment was previously flagged for amount mismatch." });
            }

            // Amount verification (tolerance of 0.01)
            if (Math.Abs(paidAmount - commitment.TotalCharged) > 0.01m)
            {
                _logger.LogCritical(
                    "COMMITMENT AMOUNT MISMATCH! TxRef: {TxRef}, Expected: {Expected}, Paid: {Paid}",
                    transactionReference, commitment.TotalCharged, paidAmount);

                commitment.Status = BookingCommitmentStatus.AmountMismatch;
                commitment.ErrorMessage = $"Amount mismatch. Expected: {commitment.TotalCharged}, Paid: {paidAmount}";
                await _dbContext.SaveChangesAsync();

                return Result<BookingCommitment>.Failure(new List<string> { "Payment amount does not match. This incident has been logged." });
            }

            // Mark as completed
            commitment.Status = BookingCommitmentStatus.Completed;
            commitment.FlutterwaveTransactionId = flutterwaveTransactionId;
            commitment.CompletedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation(
                "Commitment completed. TxRef: {TxRef}, ClientId: {ClientId}, GigId: {GigId}",
                transactionReference, commitment.ClientId, commitment.GigId);

            // ── Send receipt email + notifications (non-blocking — failure should not fail the commitment) ──
            try
            {
                await SendCommitmentNotificationsAsync(commitment);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send commitment notifications for TxRef: {TxRef}. Commitment was successful.", transactionReference);
            }

            return Result<BookingCommitment>.Success(commitment);
        }

        public async Task<bool> HasActiveCommitmentAsync(string clientId, string gigId)
        {
            // Only count a commitment as "active" if it hasn't been consumed by an order yet.
            // A commitment with IsAppliedToOrder=true is spent — the client needs a fresh one
            // before they can place another order for the same gig.
            return await _dbContext.BookingCommitments
                .AnyAsync(bc => bc.ClientId == clientId
                             && bc.GigId == gigId
                             && bc.Status == BookingCommitmentStatus.Completed
                             && !bc.IsAppliedToOrder);
        }

        public async Task<bool> HasActiveCommitmentWithCaregiverAsync(string clientId, string caregiverId)
        {
            return await _dbContext.BookingCommitments
                .AnyAsync(bc => bc.ClientId == clientId
                             && bc.CaregiverId == caregiverId
                             && bc.Status == BookingCommitmentStatus.Completed);
        }

        public async Task<BookingCommitment?> GetApplicableCommitmentAsync(string clientId, string gigId)
        {
            // 1. Exact lookup: commitment was paid directly for this gigId
            var direct = await _dbContext.BookingCommitments
                .FirstOrDefaultAsync(bc => bc.ClientId == clientId
                                        && bc.GigId == gigId
                                        && bc.Status == BookingCommitmentStatus.Completed
                                        && !bc.IsAppliedToOrder);

            if (direct != null)
                return direct;

            // 2. Fallback for special gigs created through price negotiation (RegularGig path).
            //    When a client and caregiver agree on a negotiated price, a special gig is created
            //    whose OriginalGigId points back to the gig the commitment fee was paid for.
            //    We resolve the OriginalGigId and retry the lookup against that.
            if (!ObjectId.TryParse(gigId, out var gigOid))
                return null;

            var gig = await _dbContext.Gigs.FindAsync(gigOid);
            if (gig == null || gig.IsSpecialGig != true || string.IsNullOrEmpty(gig.OriginalGigId))
                return null;

            _logger.LogInformation(
                "GetApplicableCommitmentAsync: Direct lookup missed for SpecialGigId {GigId}. " +
                "Trying OriginalGigId {OriginalGigId} for ClientId {ClientId}.",
                gigId, gig.OriginalGigId, clientId);

            return await _dbContext.BookingCommitments
                .FirstOrDefaultAsync(bc => bc.ClientId == clientId
                                        && bc.GigId == gig.OriginalGigId
                                        && bc.Status == BookingCommitmentStatus.Completed
                                        && !bc.IsAppliedToOrder);
        }

        public async Task<bool> MarkCommitmentAppliedAsync(string commitmentId, string orderId)
        {
            if (!ObjectId.TryParse(commitmentId, out var objectId))
            {
                _logger.LogError("SECURITY: Invalid commitment ID format: {CommitmentId}. Cannot mark as applied.", commitmentId);
                return false;
            }

            var commitment = await _dbContext.BookingCommitments.FindAsync(objectId);
            if (commitment == null)
            {
                _logger.LogError("SECURITY: Cannot mark commitment as applied — not found: {CommitmentId}", commitmentId);
                return false;
            }

            if (commitment.IsAppliedToOrder)
            {
                _logger.LogError(
                    "SECURITY: Commitment {CommitmentId} already applied to order {ExistingOrderId}. Attempted reuse for order {NewOrderId}.",
                    commitmentId, commitment.AppliedToOrderId, orderId);
                return false;
            }

            commitment.IsAppliedToOrder = true;
            commitment.AppliedToOrderId = orderId;
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation(
                "Commitment {CommitmentId} marked as applied to order {OrderId}",
                commitmentId, orderId);
            return true;
        }

        public async Task<BookingCommitment?> GetByTransactionReferenceAsync(string transactionReference)
        {
            return await _dbContext.BookingCommitments
                .FirstOrDefaultAsync(bc => bc.TransactionReference == transactionReference);
        }

        public async Task<CommitmentStatusResponse> GetCommitmentStatusAsync(string clientId, string gigId)
        {
            // 1. Check if this is a CareRequest-originated special gig.
            //    These never require a commitment fee — return HasAccess=true + CommitmentNotRequired=true
            //    so the cart page knows to skip the commitment gate entirely.
            if (ObjectId.TryParse(gigId, out var gigOidCheck))
            {
                var gigCheck = await _dbContext.Gigs.FindAsync(gigOidCheck);
                if (gigCheck != null
                    && gigCheck.IsSpecialGig == true
                    && !string.IsNullOrEmpty(gigCheck.CareRequestId))
                {
                    return new CommitmentStatusResponse
                    {
                        HasAccess = true,
                        CommitmentNotRequired = true,
                        GigId = gigId,
                        CaregiverId = gigCheck.CaregiverId
                    };
                }
            }

            // Terminal order statuses — an order in one of these states is done and cannot resume.
            var terminalOrderStatuses = new[] { "Completed", "Cancelled", "Terminated" };

            // 2. Direct lookup (covers regular gigs and RegularGig-path special gigs where
            //    the client accepted — GigIdForPayment = OriginalGigId in that sub-case).
            //    Prefer an unused commitment (IsAppliedToOrder=false) over a spent one so that
            //    a client who paid a fresh commitment after a completed order gets immediate access.
            var commitment = await _dbContext.BookingCommitments
                .Where(bc => bc.ClientId == clientId
                          && bc.GigId == gigId
                          && bc.Status == BookingCommitmentStatus.Completed)
                .OrderBy(bc => bc.IsAppliedToOrder)      // false (unused) sorts before true (spent)
                .ThenByDescending(bc => bc.CompletedAt)
                .FirstOrDefaultAsync();

            if (commitment != null)
            {
                // If this commitment has already been consumed by an order, verify the order is
                // still active. If the order has reached a terminal state (Completed, Cancelled,
                // Terminated), the commitment is fully spent and the client needs a fresh one.
                if (commitment.IsAppliedToOrder)
                {
                    var hasActiveOrder = await _dbContext.ClientOrders
                        .AnyAsync(o => o.ClientId == clientId
                                    && o.GigId == gigId
                                    && o.ClientOrderStatus != null
                                    && !terminalOrderStatuses.Contains(o.ClientOrderStatus));

                    if (!hasActiveOrder)
                    {
                        // Commitment is fully spent and no active order remains.
                        // Fall through to return HasAccess=false so the frontend prompts
                        // the client to pay a new commitment fee.
                        _logger.LogInformation(
                            "GetCommitmentStatusAsync: Commitment {CommitmentId} is spent (IsAppliedToOrder=true) " +
                            "and no active order exists for ClientId: {ClientId}, GigId: {GigId}. Returning HasAccess=false.",
                            commitment.Id, clientId, gigId);
                    }
                    else
                    {
                        // Order is still in progress — commitment is tied to that active order.
                        return new CommitmentStatusResponse
                        {
                            HasAccess = true,
                            GigId = gigId,
                            CaregiverId = commitment.CaregiverId,
                            UnlockedAt = commitment.CompletedAt,
                            IsAppliedToOrder = true
                        };
                    }
                }
                else
                {
                    // Commitment is unused — client has paid but not yet placed a full order.
                    return new CommitmentStatusResponse
                    {
                        HasAccess = true,
                        GigId = gigId,
                        CaregiverId = commitment.CaregiverId,
                        UnlockedAt = commitment.CompletedAt,
                        IsAppliedToOrder = false
                    };
                }
            }

            // 3. Fallback for special gigs created via RegularGig negotiation when the caregiver
            //    accepted (GigIdForPayment = SpecialGigId, commitment was paid on OriginalGigId).
            if (ObjectId.TryParse(gigId, out var gigOid))
            {
                var gig = await _dbContext.Gigs.FindAsync(gigOid);
                if (gig != null
                    && gig.IsSpecialGig == true
                    && !string.IsNullOrEmpty(gig.OriginalGigId))
                {
                    // Same preference ordering: unused over spent, newest first.
                    var fallback = await _dbContext.BookingCommitments
                        .Where(bc => bc.ClientId == clientId
                                  && bc.GigId == gig.OriginalGigId
                                  && bc.Status == BookingCommitmentStatus.Completed)
                        .OrderBy(bc => bc.IsAppliedToOrder)
                        .ThenByDescending(bc => bc.CompletedAt)
                        .FirstOrDefaultAsync();

                    if (fallback != null)
                    {
                        if (fallback.IsAppliedToOrder)
                        {
                            var hasActiveOrder = await _dbContext.ClientOrders
                                .AnyAsync(o => o.ClientId == clientId
                                            && o.GigId == gigId
                                            && o.ClientOrderStatus != null
                                            && !terminalOrderStatuses.Contains(o.ClientOrderStatus));

                            if (!hasActiveOrder)
                            {
                                _logger.LogInformation(
                                    "GetCommitmentStatusAsync (fallback): Commitment {CommitmentId} is spent " +
                                    "and no active order exists for ClientId: {ClientId}, SpecialGigId: {GigId}. Returning HasAccess=false.",
                                    fallback.Id, clientId, gigId);
                                // Fall through to HasAccess=false below.
                            }
                            else
                            {
                                return new CommitmentStatusResponse
                                {
                                    HasAccess = true,
                                    GigId = gigId,
                                    CaregiverId = fallback.CaregiverId,
                                    UnlockedAt = fallback.CompletedAt,
                                    IsAppliedToOrder = true
                                };
                            }
                        }
                        else
                        {
                            return new CommitmentStatusResponse
                            {
                                HasAccess = true,
                                GigId = gigId,
                                CaregiverId = fallback.CaregiverId,
                                UnlockedAt = fallback.CompletedAt,
                                IsAppliedToOrder = false
                            };
                        }
                    }
                }
            }

            return new CommitmentStatusResponse
            {
                HasAccess = false,
                GigId = gigId
            };
        }

        #region Private Methods

        private async Task SendCommitmentNotificationsAsync(BookingCommitment commitment)
        {
            // Look up client and caregiver details for notification content
            var client = await _dbContext.Clients
                .FirstOrDefaultAsync(c => c.Id == ObjectId.Parse(commitment.ClientId));
            var caregiver = await _dbContext.CareGivers
                .FirstOrDefaultAsync(c => c.Id == ObjectId.Parse(commitment.CaregiverId));
            var gig = await _gigServices.GetGigAsync(commitment.GigId);

            var clientName = client != null ? $"{client.FirstName} {client.LastName}" : "A client";
            var caregiverName = caregiver != null ? $"{caregiver.FirstName} {caregiver.LastName}" : "the caregiver";
            var gigTitle = gig?.Title ?? "a gig";

            // 1. In-app notifications first — must not be blocked by email failure
            await _mediator.Send(new SendNotificationCommand(
                RecipientId: commitment.ClientId,
                SenderId: commitment.CaregiverId,
                Type: NotificationTypes.CommitmentConfirmed,
                Content: $"You've unlocked access to chat with {caregiverName} about their gig: {gigTitle}. You can now discuss care details before placing a full order.",
                Title: "Gig Access Unlocked",
                RelatedEntityId: commitment.GigId
            ));

            await _mediator.Send(new SendNotificationCommand(
                RecipientId: commitment.CaregiverId,
                SenderId: commitment.ClientId,
                Type: NotificationTypes.CommitmentConfirmed,
                Content: $"{clientName} is interested in your gig: {gigTitle}. They have paid a booking commitment fee and can now message you to discuss care details.",
                Title: "New Booking Interest",
                RelatedEntityId: commitment.GigId
            ));

            _logger.LogInformation(
                "Commitment in-app notifications sent. ClientId: {ClientId}, CaregiverId: {CaregiverId}, GigId: {GigId}",
                commitment.ClientId, commitment.CaregiverId, commitment.GigId);

            // 2. Email receipt with PDF — isolated so failure never blocks in-app notifications
            if (client != null)
            {
                try
                {
                    var receiptData = new CommitmentReceiptData
                    {
                        TransactionReference = commitment.TransactionReference,
                        FlutterwaveTransactionId = commitment.FlutterwaveTransactionId,
                        ClientName = clientName,
                        ClientEmail = client.Email,
                        CaregiverName = caregiverName,
                        GigTitle = gigTitle,
                        CommitmentFee = commitment.Amount,
                        GatewayFees = commitment.FlutterwaveFees,
                        TotalCharged = commitment.TotalCharged,
                        Currency = "NGN",
                        PaidAt = commitment.CompletedAt ?? DateTime.UtcNow
                    };

                    var pdfBytes = _receiptPdfService.GenerateCommitmentReceipt(receiptData);
                    var fileName = $"CarePro-Receipt-Commitment-{commitment.TransactionReference}.pdf";
                    var description = $"Booking Commitment Fee — {gigTitle}";

                    await _emailService.SendPaymentReceiptEmailAsync(
                        client.Email, client.FirstName, fileName, description, pdfBytes);

                    _logger.LogInformation("Commitment receipt email sent to {Email} for TxRef: {TxRef}",
                        client.Email, commitment.TransactionReference);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Failed to send commitment receipt email for TxRef: {TxRef}. In-app notifications were already delivered.",
                        commitment.TransactionReference);
                }
            }
        }

        private decimal CalculateFlutterwaveFees(decimal amount)
        {
            decimal fee = amount * FLUTTERWAVE_FEE_RATE;
            return Math.Min(Math.Round(fee, 2), FLUTTERWAVE_FEE_CAP);
        }

        private string? ExtractPaymentLink(string flutterwaveResponse)
        {
            try
            {
                var response = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(flutterwaveResponse);
                if (response.TryGetProperty("data", out var data) &&
                    data.TryGetProperty("link", out var link))
                {
                    return link.GetString();
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        #endregion

        public async Task<bool> InvalidateCommitmentForOrderAsync(string orderId)
        {
            if (string.IsNullOrWhiteSpace(orderId))
            {
                _logger.LogWarning("InvalidateCommitmentForOrderAsync called with null/empty orderId");
                return false;
            }

            var commitment = await _dbContext.BookingCommitments
                .FirstOrDefaultAsync(bc => bc.AppliedToOrderId == orderId
                                        && bc.IsAppliedToOrder);

            if (commitment == null)
            {
                _logger.LogInformation("No applied commitment found for order {OrderId} — nothing to invalidate.", orderId);
                return false;
            }

            commitment.Status = BookingCommitmentStatus.Expired;
            commitment.IsAppliedToOrder = false;
            commitment.AppliedToOrderId = null;

            _dbContext.BookingCommitments.Update(commitment);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation(
                "Commitment {CommitmentId} invalidated due to order {OrderId} cancellation. Client must pay a new booking fee.",
                commitment.Id, orderId);

            return true;
        }

        public async Task ResetAmountMismatchAsync(string transactionReference, string adminNote)
        {
            var commitment = await _dbContext.BookingCommitments
                .FirstOrDefaultAsync(bc => bc.TransactionReference == transactionReference);

            if (commitment == null) return;

            commitment.Status = BookingCommitmentStatus.Pending;
            commitment.ErrorMessage = adminNote;

            _dbContext.BookingCommitments.Update(commitment);
            await _dbContext.SaveChangesAsync();

            _logger.LogWarning(
                "ADMIN OVERRIDE: AmountMismatch reset on commitment TxRef={TxRef}. Note: {Note}",
                transactionReference, adminNote);
        }

        public async Task<List<BookingCommitmentListItem>> GetClientCommitmentsAsync(string clientId)
        {
            var commitments = await _dbContext.BookingCommitments
                .Where(c => c.ClientId == clientId)
                .OrderByDescending(c => c.CreatedAt)
                .ToListAsync();

            return commitments.Select(c => new BookingCommitmentListItem
            {
                Id = c.Id.ToString(),
                GigId = c.GigId,
                CaregiverId = c.CaregiverId,
                Amount = c.Amount,
                Status = c.Status.ToString().ToLower(),
                TransactionReference = c.TransactionReference,
                CreatedAt = c.CreatedAt,
                CompletedAt = c.CompletedAt,
                IsAppliedToOrder = c.IsAppliedToOrder,
                AppliedToOrderId = c.AppliedToOrderId
            }).ToList();
        }

        public async Task<Result<CancelCommitmentResponse>> CancelCommitmentAsync(string gigId, string clientId)
        {
            if (string.IsNullOrWhiteSpace(gigId))
                return Result<CancelCommitmentResponse>.Failure(new List<string> { "GigId is required." });

            if (string.IsNullOrWhiteSpace(clientId))
                return Result<CancelCommitmentResponse>.Failure(new List<string> { "Client authorization required." });

            // Resolve the actual gig ID to look up — handles OriginalGigId fallback for special/negotiated gigs
            var resolvedGigId = gigId;
            if (ObjectId.TryParse(gigId, out var gigOid))
            {
                var gig = await _dbContext.Gigs.FindAsync(gigOid);
                if (gig?.IsSpecialGig == true && !string.IsNullOrEmpty(gig.OriginalGigId))
                {
                    _logger.LogInformation(
                        "CancelCommitmentAsync: SpecialGig {GigId} resolved to OriginalGigId {OriginalGigId} for ClientId {ClientId}.",
                        gigId, gig.OriginalGigId, clientId);
                    resolvedGigId = gig.OriginalGigId;
                }
            }

            // Find the relevant commitment record — look for any commitment for this client+gig
            var commitment = await _dbContext.BookingCommitments
                .FirstOrDefaultAsync(bc => bc.ClientId == clientId
                                        && (bc.GigId == resolvedGigId || bc.GigId == gigId)
                                        && bc.Status == BookingCommitmentStatus.Completed);

            if (commitment == null)
            {
                // Check if one exists but is already in a terminal/non-cancellable state
                var anyCommitment = await _dbContext.BookingCommitments
                    .FirstOrDefaultAsync(bc => bc.ClientId == clientId
                                            && (bc.GigId == resolvedGigId || bc.GigId == gigId));

                if (anyCommitment != null)
                {
                    _logger.LogWarning(
                        "CancelCommitmentAsync: Commitment for ClientId {ClientId}, GigId {GigId} is in non-cancellable state {Status}.",
                        clientId, gigId, anyCommitment.Status);
                    return Result<CancelCommitmentResponse>.Failure(new List<string>
                    {
                        $"This commitment cannot be cancelled. Current status: {anyCommitment.Status}."
                    });
                }

                _logger.LogWarning(
                    "CancelCommitmentAsync: No active commitment found for ClientId {ClientId}, GigId {GigId}.",
                    clientId, gigId);
                return Result<CancelCommitmentResponse>.Failure(new List<string>
                {
                    "No active booking commitment found for this gig."
                });
            }

            // GUARD: Cannot cancel if the commitment has already been consumed by a full gig payment
            if (commitment.IsAppliedToOrder)
            {
                _logger.LogWarning(
                    "CancelCommitmentAsync blocked — commitment {CommitmentId} already applied to order {OrderId}. ClientId: {ClientId}.",
                    commitment.Id, commitment.AppliedToOrderId, clientId);
                return Result<CancelCommitmentResponse>.Failure(new List<string>
                {
                    "This commitment has already been applied to an active order and cannot be cancelled. " +
                    "If you want to cancel the service, please use the order cancellation option."
                });
            }

            // CONCURRENCY GUARD: Re-read the commitment fresh from DB before mutating
            // to catch a race where full gig payment ran concurrently and just marked it applied
            var freshCommitment = await _dbContext.BookingCommitments.FindAsync(commitment.Id);
            if (freshCommitment == null || freshCommitment.IsAppliedToOrder
                || freshCommitment.Status != BookingCommitmentStatus.Completed)
            {
                _logger.LogWarning(
                    "CancelCommitmentAsync: Concurrency guard triggered for commitment {CommitmentId}. " +
                    "State changed between read and write. ClientId: {ClientId}.",
                    commitment.Id, clientId);
                return Result<CancelCommitmentResponse>.Failure(new List<string>
                {
                    "The commitment state changed while processing your request. " +
                    "It may have just been applied to an order. Please check your order status."
                });
            }

            // Mark as cancelled — all three gates (chat, gig payment, admin order creation) check
            // for Status == Completed so they will block immediately after this save
            freshCommitment.Status = BookingCommitmentStatus.CancelledByClient;
            _dbContext.BookingCommitments.Update(freshCommitment);
            await _dbContext.SaveChangesAsync();

            var cancelledAt = DateTime.UtcNow;

            _logger.LogInformation(
                "Commitment {CommitmentId} cancelled by client {ClientId} for gig {GigId}. Fee forfeited: ₦{Amount}.",
                freshCommitment.Id, clientId, gigId, freshCommitment.Amount);

            // Fetch gig title for notification messages
            var gigDetails = await _gigServices.GetGigAsync(resolvedGigId != gigId ? resolvedGigId : gigId);
            var gigTitle = gigDetails?.Title ?? "the gig";

            // Notify the client: confirmation + fee forfeiture reminder
            try
            {
                await _mediator.Send(new SendNotificationCommand(
                    RecipientId: clientId,
                    SenderId: clientId,
                    Type: NotificationTypes.CommitmentCancelledByClient,
                    Content: $"You have cancelled your booking commitment for \"{gigTitle}\". " +
                             $"Your ₦{freshCommitment.Amount:N0} commitment fee is non-refundable. " +
                             "You will need to pay a new commitment fee to regain access.",
                    Title: "Booking Commitment Cancelled",
                    RelatedEntityId: freshCommitment.Id.ToString()));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send cancellation confirmation notification to client {ClientId}", clientId);
            }

            // Notify the caregiver: their committed client has withdrawn
            try
            {
                await _mediator.Send(new SendNotificationCommand(
                    RecipientId: freshCommitment.CaregiverId,
                    SenderId: clientId,
                    Type: NotificationTypes.CommitmentCancelledAlert,
                    Content: $"A client has cancelled their booking commitment for \"{gigTitle}\". " +
                             "They will no longer have access to chat with you for this gig unless they pay a new commitment fee.",
                    Title: "Booking Commitment Withdrawn",
                    RelatedEntityId: freshCommitment.Id.ToString()));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send cancellation alert notification to caregiver {CaregiverId}", freshCommitment.CaregiverId);
            }

            return Result<CancelCommitmentResponse>.Success(new CancelCommitmentResponse
            {
                CommitmentId = freshCommitment.Id.ToString(),
                GigId = gigId,
                Status = "cancelled_by_client",
                CancelledAt = cancelledAt,
                Message = $"Your booking commitment for \"{gigTitle}\" has been cancelled. " +
                          $"The ₦{freshCommitment.Amount:N0} fee is non-refundable."
            });
        }
    }
}
