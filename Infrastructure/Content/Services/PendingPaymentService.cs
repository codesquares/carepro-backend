using Application.DTOs;
using Application.Interfaces;
using Application.Interfaces.Content;
using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;

namespace Infrastructure.Content.Services
{
    public class PendingPaymentService : IPendingPaymentService
    {
        private readonly CareProDbContext _dbContext;
        private readonly IGigServices _gigServices;
        private readonly IClientOrderService _clientOrderService;
        private readonly FlutterwaveService _flutterwaveService;
        private readonly ISubscriptionService _subscriptionService;
        private readonly ILogger<PendingPaymentService> _logger;
        private readonly IConfiguration _configuration;

        // Service charge rate (10%)
        private const decimal SERVICE_CHARGE_RATE = 0.10m;
        
        // Flutterwave local card fee rate (1.4%, capped at 2000 NGN)
        private const decimal FLUTTERWAVE_FEE_RATE = 0.014m;
        private const decimal FLUTTERWAVE_FEE_CAP = 2000m;
        private readonly IBillingRecordService _billingRecordService;

        public PendingPaymentService(
            CareProDbContext dbContext,
            IGigServices gigServices,
            IClientOrderService clientOrderService,
            FlutterwaveService flutterwaveService,
            ISubscriptionService subscriptionService,
            ILogger<PendingPaymentService> logger,
            IConfiguration configuration,
            IBillingRecordService billingRecordService)
        {
            _dbContext = dbContext;
            _gigServices = gigServices;
            _clientOrderService = clientOrderService;
            _flutterwaveService = flutterwaveService;
            _subscriptionService = subscriptionService;
            _logger = logger;
            _configuration = configuration;
            _billingRecordService = billingRecordService;
        }

        public async Task<Result<PendingPaymentResponse>> CreatePendingPaymentAsync(InitiatePaymentRequest request, string clientId)
        {
            var errors = new List<string>();

            // Validate request
            if (string.IsNullOrEmpty(request.GigId))
                errors.Add("GigId is required.");
            
            if (string.IsNullOrEmpty(request.ServiceType))
                errors.Add("ServiceType is required.");
            
            if (!new[] { "one-time", "monthly" }.Contains(request.ServiceType?.ToLower()))
                errors.Add("ServiceType must be 'one-time' or 'monthly'.");
            
            if (request.FrequencyPerWeek < 1 || request.FrequencyPerWeek > 7)
                errors.Add("FrequencyPerWeek must be between 1 and 7.");
            
            if (string.IsNullOrEmpty(request.Email))
                errors.Add("Email is required.");
            
            if (string.IsNullOrEmpty(request.RedirectUrl))
                errors.Add("RedirectUrl is required.");

            if (errors.Any())
                return Result<PendingPaymentResponse>.Failure(errors);

            // Fetch gig to get base price
            var gig = await _gigServices.GetGigAsync(request.GigId);
            if (gig == null)
            {
                return Result<PendingPaymentResponse>.Failure(new List<string> { "Gig not found." });
            }

            // Validate gig is active
            if (gig.Status?.ToLower() != "active" && gig.Status?.ToLower() != "published")
            {
                return Result<PendingPaymentResponse>.Failure(new List<string> { "This gig is not currently available for purchase." });
            }

            // Calculate amounts
            decimal basePrice = gig.Price;
            decimal orderFee = CalculateOrderFee(basePrice, request.ServiceType?.ToLower() ?? "one-time", request.FrequencyPerWeek);
            decimal serviceCharge = Math.Round(orderFee * SERVICE_CHARGE_RATE, 2);
            decimal flutterwaveFees = CalculateFlutterwaveFees(orderFee + serviceCharge);
            decimal totalAmount = orderFee + serviceCharge + flutterwaveFees;

            // Generate unique transaction reference
            string transactionReference = $"CAREPRO-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8].ToUpper()}";

            // Create pending payment record
            var pendingPayment = new PendingPayment
            {
                Id = ObjectId.GenerateNewId(),
                TransactionReference = transactionReference,
                GigId = request.GigId,
                ClientId = clientId,
                Email = request.Email,
                ServiceType = request.ServiceType?.ToLower() ?? "one-time",
                FrequencyPerWeek = request.FrequencyPerWeek,
                BasePrice = basePrice,
                OrderFee = orderFee,
                ServiceCharge = serviceCharge,
                FlutterwaveFees = flutterwaveFees,
                TotalAmount = totalAmount,
                Currency = "NGN",
                RedirectUrl = request.RedirectUrl,
                Status = PendingPaymentStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };

            // Call Flutterwave to initiate payment
            try
            {
                var flutterwaveResponse = await _flutterwaveService.InitiatePayment(
                    totalAmount,
                    request.Email,
                    "NGN",
                    transactionReference,
                    request.RedirectUrl
                );

                // Parse Flutterwave response to extract payment link
                var paymentLink = ExtractPaymentLink(flutterwaveResponse);
                if (string.IsNullOrEmpty(paymentLink))
                {
                    _logger.LogError("Failed to get payment link from Flutterwave. Response: {Response}", flutterwaveResponse);
                    return Result<PendingPaymentResponse>.Failure(new List<string> { "Failed to initialize payment with Flutterwave." });
                }

                pendingPayment.PaymentLink = paymentLink;

                // Save pending payment to database
                _dbContext.PendingPayments.Add(pendingPayment);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation(
                    "Payment initiated. TxRef: {TxRef}, GigId: {GigId}, Amount: {Amount}",
                    transactionReference, request.GigId, totalAmount);

                return Result<PendingPaymentResponse>.Success(new PendingPaymentResponse
                {
                    Success = true,
                    Message = "Payment initiated successfully.",
                    TransactionReference = transactionReference,
                    PaymentLink = paymentLink,
                    Breakdown = new PaymentBreakdown
                    {
                        BasePrice = basePrice,
                        ServiceType = request.ServiceType?.ToLower() ?? "one-time",
                        FrequencyPerWeek = request.FrequencyPerWeek,
                        OrderFee = orderFee,
                        ServiceCharge = serviceCharge,
                        FlutterwaveFees = flutterwaveFees,
                        TotalAmount = totalAmount,
                        Currency = "NGN"
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initiating Flutterwave payment for GigId: {GigId}", request.GigId);
                return Result<PendingPaymentResponse>.Failure(new List<string> { "An error occurred while processing payment." });
            }
        }

        public async Task<PendingPayment?> GetByTransactionReferenceAsync(string transactionReference)
        {
            return await _dbContext.PendingPayments
                .FirstOrDefaultAsync(p => p.TransactionReference == transactionReference);
        }

        public async Task<Result<PendingPayment>> CompletePaymentAsync(string transactionReference, string flutterwaveTransactionId, decimal paidAmount)
        {
            var pendingPayment = await GetByTransactionReferenceAsync(transactionReference);
            if (pendingPayment == null)
            {
                _logger.LogWarning("Payment completion attempted for unknown TxRef: {TxRef}", transactionReference);
                return Result<PendingPayment>.Failure(new List<string> { "Payment record not found." });
            }

            // IDEMPOTENCY GUARD: If payment is already completed, return success (webhook replay protection)
            if (pendingPayment.Status == PendingPaymentStatus.Completed)
            {
                _logger.LogWarning(
                    "SECURITY: Duplicate CompletePayment attempt for TxRef: {TxRef}. Already completed at {CompletedAt}. Returning existing result.",
                    transactionReference, pendingPayment.CompletedAt);
                return Result<PendingPayment>.Success(pendingPayment);
            }

            // Reject if already explicitly failed or flagged as amount mismatch
            if (pendingPayment.Status == PendingPaymentStatus.AmountMismatch)
            {
                _logger.LogWarning(
                    "SECURITY: CompletePayment retry for previously flagged TxRef: {TxRef} (AmountMismatch). Blocked.",
                    transactionReference);
                return Result<PendingPayment>.Failure(new List<string> { "This payment was previously flagged for amount mismatch." });
            }

            // CRITICAL SECURITY CHECK: Verify the paid amount matches expected amount
            // Allow small tolerance for rounding (0.01)
            if (Math.Abs(paidAmount - pendingPayment.TotalAmount) > 0.01m)
            {
                _logger.LogCritical(
                    "AMOUNT MISMATCH DETECTED! TxRef: {TxRef}, Expected: {Expected}, Paid: {Paid}. Possible tampering attempt.",
                    transactionReference, pendingPayment.TotalAmount, paidAmount);
                
                pendingPayment.Status = PendingPaymentStatus.AmountMismatch;
                pendingPayment.ErrorMessage = $"Amount mismatch. Expected: {pendingPayment.TotalAmount}, Paid: {paidAmount}";
                await _dbContext.SaveChangesAsync();
                
                return Result<PendingPayment>.Failure(new List<string> { "Payment amount does not match. This incident has been logged." });
            }

            // Create the client order
            var orderResult = await _clientOrderService.CreateClientOrderAsync(new AddClientOrderRequest
            {
                ClientId = pendingPayment.ClientId,
                GigId = pendingPayment.GigId,
                PaymentOption = pendingPayment.ServiceType,
                Amount = (int)pendingPayment.TotalAmount,
                TransactionId = flutterwaveTransactionId
            });

            if (!orderResult.IsSuccess)
            {
                _logger.LogError("Failed to create client order for TxRef: {TxRef}. Errors: {Errors}",
                    transactionReference, string.Join(", ", orderResult.Errors));
                
                pendingPayment.Status = PendingPaymentStatus.Failed;
                pendingPayment.ErrorMessage = "Payment received but failed to create order. Please contact support.";
                await _dbContext.SaveChangesAsync();
                
                return Result<PendingPayment>.Failure(new List<string> { "Failed to create order after payment." });
            }

            // Update pending payment as completed
            pendingPayment.Status = PendingPaymentStatus.Completed;
            pendingPayment.FlutterwaveTransactionId = flutterwaveTransactionId;
            pendingPayment.CompletedAt = DateTime.UtcNow;
            pendingPayment.ClientOrderId = orderResult.Value?.Id;
            
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation(
                "Payment completed successfully. TxRef: {TxRef}, FlwTxId: {FlwTxId}, OrderId: {OrderId}",
                transactionReference, flutterwaveTransactionId, orderResult.Value?.Id);

            // ── Create BillingRecord for this payment ──
            try
            {
                var gig = await _gigServices.GetGigAsync(pendingPayment.GigId);
                var caregiverId = gig?.CaregiverId ?? string.Empty;

                await _billingRecordService.CreateBillingRecordAsync(
                    orderId: orderResult.Value?.Id ?? string.Empty,
                    clientId: pendingPayment.ClientId,
                    caregiverId: caregiverId,
                    gigId: pendingPayment.GigId,
                    serviceType: pendingPayment.ServiceType,
                    frequencyPerWeek: pendingPayment.FrequencyPerWeek,
                    amountPaid: pendingPayment.TotalAmount,
                    orderFee: pendingPayment.OrderFee,
                    serviceCharge: pendingPayment.ServiceCharge,
                    gatewayFees: pendingPayment.FlutterwaveFees,
                    paymentTransactionId: flutterwaveTransactionId,
                    billingCycleNumber: 1
                );

                _logger.LogInformation(
                    "BillingRecord created for OrderId: {OrderId}, TxRef: {TxRef}",
                    orderResult.Value?.Id, transactionReference);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create BillingRecord for TxRef: {TxRef}. Payment was successful.", transactionReference);
            }

            // For recurring service types (monthly), create a subscription
            if (pendingPayment.ServiceType != "one-time")
            {
                await CreateSubscriptionForRecurringPaymentAsync(pendingPayment, flutterwaveTransactionId, orderResult.Value?.Id);
            }

            return Result<PendingPayment>.Success(pendingPayment);
        }

        public async Task<Result<PendingPayment>> FailPaymentAsync(string transactionReference, string errorMessage)
        {
            var pendingPayment = await GetByTransactionReferenceAsync(transactionReference);
            if (pendingPayment == null)
            {
                return Result<PendingPayment>.Failure(new List<string> { "Payment record not found." });
            }

            pendingPayment.Status = PendingPaymentStatus.Failed;
            pendingPayment.ErrorMessage = errorMessage;
            await _dbContext.SaveChangesAsync();

            _logger.LogWarning("Payment failed. TxRef: {TxRef}, Error: {Error}", transactionReference, errorMessage);

            return Result<PendingPayment>.Success(pendingPayment);
        }

        public async Task<Result<PaymentStatusResponse>> GetPaymentStatusAsync(string transactionReference)
        {
            var pendingPayment = await GetByTransactionReferenceAsync(transactionReference);
            if (pendingPayment == null)
            {
                return Result<PaymentStatusResponse>.Failure(new List<string> { "Payment not found." });
            }

            return Result<PaymentStatusResponse>.Success(new PaymentStatusResponse
            {
                Success = pendingPayment.Status == PendingPaymentStatus.Completed,
                Status = pendingPayment.Status.ToString().ToLower(),
                TransactionReference = pendingPayment.TransactionReference,
                FlutterwaveTransactionId = pendingPayment.FlutterwaveTransactionId,
                PaymentDate = pendingPayment.CompletedAt,
                ClientOrderId = pendingPayment.ClientOrderId,
                Breakdown = new PaymentBreakdown
                {
                    BasePrice = pendingPayment.BasePrice,
                    ServiceType = pendingPayment.ServiceType,
                    FrequencyPerWeek = pendingPayment.FrequencyPerWeek,
                    OrderFee = pendingPayment.OrderFee,
                    ServiceCharge = pendingPayment.ServiceCharge,
                    FlutterwaveFees = pendingPayment.FlutterwaveFees,
                    TotalAmount = pendingPayment.TotalAmount,
                    Currency = pendingPayment.Currency
                },
                ErrorMessage = pendingPayment.ErrorMessage
            });
        }

        #region Private Methods

        /// <summary>
        /// Creates a subscription after a successful initial payment for a recurring service.
        /// Attempts to extract the card token from Flutterwave for future charges.
        /// </summary>
        private async Task CreateSubscriptionForRecurringPaymentAsync(
            PendingPayment payment, string flutterwaveTransactionId, string? orderId)
        {
            try
            {
                // Try to extract card token for recurring charges
                string? paymentToken = null, cardLastFour = null, cardBrand = null, cardExpiry = null;

                var verification = await _flutterwaveService.VerifyAndExtractTokenAsync(flutterwaveTransactionId);
                if (verification != null)
                {
                    paymentToken = verification.PaymentToken;
                    cardLastFour = verification.CardLastFour;
                    cardBrand = verification.CardBrand;
                    cardExpiry = verification.CardExpiry;
                }

                // Get caregiver ID from the gig
                var gig = await _gigServices.GetGigAsync(payment.GigId);
                var caregiverId = gig?.CaregiverId ?? string.Empty;

                var subscriptionResult = await _subscriptionService.CreateSubscriptionAsync(new Application.DTOs.CreateSubscriptionRequest
                {
                    ClientId = payment.ClientId,
                    CaregiverId = caregiverId,
                    GigId = payment.GigId,
                    OrderId = orderId ?? payment.ClientOrderId ?? string.Empty,
                    Email = payment.Email,
                    BillingCycle = payment.ServiceType, // "monthly" (only recurring type)
                    FrequencyPerWeek = payment.FrequencyPerWeek,
                    PricePerVisit = payment.BasePrice,
                    RecurringAmount = payment.TotalAmount,
                    PriceBreakdown = new Application.DTOs.SubscriptionPriceBreakdownDTO
                    {
                        BasePrice = payment.BasePrice,
                        FrequencyPerWeek = payment.FrequencyPerWeek,
                        OrderFee = payment.OrderFee,
                        ServiceCharge = payment.ServiceCharge,
                        GatewayFees = payment.FlutterwaveFees,
                        TotalAmount = payment.TotalAmount
                    },
                    Currency = payment.Currency,
                    FlutterwavePaymentToken = paymentToken,
                    CardLastFour = cardLastFour,
                    CardBrand = cardBrand,
                    CardExpiry = cardExpiry
                });

                if (subscriptionResult.IsSuccess)
                {
                    _logger.LogInformation(
                        "Subscription {SubscriptionId} created for {ServiceType} payment. Token captured: {HasToken}",
                        subscriptionResult.Value?.Id, payment.ServiceType, paymentToken != null);
                }
                else
                {
                    _logger.LogWarning(
                        "Failed to create subscription for TxRef {TxRef}: {Errors}",
                        payment.TransactionReference, string.Join(", ", subscriptionResult.Errors));
                }
            }
            catch (Exception ex)
            {
                // Subscription creation failure should NOT fail the payment
                _logger.LogError(ex,
                    "Error creating subscription for completed payment TxRef {TxRef}. Payment was successful.",
                    payment.TransactionReference);
            }
        }

        private decimal CalculateOrderFee(decimal basePrice, string serviceType, int frequencyPerWeek)
        {
            return serviceType switch
            {
                "one-time" => basePrice,
                "monthly" => basePrice * frequencyPerWeek * 4, // basePrice × visits/week × 4 weeks
                _ => basePrice
            };
        }

        private decimal CalculateFlutterwaveFees(decimal amount)
        {
            // Flutterwave local card fee: 1.4% capped at 2000 NGN
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
