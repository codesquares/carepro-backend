using Application.DTOs;
using Application.Interfaces;
using Application.Interfaces.Content;
using Application.Interfaces.Email;
using Domain.Entities;
using Infrastructure.Content.Data;
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
        private readonly INotificationService _notificationService;
        private readonly IEmailService _emailService;
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
            INotificationService notificationService,
            IEmailService emailService,
            ILogger<BookingCommitmentService> logger)
        {
            _dbContext = dbContext;
            _gigServices = gigServices;
            _flutterwaveService = flutterwaveService;
            _notificationService = notificationService;
            _emailService = emailService;
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
            // Block commitment if the client already has an active (non-completed) order for this gig,
            // because the commitment fee would be pointless — they can't pay for the gig again.
            var existingActiveOrder = await _dbContext.ClientOrders
                .FirstOrDefaultAsync(o => o.ClientId == clientId
                                       && o.GigId == request.GigId
                                       && o.ClientOrderStatus != "Completed");

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

            // Check if client already has an active (completed) commitment for this gig
            var existingCompleted = await _dbContext.BookingCommitments
                .FirstOrDefaultAsync(bc => bc.ClientId == clientId
                                        && bc.GigId == request.GigId
                                        && bc.Status == BookingCommitmentStatus.Completed);

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
                    _logger.LogInformation(
                        "Expired stale commitment. TxRef: {TxRef}, Age: {AgeHours}h",
                        existingPending.TransactionReference, (int)age.TotalHours);
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
            return await _dbContext.BookingCommitments
                .AnyAsync(bc => bc.ClientId == clientId
                             && bc.GigId == gigId
                             && bc.Status == BookingCommitmentStatus.Completed);
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
            return await _dbContext.BookingCommitments
                .FirstOrDefaultAsync(bc => bc.ClientId == clientId
                                        && bc.GigId == gigId
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
            var commitment = await _dbContext.BookingCommitments
                .FirstOrDefaultAsync(bc => bc.ClientId == clientId
                                        && bc.GigId == gigId
                                        && bc.Status == BookingCommitmentStatus.Completed);

            if (commitment == null)
            {
                return new CommitmentStatusResponse
                {
                    HasAccess = false,
                    GigId = gigId
                };
            }

            return new CommitmentStatusResponse
            {
                HasAccess = true,
                GigId = gigId,
                CaregiverId = commitment.CaregiverId,
                UnlockedAt = commitment.CompletedAt,
                IsAppliedToOrder = commitment.IsAppliedToOrder
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

            // 1. Send receipt email to client
            if (client != null)
            {
                await _emailService.SendPaymentConfirmationEmailAsync(
                    client.Email,
                    client.FirstName,
                    commitment.Amount,
                    $"Booking Commitment Fee — {gigTitle}",
                    commitment.FlutterwaveTransactionId ?? commitment.TransactionReference
                );
            }

            // 2. Notify client: "You now have access to chat with [Caregiver] about [Gig]"
            await _notificationService.CreateNotificationAsync(
                recipientId: commitment.ClientId,
                senderId: commitment.CaregiverId,
                type: NotificationTypes.CommitmentConfirmed,
                content: $"You've unlocked access to chat with {caregiverName} about their gig: {gigTitle}. You can now discuss care details before placing a full order.",
                Title: "Gig Access Unlocked",
                relatedEntityId: commitment.GigId
            );

            // 3. Notify caregiver: "A client is interested in your gig"
            await _notificationService.CreateNotificationAsync(
                recipientId: commitment.CaregiverId,
                senderId: commitment.ClientId,
                type: NotificationTypes.CommitmentConfirmed,
                content: $"{clientName} is interested in your gig: {gigTitle}. They have paid a booking commitment fee and can now message you to discuss care details.",
                Title: "New Booking Interest",
                relatedEntityId: commitment.GigId
            );

            _logger.LogInformation(
                "Commitment notifications sent. ClientId: {ClientId}, CaregiverId: {CaregiverId}, GigId: {GigId}",
                commitment.ClientId, commitment.CaregiverId, commitment.GigId);
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
    }
}
