using Application.Commands;
using Application.DTOs;
using Application.Interfaces.Content;
using Domain.Entities;
using Infrastructure.Content.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;

namespace Infrastructure.Content.Services
{
    public class SubscriptionService : ISubscriptionService
    {
        private readonly CareProDbContext _dbContext;
        private readonly IClientOrderService _clientOrderService;
        private readonly FlutterwaveService _flutterwaveService;
        private readonly IMediator _mediator;
        private readonly ILogger<SubscriptionService> _logger;
        private readonly IConfiguration _configuration;
        private readonly IBillingRecordService _billingRecordService;

        // Same fee structure as PendingPaymentService
        private const decimal SERVICE_CHARGE_RATE = 0.10m;
        private const decimal FLUTTERWAVE_FEE_RATE = 0.014m;
        private const decimal FLUTTERWAVE_FEE_CAP = 2000m;

        public SubscriptionService(
            CareProDbContext dbContext,
            IClientOrderService clientOrderService,
            FlutterwaveService flutterwaveService,
            IMediator mediator,
            ILogger<SubscriptionService> logger,
            IConfiguration configuration,
            IBillingRecordService billingRecordService)
        {
            _dbContext = dbContext;
            _clientOrderService = clientOrderService;
            _flutterwaveService = flutterwaveService;
            _mediator = mediator;
            _logger = logger;
            _configuration = configuration;
            _billingRecordService = billingRecordService;
        }

        // ══════════════════════════════════════════
        //  SUBSCRIPTION LIFECYCLE
        // ══════════════════════════════════════════

        public async Task<Result<SubscriptionDTO>> CreateSubscriptionAsync(CreateSubscriptionRequest request)
        {
            var errors = new List<string>();

            if (string.IsNullOrEmpty(request.ClientId)) errors.Add("ClientId is required.");
            if (string.IsNullOrEmpty(request.CaregiverId)) errors.Add("CaregiverId is required.");
            if (string.IsNullOrEmpty(request.GigId)) errors.Add("GigId is required.");
            if (string.IsNullOrEmpty(request.OrderId)) errors.Add("OrderId is required.");
            if (request.BillingCycle != "monthly")
                errors.Add("BillingCycle must be 'monthly'.");
            if (request.RecurringAmount <= 0) errors.Add("RecurringAmount must be positive.");

            if (errors.Any())
                return Result<SubscriptionDTO>.Failure(errors);

            // Check for existing active subscription for same client+gig
            var existing = await _dbContext.Subscriptions
                .FirstOrDefaultAsync(s =>
                    s.ClientId == request.ClientId &&
                    s.GigId == request.GigId &&
                    (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.PastDue));

            if (existing != null)
            {
                return Result<SubscriptionDTO>.Failure(new List<string>
                {
                    "An active subscription already exists for this service. Cancel the existing one first."
                });
            }

            var now = DateTime.UtcNow;
            var periodEnd = now.AddDays(30); // Monthly billing only

            var subscription = new Subscription
            {
                Id = ObjectId.GenerateNewId().ToString(),
                ClientId = request.ClientId,
                CaregiverId = request.CaregiverId,
                GigId = request.GigId,
                OriginalOrderId = request.OrderId,
                Email = request.Email,
                BillingCycle = request.BillingCycle,
                FrequencyPerWeek = request.FrequencyPerWeek,
                PricePerVisit = request.PricePerVisit,
                RecurringAmount = request.RecurringAmount,
                PriceBreakdown = new SubscriptionPriceBreakdown
                {
                    BasePrice = request.PriceBreakdown.BasePrice,
                    FrequencyPerWeek = request.PriceBreakdown.FrequencyPerWeek,
                    OrderFee = request.PriceBreakdown.OrderFee,
                    ServiceCharge = request.PriceBreakdown.ServiceCharge,
                    GatewayFees = request.PriceBreakdown.GatewayFees,
                    TotalAmount = request.PriceBreakdown.TotalAmount
                },
                Currency = request.Currency,
                Status = SubscriptionStatus.Active,
                CurrentPeriodStart = now,
                CurrentPeriodEnd = periodEnd,
                NextChargeDate = periodEnd,
                BillingCyclesCompleted = 1, // Initial payment counts as first cycle
                AutoRenew = true,
                FlutterwavePaymentToken = request.FlutterwavePaymentToken,
                CardLastFour = request.CardLastFour,
                CardBrand = request.CardBrand,
                CardExpiry = request.CardExpiry,
                CreatedAt = now,
                UpdatedAt = now,
                PaymentHistory = new List<SubscriptionPaymentRecord>
                {
                    new SubscriptionPaymentRecord
                    {
                        Id = ObjectId.GenerateNewId().ToString(),
                        TransactionReference = $"INIT-{request.OrderId}",
                        Amount = request.RecurringAmount,
                        Currency = request.Currency,
                        Status = "successful",
                        BillingCycleNumber = 1,
                        AttemptedAt = now,
                        CompletedAt = now,
                        ClientOrderId = request.OrderId
                    }
                }
            };

            _dbContext.Subscriptions.Add(subscription);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation(
                "Subscription created: {SubscriptionId} for Client {ClientId}, Gig {GigId}, Cycle: {Cycle}, Amount: {Amount}",
                subscription.Id, request.ClientId, request.GigId, request.BillingCycle, request.RecurringAmount);

            // Notify client
            await _mediator.Send(new SendNotificationCommand(
                request.ClientId,
                "system",
                NotificationTypes.SubscriptionCreated,
                $"Your {request.BillingCycle} subscription has been activated. Next charge: {periodEnd:MMM dd, yyyy}.",
                "Subscription Activated",
                subscription.Id));

            return Result<SubscriptionDTO>.Success(MapToDTO(subscription));
        }

        public async Task<SubscriptionDTO?> GetSubscriptionByIdAsync(string subscriptionId)
        {
            var subscription = await _dbContext.Subscriptions
                .FirstOrDefaultAsync(s => s.Id == subscriptionId);
            return subscription != null ? MapToDTO(subscription) : null;
        }

        public async Task<SubscriptionDTO?> GetSubscriptionByOrderIdAsync(string orderId)
        {
            var subscription = await _dbContext.Subscriptions
                .FirstOrDefaultAsync(s => s.OriginalOrderId == orderId);
            return subscription != null ? MapToDTO(subscription) : null;
        }

        public async Task<List<SubscriptionDTO>> GetClientSubscriptionsAsync(string clientId)
        {
            var subscriptions = await _dbContext.Subscriptions
                .Where(s => s.ClientId == clientId)
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();
            return subscriptions.Select(MapToDTO).ToList();
        }

        public async Task<List<SubscriptionDTO>> GetCaregiverSubscriptionsAsync(string caregiverId)
        {
            var subscriptions = await _dbContext.Subscriptions
                .Where(s => s.CaregiverId == caregiverId)
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();
            return subscriptions.Select(MapToDTO).ToList();
        }

        public async Task<ClientSubscriptionSummary> GetClientSubscriptionSummaryAsync(string clientId)
        {
            var subscriptions = await _dbContext.Subscriptions
                .Where(s => s.ClientId == clientId)
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();

            var active = subscriptions
                .Where(s => s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.PendingCancellation)
                .ToList();

            var nextPayment = active
                .Where(s => s.NextChargeDate.HasValue && s.Status == SubscriptionStatus.Active)
                .OrderBy(s => s.NextChargeDate)
                .FirstOrDefault();

            return new ClientSubscriptionSummary
            {
                TotalActiveSubscriptions = active.Count,
                TotalMonthlySpend = active.Sum(s => s.RecurringAmount),
                NextPaymentDate = nextPayment?.NextChargeDate,
                NextPaymentAmount = nextPayment?.RecurringAmount ?? 0,
                Subscriptions = subscriptions.Select(MapToDTO).ToList()
            };
        }

        // ══════════════════════════════════════════
        //  CANCELLATION & TERMINATION
        // ══════════════════════════════════════════

        public async Task<Result<SubscriptionDTO>> CancelSubscriptionAsync(
            string subscriptionId, string userId, CancelSubscriptionRequest request)
        {
            var subscription = await _dbContext.Subscriptions
                .FirstOrDefaultAsync(s => s.Id == subscriptionId);

            if (subscription == null)
                return Result<SubscriptionDTO>.Failure(new List<string> { "Subscription not found." });

            // Only the client or an admin can cancel
            if (subscription.ClientId != userId)
                return Result<SubscriptionDTO>.Failure(new List<string> { "Only the subscription owner can cancel." });

            if (subscription.Status != SubscriptionStatus.Active)
                return Result<SubscriptionDTO>.Failure(new List<string>
                {
                    $"Cannot cancel a subscription with status '{subscription.Status}'. Only active subscriptions can be cancelled."
                });

            subscription.Status = SubscriptionStatus.PendingCancellation;
            subscription.CancelAtPeriodEnd = true;
            subscription.CancellationRequestedAt = DateTime.UtcNow;
            subscription.CancellationReason = request.Reason;
            subscription.CancelledBy = "client";
            subscription.AutoRenew = false;
            subscription.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation(
                "Subscription {SubscriptionId} marked for cancellation at period end ({PeriodEnd})",
                subscriptionId, subscription.CurrentPeriodEnd);

            // Notify client
            await _mediator.Send(new SendNotificationCommand(
                subscription.ClientId,
                "system",
                NotificationTypes.SubscriptionCancellationScheduled,
                $"Your subscription will be cancelled on {subscription.CurrentPeriodEnd:MMM dd, yyyy}. Service continues until then.",
                "Cancellation Scheduled",
                subscriptionId));

            // Notify caregiver
            await _mediator.Send(new SendNotificationCommand(
                subscription.CaregiverId,
                "system",
                NotificationTypes.SubscriptionCancellationNotice,
                $"A client's subscription for your service will end on {subscription.CurrentPeriodEnd:MMM dd, yyyy}.",
                "Subscription Ending",
                subscriptionId));

            return Result<SubscriptionDTO>.Success(MapToDTO(subscription));
        }

        public async Task<Result<SubscriptionDTO>> ReactivateSubscriptionAsync(string subscriptionId, string userId)
        {
            var subscription = await _dbContext.Subscriptions
                .FirstOrDefaultAsync(s => s.Id == subscriptionId);

            if (subscription == null)
                return Result<SubscriptionDTO>.Failure(new List<string> { "Subscription not found." });

            if (subscription.ClientId != userId)
                return Result<SubscriptionDTO>.Failure(new List<string> { "Only the subscription owner can reactivate." });

            if (subscription.Status != SubscriptionStatus.PendingCancellation)
                return Result<SubscriptionDTO>.Failure(new List<string>
                {
                    "Only subscriptions pending cancellation can be reactivated."
                });

            if (DateTime.UtcNow >= subscription.CurrentPeriodEnd)
                return Result<SubscriptionDTO>.Failure(new List<string>
                {
                    "Cannot reactivate — the billing period has already ended."
                });

            subscription.Status = SubscriptionStatus.Active;
            subscription.CancelAtPeriodEnd = false;
            subscription.CancellationRequestedAt = null;
            subscription.CancellationReason = null;
            subscription.CancelledBy = null;
            subscription.AutoRenew = true;
            subscription.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Subscription {SubscriptionId} reactivated by {UserId}", subscriptionId, userId);

            await _mediator.Send(new SendNotificationCommand(
                subscription.ClientId,
                "system",
                NotificationTypes.SubscriptionReactivated,
                "Your subscription has been reactivated. Auto-renewal is back on.",
                "Subscription Reactivated",
                subscriptionId));

            return Result<SubscriptionDTO>.Success(MapToDTO(subscription));
        }

        public async Task<Result<SubscriptionDTO>> TerminateSubscriptionAsync(
            string subscriptionId, string userId, TerminateSubscriptionRequest request)
        {
            var subscription = await _dbContext.Subscriptions
                .FirstOrDefaultAsync(s => s.Id == subscriptionId);

            if (subscription == null)
                return Result<SubscriptionDTO>.Failure(new List<string> { "Subscription not found." });

            // Client, caregiver, or admin can terminate
            var isClient = subscription.ClientId == userId;
            var isCaregiver = subscription.CaregiverId == userId;
            if (!isClient && !isCaregiver)
                return Result<SubscriptionDTO>.Failure(new List<string> { "Not authorized to terminate this subscription." });

            var actor = isClient ? "client" : "caregiver";
            var result = await TerminateSubscriptionInternalAsync(
                subscription,
                actor,
                request.Reason,
                terminateLinkedContract: true);

            return result;
        }

        // ══════════════════════════════════════════
        //  PLAN CHANGES
        // ══════════════════════════════════════════

        public async Task<Result<PlanChangeResponse>> ChangePlanAsync(
            string subscriptionId, string userId, ChangePlanRequest request)
        {
            var subscription = await _dbContext.Subscriptions
                .FirstOrDefaultAsync(s => s.Id == subscriptionId);

            if (subscription == null)
                return Result<PlanChangeResponse>.Failure(new List<string> { "Subscription not found." });

            if (subscription.ClientId != userId)
                return Result<PlanChangeResponse>.Failure(new List<string> { "Only the subscription owner can change plans." });

            if (subscription.Status != SubscriptionStatus.Active)
                return Result<PlanChangeResponse>.Failure(new List<string> { "Can only change plans on active subscriptions." });

            var newCycle = request.NewBillingCycle?.ToLower() ?? subscription.BillingCycle;
            var newFrequency = request.NewFrequencyPerWeek ?? subscription.FrequencyPerWeek;

            if (newCycle != "monthly")
                return Result<PlanChangeResponse>.Failure(new List<string> { "BillingCycle must be 'monthly'." });

            if (newFrequency < 1 || newFrequency > 7)
                return Result<PlanChangeResponse>.Failure(new List<string> { "FrequencyPerWeek must be between 1 and 7." });

            // Check if anything actually changed
            if (newCycle == subscription.BillingCycle && newFrequency == subscription.FrequencyPerWeek)
                return Result<PlanChangeResponse>.Failure(new List<string> { "No changes detected — the plan is already set to these values." });

            // Calculate new amounts
            var basePrice = subscription.PricePerVisit;
            var newOrderFee = CalculateOrderFee(basePrice, newCycle, newFrequency);
            var newServiceCharge = Math.Round(newOrderFee * SERVICE_CHARGE_RATE, 2);
            var newGatewayFees = CalculateFlutterwaveFees(newOrderFee + newServiceCharge);
            var newTotal = newOrderFee + newServiceCharge + newGatewayFees;

            // Determine change type
            var changeType = newTotal > subscription.RecurringAmount ? "upgrade" : "downgrade";

            // Record plan change (takes effect next cycle)
            var planChange = new PlanChangeRecord
            {
                Id = ObjectId.GenerateNewId().ToString(),
                PreviousBillingCycle = subscription.BillingCycle,
                PreviousFrequencyPerWeek = subscription.FrequencyPerWeek,
                PreviousAmount = subscription.RecurringAmount,
                NewBillingCycle = newCycle,
                NewFrequencyPerWeek = newFrequency,
                NewAmount = newTotal,
                ChangeType = changeType,
                ChangedAt = DateTime.UtcNow,
                EffectiveDate = subscription.CurrentPeriodEnd
            };

            subscription.PlanChangeHistory.Add(planChange);

            // Apply changes immediately to the subscription (will charge new amount next cycle)
            subscription.BillingCycle = newCycle;
            subscription.FrequencyPerWeek = newFrequency;
            subscription.RecurringAmount = newTotal;
            subscription.PriceBreakdown = new SubscriptionPriceBreakdown
            {
                BasePrice = basePrice,
                FrequencyPerWeek = newFrequency,
                OrderFee = newOrderFee,
                ServiceCharge = newServiceCharge,
                GatewayFees = newGatewayFees,
                TotalAmount = newTotal
            };
            subscription.UpdatedAt = DateTime.UtcNow;

            // Recalculate next period end based on new billing cycle
            // (only affects future periods, current period stays the same)

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation(
                "Plan changed for subscription {SubscriptionId}: {OldCycle}/{OldFreq} -> {NewCycle}/{NewFreq} ({ChangeType}). " +
                "Effective: {EffectiveDate}",
                subscriptionId, planChange.PreviousBillingCycle, planChange.PreviousFrequencyPerWeek,
                newCycle, newFrequency, changeType, planChange.EffectiveDate);

            await _mediator.Send(new SendNotificationCommand(
                subscription.ClientId,
                "system",
                NotificationTypes.SubscriptionPlanChanged,
                $"Your plan has been {changeType}d. New amount: {subscription.Currency} {newTotal:N2}/{newCycle}. " +
                $"Changes take effect on {planChange.EffectiveDate:MMM dd, yyyy}.",
                "Plan Changed",
                subscriptionId));

            return Result<PlanChangeResponse>.Success(new PlanChangeResponse
            {
                Success = true,
                Message = $"Plan {changeType} scheduled successfully.",
                ChangeType = changeType,
                CurrentAmount = planChange.PreviousAmount,
                NewAmount = newTotal,
                EffectiveDate = planChange.EffectiveDate,
                Subscription = MapToDTO(subscription)
            });
        }

        public async Task<List<PlanChangeRecordDTO>> GetPlanChangeHistoryAsync(string subscriptionId)
        {
            var subscription = await _dbContext.Subscriptions
                .FirstOrDefaultAsync(s => s.Id == subscriptionId);

            if (subscription == null) return new List<PlanChangeRecordDTO>();

            return subscription.PlanChangeHistory
                .OrderByDescending(p => p.ChangedAt)
                .Select(p => new PlanChangeRecordDTO
                {
                    Id = p.Id,
                    PreviousBillingCycle = p.PreviousBillingCycle,
                    PreviousFrequencyPerWeek = p.PreviousFrequencyPerWeek,
                    PreviousAmount = p.PreviousAmount,
                    NewBillingCycle = p.NewBillingCycle,
                    NewFrequencyPerWeek = p.NewFrequencyPerWeek,
                    NewAmount = p.NewAmount,
                    ChangeType = p.ChangeType,
                    ChangedAt = p.ChangedAt,
                    EffectiveDate = p.EffectiveDate
                }).ToList();
        }

        // ══════════════════════════════════════════
        //  PAYMENT METHOD
        // ══════════════════════════════════════════

        public async Task<Result<UpdatePaymentMethodResponse>> InitiatePaymentMethodUpdateAsync(
            string subscriptionId, string userId, UpdatePaymentMethodRequest request)
        {
            var subscription = await _dbContext.Subscriptions
                .FirstOrDefaultAsync(s => s.Id == subscriptionId);

            if (subscription == null)
                return Result<UpdatePaymentMethodResponse>.Failure(new List<string> { "Subscription not found." });

            if (subscription.ClientId != userId)
                return Result<UpdatePaymentMethodResponse>.Failure(new List<string> { "Not authorized." });

            // Initiate a small verification charge (50 NGN) to capture new card token
            var txRef = $"CAREPRO-CARDUPDATE-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8].ToUpper()}";

            try
            {
                var flutterwaveResponse = await _flutterwaveService.InitiatePayment(
                    50m, // Small verification amount
                    subscription.Email,
                    subscription.Currency,
                    txRef,
                    request.RedirectUrl,
                    "card"); // Force card payment so a token can always be captured

                var paymentLink = ExtractPaymentLink(flutterwaveResponse);
                if (string.IsNullOrEmpty(paymentLink))
                {
                    return Result<UpdatePaymentMethodResponse>.Failure(new List<string>
                    {
                        "Failed to initiate card verification with payment provider."
                    });
                }

                // Store the txRef so the webhook can look up this subscription when the 50 NGN charge completes
                subscription.PendingCardUpdateTxRef = txRef;
                subscription.CardUpdateState = "pending";
                subscription.CardUpdateStartedAt = DateTime.UtcNow;
                subscription.CardUpdateFailureReason = null;
                subscription.UpdatedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();

                return Result<UpdatePaymentMethodResponse>.Success(new UpdatePaymentMethodResponse
                {
                    Success = true,
                    Message = "Please authorize your new card. A small verification charge of 50 NGN will be placed and refunded.",
                    AuthorizationLink = paymentLink,
                    TransactionReference = txRef
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initiate payment method update for subscription {SubscriptionId}", subscriptionId);
                return Result<UpdatePaymentMethodResponse>.Failure(new List<string> { "An error occurred. Please try again." });
            }
        }

        public async Task<Result<SubscriptionPaymentRecordDTO>> CompleteRecurringChargeFromWebhookAsync(
            string txRef, string flutterwaveTransactionId, decimal amount)
        {
            // Find the subscription that has a pending payment record with this txRef
            var subscription = await _dbContext.Subscriptions
                .FirstOrDefaultAsync(s => s.PaymentHistory.Any(p =>
                    p.TransactionReference == txRef && p.Status == "pending"));

            if (subscription == null)
            {
                _logger.LogWarning(
                    "CompleteRecurringChargeFromWebhook: No subscription found with pending txRef {TxRef}",
                    txRef);
                return Result<SubscriptionPaymentRecordDTO>.Failure(new List<string> { "Subscription not found for this transaction." });
            }

            var paymentRecord = subscription.PaymentHistory
                .FirstOrDefault(p => p.TransactionReference == txRef && p.Status == "pending");

            if (paymentRecord == null)
                return Result<SubscriptionPaymentRecordDTO>.Failure(new List<string> { "Payment record not found." });

            // Amount verification
            if (Math.Abs(amount - subscription.RecurringAmount) > 0.01m)
            {
                _logger.LogCritical(
                    "SECURITY: AMOUNT MISMATCH on webhook recurring charge! Subscription {SubscriptionId}, Expected: {Expected}, Received: {Received}",
                    subscription.Id, subscription.RecurringAmount, amount);
                paymentRecord.Status = "amount_mismatch";
                paymentRecord.ErrorMessage = $"Expected {subscription.RecurringAmount}, received {amount}";
                subscription.UpdatedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();
                return Result<SubscriptionPaymentRecordDTO>.Failure(new List<string> { "Payment amount mismatch detected." });
            }

            var cycleNumber = paymentRecord.BillingCycleNumber;

            // Create the ClientOrder for this billing cycle
            var orderResult = await _clientOrderService.CreateClientOrderAsync(new AddClientOrderRequest
            {
                ClientId = subscription.ClientId,
                GigId = subscription.GigId,
                PaymentOption = subscription.BillingCycle,
                Amount = (int)Math.Round(subscription.RecurringAmount, 0),
                OrderFee = subscription.PriceBreakdown.OrderFee,
                TransactionId = flutterwaveTransactionId,
                BillingCycleNumber = cycleNumber
            });

            paymentRecord.Status = "successful";
            paymentRecord.FlutterwaveTransactionId = flutterwaveTransactionId;
            paymentRecord.CompletedAt = DateTime.UtcNow;
            paymentRecord.ClientOrderId = orderResult.Value?.Id;

            var now = DateTime.UtcNow;
            subscription.CurrentPeriodStart = now;
            subscription.CurrentPeriodEnd = now.AddDays(30);
            subscription.NextChargeDate = subscription.CurrentPeriodEnd;
            subscription.BillingCyclesCompleted = cycleNumber;
            subscription.FailedChargeAttempts = 0;
            subscription.LastChargeError = null;
            subscription.Status = SubscriptionStatus.Active;
            subscription.UpdatedAt = now;

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation(
                "Recurring charge completed via webhook for subscription {SubscriptionId}. Cycle #{Cycle}, Amount: {Amount}",
                subscription.Id, cycleNumber, amount);

            // Billing record
            try
            {
                await _billingRecordService.CreateBillingRecordAsync(
                    orderId: orderResult.Value?.Id ?? string.Empty,
                    clientId: subscription.ClientId,
                    caregiverId: subscription.CaregiverId,
                    gigId: subscription.GigId,
                    serviceType: subscription.BillingCycle,
                    frequencyPerWeek: subscription.FrequencyPerWeek,
                    amountPaid: subscription.RecurringAmount,
                    orderFee: subscription.PriceBreakdown.OrderFee,
                    serviceCharge: subscription.PriceBreakdown.ServiceCharge,
                    gatewayFees: subscription.PriceBreakdown.GatewayFees,
                    paymentTransactionId: flutterwaveTransactionId,
                    subscriptionId: subscription.Id,
                    contractId: subscription.ContractId,
                    billingCycleNumber: cycleNumber,
                    periodStart: subscription.CurrentPeriodStart,
                    periodEnd: subscription.CurrentPeriodEnd,
                    nextChargeDate: subscription.NextChargeDate
                );
            }
            catch (Exception billingEx)
            {
                _logger.LogError(billingEx,
                    "Failed to create BillingRecord for subscription {SubscriptionId} cycle {Cycle} (webhook path)",
                    subscription.Id, cycleNumber);
            }

            await _mediator.Send(new SendNotificationCommand(
                subscription.ClientId,
                "system",
                NotificationTypes.RecurringPaymentSuccessful,
                $"Your {subscription.BillingCycle} subscription payment of {subscription.Currency} {subscription.RecurringAmount:N2} was successful. " +
                $"Next charge: {subscription.CurrentPeriodEnd:MMM dd, yyyy}.",
                "Payment Successful",
                subscription.Id));

            return Result<SubscriptionPaymentRecordDTO>.Success(new SubscriptionPaymentRecordDTO
            {
                Id = paymentRecord.Id,
                TransactionReference = paymentRecord.TransactionReference,
                FlutterwaveTransactionId = paymentRecord.FlutterwaveTransactionId,
                Amount = paymentRecord.Amount,
                Currency = paymentRecord.Currency,
                Status = paymentRecord.Status,
                BillingCycleNumber = paymentRecord.BillingCycleNumber,
                AttemptedAt = paymentRecord.AttemptedAt,
                CompletedAt = paymentRecord.CompletedAt,
                ClientOrderId = paymentRecord.ClientOrderId
            });
        }

        public async Task<Result<SubscriptionDTO>> CompletePaymentMethodUpdateByTxRefAsync(
            string txRef, string token, string cardLastFour, string cardBrand, string cardExpiry)
        {
            var subscription = await _dbContext.Subscriptions
                .FirstOrDefaultAsync(s => s.PendingCardUpdateTxRef == txRef);

            if (subscription == null)
            {
                _logger.LogWarning(
                    "CompletePaymentMethodUpdateByTxRef: No subscription found with PendingCardUpdateTxRef={TxRef}", txRef);
                return Result<SubscriptionDTO>.Failure(new List<string> { "Subscription not found for this card update." });
            }

            return await CompletePaymentMethodUpdateAsync(subscription.Id, token, cardLastFour, cardBrand, cardExpiry);
        }

        public async Task MarkPaymentMethodUpdateFailedByTxRefAsync(string txRef, string reason)
        {
            var subscription = await _dbContext.Subscriptions
                .FirstOrDefaultAsync(s => s.PendingCardUpdateTxRef == txRef);

            if (subscription == null)
            {
                _logger.LogWarning(
                    "MarkPaymentMethodUpdateFailedByTxRef: No subscription found with PendingCardUpdateTxRef={TxRef}", txRef);
                return;
            }

            subscription.CardUpdateState = "failed";
            subscription.CardUpdateFailureReason = reason;
            subscription.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
        }

        public async Task<Result<SubscriptionDTO>> CompletePaymentMethodUpdateAsync(
            string subscriptionId, string flutterwaveToken, string cardLastFour, string cardBrand, string cardExpiry)
        {
            var subscription = await _dbContext.Subscriptions
                .FirstOrDefaultAsync(s => s.Id == subscriptionId);

            if (subscription == null)
                return Result<SubscriptionDTO>.Failure(new List<string> { "Subscription not found." });

            subscription.FlutterwavePaymentToken = flutterwaveToken;
            subscription.CardLastFour = cardLastFour;
            subscription.CardBrand = cardBrand;
            subscription.CardExpiry = cardExpiry;
            subscription.PendingCardUpdateTxRef = null; // Clear pending update marker
            subscription.CardUpdateState = "completed";
            subscription.CardUpdateCompletedAt = DateTime.UtcNow;
            subscription.CardUpdateFailureReason = null;
            subscription.UpdatedAt = DateTime.UtcNow;

            // If subscription was suspended or past-due due to payment failures, reactivate it
            if (subscription.Status == SubscriptionStatus.Suspended ||
                subscription.Status == SubscriptionStatus.PastDue)
            {
                subscription.Status = SubscriptionStatus.Active;
                subscription.FailedChargeAttempts = 0;
                subscription.LastChargeError = null;
                subscription.NextChargeDate = DateTime.UtcNow; // Charge immediately
                _logger.LogInformation("Subscription {SubscriptionId} reactivated after payment method update", subscriptionId);
            }

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation(
                "Payment method updated for subscription {SubscriptionId}. Card: {Brand} ****{Last4}",
                subscriptionId, cardBrand, cardLastFour);

            await _mediator.Send(new SendNotificationCommand(
                subscription.ClientId,
                "system",
                NotificationTypes.PaymentMethodUpdated,
                $"Your payment method has been updated to {cardBrand} ****{cardLastFour}.",
                "Payment Method Updated",
                subscriptionId));

            return Result<SubscriptionDTO>.Success(MapToDTO(subscription));
        }

        // ══════════════════════════════════════════
        //  PAUSE / RESUME
        // ══════════════════════════════════════════

        public async Task<Result<SubscriptionDTO>> PauseSubscriptionAsync(
            string subscriptionId, string userId, PauseSubscriptionRequest request)
        {
            var subscription = await _dbContext.Subscriptions
                .FirstOrDefaultAsync(s => s.Id == subscriptionId);

            if (subscription == null)
                return Result<SubscriptionDTO>.Failure(new List<string> { "Subscription not found." });

            if (subscription.ClientId != userId)
                return Result<SubscriptionDTO>.Failure(new List<string> { "Not authorized." });

            if (subscription.Status != SubscriptionStatus.Active)
                return Result<SubscriptionDTO>.Failure(new List<string> { "Only active subscriptions can be paused." });

            subscription.Status = SubscriptionStatus.Paused;
            subscription.NextChargeDate = null; // No charges while paused
            subscription.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Subscription {SubscriptionId} paused by {UserId}", subscriptionId, userId);

            await _mediator.Send(new SendNotificationCommand(
                subscription.ClientId,
                "system",
                NotificationTypes.SubscriptionPaused,
                "Your subscription has been paused. No charges will be made until you resume.",
                "Subscription Paused",
                subscriptionId));

            return Result<SubscriptionDTO>.Success(MapToDTO(subscription));
        }

        public async Task<Result<SubscriptionDTO>> ResumeSubscriptionAsync(string subscriptionId, string userId)
        {
            var subscription = await _dbContext.Subscriptions
                .FirstOrDefaultAsync(s => s.Id == subscriptionId);

            if (subscription == null)
                return Result<SubscriptionDTO>.Failure(new List<string> { "Subscription not found." });

            if (subscription.ClientId != userId)
                return Result<SubscriptionDTO>.Failure(new List<string> { "Not authorized." });

            if (subscription.Status != SubscriptionStatus.Paused)
                return Result<SubscriptionDTO>.Failure(new List<string> { "Only paused subscriptions can be resumed." });

            var now = DateTime.UtcNow;
            var periodEnd = now.AddDays(30); // Monthly billing only

            subscription.Status = SubscriptionStatus.Active;
            subscription.CurrentPeriodStart = now;
            subscription.CurrentPeriodEnd = periodEnd;
            subscription.NextChargeDate = periodEnd;
            subscription.AutoRenew = true;
            subscription.UpdatedAt = now;

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Subscription {SubscriptionId} resumed by {UserId}. Next charge: {NextCharge}",
                subscriptionId, userId, periodEnd);

            await _mediator.Send(new SendNotificationCommand(
                subscription.ClientId,
                "system",
                NotificationTypes.SubscriptionResumed,
                $"Your subscription has been resumed. Next charge: {periodEnd:MMM dd, yyyy}.",
                "Subscription Resumed",
                subscriptionId));

            return Result<SubscriptionDTO>.Success(MapToDTO(subscription));
        }

        // ══════════════════════════════════════════
        //  RECURRING BILLING (Background Service)
        // ══════════════════════════════════════════

        public async Task<List<Subscription>> GetSubscriptionsDueForBillingAsync()
        {
            var now = DateTime.UtcNow;
            return await _dbContext.Subscriptions
                .Where(s =>
                    (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.PastDue) &&
                    s.AutoRenew &&
                    s.NextChargeDate.HasValue &&
                    s.NextChargeDate.Value <= now &&
                    !string.IsNullOrEmpty(s.FlutterwavePaymentToken))
                .ToListAsync();
        }

        public async Task<Result<SubscriptionPaymentRecordDTO>> ProcessRecurringChargeAsync(string subscriptionId)
        {
            var subscription = await _dbContext.Subscriptions
                .FirstOrDefaultAsync(s => s.Id == subscriptionId);

            if (subscription == null)
                return Result<SubscriptionPaymentRecordDTO>.Failure(new List<string> { "Subscription not found." });

            if (string.IsNullOrEmpty(subscription.FlutterwavePaymentToken))
                return Result<SubscriptionPaymentRecordDTO>.Failure(new List<string> { "No payment token available." });

            // ── DOUBLE-CHARGE GUARD ──
            // If a charge is already in progress (status set to "charging"), skip.
            // This protects against the background service picking up the same subscription
            // on concurrent runs or when a charge takes longer than the check interval.
            if (subscription.Status == SubscriptionStatus.Charging)
            {
                _logger.LogWarning(
                    "SECURITY: Skipping subscription {SubscriptionId} — charge already in progress (status=Charging).",
                    subscriptionId);
                return Result<SubscriptionPaymentRecordDTO>.Failure(new List<string> { "Charge already in progress." });
            }

            // Mark as "charging" to prevent concurrent processing
            var previousStatus = subscription.Status;
            subscription.Status = SubscriptionStatus.Charging;
            subscription.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            var txRef = $"CAREPRO-RECURRING-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8].ToUpper()}";
            var cycleNumber = subscription.BillingCyclesCompleted + 1;

            var paymentRecord = new SubscriptionPaymentRecord
            {
                Id = ObjectId.GenerateNewId().ToString(),
                TransactionReference = txRef,
                Amount = subscription.RecurringAmount,
                Currency = subscription.Currency,
                Status = "pending",
                BillingCycleNumber = cycleNumber,
                AttemptedAt = DateTime.UtcNow
            };

            try
            {
                // Charge using Flutterwave tokenized payment
                var chargeResult = await _flutterwaveService.ChargeWithToken(
                    subscription.FlutterwavePaymentToken,
                    subscription.RecurringAmount,
                    subscription.Currency,
                    subscription.Email,
                    txRef);

                if (chargeResult == null || !chargeResult.Success)
                {
                    if (chargeResult?.IsPending == true)
                    {
                        // 3DS/OTP required — not a permanent failure, awaiting user action
                        paymentRecord.Status = "pending";
                        paymentRecord.ErrorMessage = "Awaiting cardholder authentication";
                        subscription.PaymentHistory.Add(paymentRecord);
                        subscription.Status = previousStatus;
                        subscription.UpdatedAt = DateTime.UtcNow;
                        await _dbContext.SaveChangesAsync();

                        var authUrl = chargeResult.AuthUrl ?? $"{_configuration["FrontendUrl"] ?? "https://oncarepro.com"}/subscription/payment-confirmed";
                        var pendingLinkedOrderId = string.IsNullOrWhiteSpace(subscription.OriginalOrderId)
                            ? null
                            : subscription.OriginalOrderId;

                        await _mediator.Send(new SendNotificationCommand(
                            subscription.ClientId,
                            "system",
                            NotificationTypes.PaymentActionRequired,
                            $"Your ₦{subscription.RecurringAmount:N2} subscription payment requires your authorisation. Please complete your payment here: {authUrl} AUTH_URL={authUrl}",
                            "Payment Authorisation Required",
                            subscriptionId,
                            pendingLinkedOrderId));

                        _logger.LogInformation(
                            "Subscription {SubscriptionId} charge requires 3DS. AuthUrl: {AuthUrl}",
                            subscriptionId, authUrl);

                        return Result<SubscriptionPaymentRecordDTO>.Failure(new List<string> { "Awaiting cardholder authentication." });
                    }

                    var error = chargeResult?.ErrorMessage ?? "Charge failed";
                    paymentRecord.Status = "failed";
                    paymentRecord.ErrorMessage = error;
                    subscription.PaymentHistory.Add(paymentRecord);
                    subscription.Status = previousStatus; // Restore previous status
                    await _dbContext.SaveChangesAsync();
                    await HandleFailedChargeAsync(subscriptionId, error);
                    return Result<SubscriptionPaymentRecordDTO>.Failure(new List<string> { error });
                }

                // ── SERVER-TO-SERVER VERIFICATION ──
                // Don't trust the charge response alone — verify the transaction is genuinely successful
                var verification = await _flutterwaveService.VerifyTransactionAsync(chargeResult.TransactionId);
                if (verification == null || !verification.Success ||
                    verification.Status.ToLower() != "successful")
                {
                    _logger.LogCritical(
                        "SECURITY: Tokenized charge for subscription {SubscriptionId} returned success but server verification FAILED. " +
                        "ChargeTransactionId: {TxId}, VerifyStatus: {Status}",
                        subscriptionId, chargeResult.TransactionId, verification?.Status ?? "null");

                    paymentRecord.Status = "verification_failed";
                    paymentRecord.ErrorMessage = "Charge reported success but verification failed";
                    subscription.PaymentHistory.Add(paymentRecord);
                    subscription.Status = previousStatus;
                    await _dbContext.SaveChangesAsync();
                    return Result<SubscriptionPaymentRecordDTO>.Failure(new List<string> { "Payment verification failed. Will retry." });
                }

                // ── AMOUNT VERIFICATION ──
                // Ensure the verified amount matches what we expected to charge
                if (Math.Abs(verification.Amount - subscription.RecurringAmount) > 0.01m)
                {
                    _logger.LogCritical(
                        "SECURITY: AMOUNT MISMATCH on recurring charge! Subscription {SubscriptionId}, Expected: {Expected}, Verified: {Verified}",
                        subscriptionId, subscription.RecurringAmount, verification.Amount);

                    paymentRecord.Status = "amount_mismatch";
                    paymentRecord.ErrorMessage = $"Expected {subscription.RecurringAmount}, verified {verification.Amount}";
                    subscription.PaymentHistory.Add(paymentRecord);
                    subscription.Status = previousStatus;
                    await _dbContext.SaveChangesAsync();
                    return Result<SubscriptionPaymentRecordDTO>.Failure(new List<string> { "Payment amount mismatch detected." });
                }

                // Payment verified — create a new ClientOrder for this billing cycle
                var orderResult = await _clientOrderService.CreateClientOrderAsync(new AddClientOrderRequest
                {
                    ClientId = subscription.ClientId,
                    GigId = subscription.GigId,
                    PaymentOption = subscription.BillingCycle,
                    Amount = (int)Math.Round(subscription.RecurringAmount, 0), // Rounded to int for ClientOrder
                    OrderFee = subscription.PriceBreakdown.OrderFee,
                    TransactionId = chargeResult.TransactionId,
                    BillingCycleNumber = cycleNumber
                });

                paymentRecord.Status = "successful";
                paymentRecord.FlutterwaveTransactionId = chargeResult.TransactionId;
                paymentRecord.CompletedAt = DateTime.UtcNow;
                paymentRecord.ClientOrderId = orderResult.Value?.Id;

                // Advance the billing period
                var now = DateTime.UtcNow;
                subscription.CurrentPeriodStart = now;
                subscription.CurrentPeriodEnd = now.AddDays(30); // Monthly billing only
                subscription.NextChargeDate = subscription.CurrentPeriodEnd;
                subscription.BillingCyclesCompleted = cycleNumber;
                subscription.FailedChargeAttempts = 0;
                subscription.LastChargeError = null;
                subscription.Status = SubscriptionStatus.Active; // Restore from Charging → Active
                subscription.PaymentHistory.Add(paymentRecord);
                subscription.UpdatedAt = now;

                await _dbContext.SaveChangesAsync();

                _logger.LogInformation(
                    "Recurring charge successful for subscription {SubscriptionId}. Cycle #{Cycle}, Amount: {Amount}, OrderId: {OrderId}",
                    subscriptionId, cycleNumber, subscription.RecurringAmount, orderResult.Value?.Id);

                // ── Create BillingRecord for this renewal cycle ──
                try
                {
                    await _billingRecordService.CreateBillingRecordAsync(
                        orderId: orderResult.Value?.Id ?? string.Empty,
                        clientId: subscription.ClientId,
                        caregiverId: subscription.CaregiverId,
                        gigId: subscription.GigId,
                        serviceType: subscription.BillingCycle,
                        frequencyPerWeek: subscription.FrequencyPerWeek,
                        amountPaid: subscription.RecurringAmount,
                        orderFee: subscription.PriceBreakdown.OrderFee,
                        serviceCharge: subscription.PriceBreakdown.ServiceCharge,
                        gatewayFees: subscription.PriceBreakdown.GatewayFees,
                        paymentTransactionId: chargeResult.TransactionId ?? txRef,
                        subscriptionId: subscriptionId,
                        contractId: subscription.ContractId,
                        billingCycleNumber: cycleNumber,
                        periodStart: subscription.CurrentPeriodStart,
                        periodEnd: subscription.CurrentPeriodEnd,
                        nextChargeDate: subscription.NextChargeDate
                    );
                }
                catch (Exception billingEx)
                {
                    _logger.LogError(billingEx, "Failed to create BillingRecord for subscription {SubscriptionId} cycle {Cycle}", subscriptionId, cycleNumber);
                }

                // Notify client of successful charge
                await _mediator.Send(new SendNotificationCommand(
                    subscription.ClientId,
                    "system",
                    NotificationTypes.RecurringPaymentSuccessful,
                    $"Your {subscription.BillingCycle} subscription payment of {subscription.Currency} {subscription.RecurringAmount:N2} was successful. " +
                    $"Next charge: {subscription.CurrentPeriodEnd:MMM dd, yyyy}.",
                    "Payment Successful",
                    subscriptionId));

                return Result<SubscriptionPaymentRecordDTO>.Success(new SubscriptionPaymentRecordDTO
                {
                    Id = paymentRecord.Id,
                    TransactionReference = paymentRecord.TransactionReference,
                    FlutterwaveTransactionId = paymentRecord.FlutterwaveTransactionId,
                    Amount = paymentRecord.Amount,
                    Currency = paymentRecord.Currency,
                    Status = paymentRecord.Status,
                    BillingCycleNumber = paymentRecord.BillingCycleNumber,
                    AttemptedAt = paymentRecord.AttemptedAt,
                    CompletedAt = paymentRecord.CompletedAt,
                    ClientOrderId = paymentRecord.ClientOrderId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing recurring charge for subscription {SubscriptionId}", subscriptionId);

                // Restore status from Charging back to previous state
                subscription.Status = previousStatus;

                paymentRecord.Status = "failed";
                paymentRecord.ErrorMessage = ex.Message;
                subscription.PaymentHistory.Add(paymentRecord);
                await _dbContext.SaveChangesAsync();
                await HandleFailedChargeAsync(subscriptionId, ex.Message);
                return Result<SubscriptionPaymentRecordDTO>.Failure(new List<string> { "Payment processing error. Will retry." });
            }
        }

        public async Task HandleFailedChargeAsync(string subscriptionId, string errorMessage)
        {
            var subscription = await _dbContext.Subscriptions
                .FirstOrDefaultAsync(s => s.Id == subscriptionId);

            if (subscription == null) return;

            subscription.FailedChargeAttempts++;
            subscription.LastChargeError = errorMessage;
            subscription.LastFailedChargeAt = DateTime.UtcNow;
            subscription.UpdatedAt = DateTime.UtcNow;

            if (subscription.FailedChargeAttempts >= subscription.MaxRetryAttempts)
            {
                // All retries exhausted — suspend the subscription
                subscription.Status = SubscriptionStatus.Suspended;
                subscription.NextChargeDate = null;

                _logger.LogWarning(
                    "Subscription {SubscriptionId} SUSPENDED after {Attempts} failed charge attempts. Last error: {Error}",
                    subscriptionId, subscription.FailedChargeAttempts, errorMessage);

                // Pass OriginalOrderId (when present) as OrderId so the frontend's
                // `notification.orderId || relatedEntityId` fallback resolves to a real
                // ClientOrders._id instead of the subscription id (which 404s on /api/ClientOrders/orderId).
                var suspendedLinkedOrderId = string.IsNullOrWhiteSpace(subscription.OriginalOrderId)
                    ? null
                    : subscription.OriginalOrderId;

                await _mediator.Send(new SendNotificationCommand(
                    subscription.ClientId,
                    "system",
                    NotificationTypes.SubscriptionSuspended,
                    "Your subscription has been suspended due to repeated payment failures. Please update your payment method to continue service.",
                    "Subscription Suspended",
                    subscriptionId,
                    suspendedLinkedOrderId));
            }
            else
            {
                // Schedule retry (exponential backoff: 1h, 4h, 16h)
                var retryHours = Math.Pow(4, subscription.FailedChargeAttempts - 1);
                subscription.NextChargeDate = DateTime.UtcNow.AddHours(retryHours);
                subscription.Status = SubscriptionStatus.PastDue;

                _logger.LogWarning(
                    "Subscription {SubscriptionId} charge failed (attempt {Attempt}/{Max}). Retry at {RetryTime}. Error: {Error}",
                    subscriptionId, subscription.FailedChargeAttempts, subscription.MaxRetryAttempts,
                    subscription.NextChargeDate, errorMessage);

                // Pass OriginalOrderId (when present) as OrderId so the frontend's
                // `notification.orderId || relatedEntityId` fallback resolves to a real
                // ClientOrders._id instead of the subscription id (which 404s on /api/ClientOrders/orderId).
                var failedLinkedOrderId = string.IsNullOrWhiteSpace(subscription.OriginalOrderId)
                    ? null
                    : subscription.OriginalOrderId;

                await _mediator.Send(new SendNotificationCommand(
                    subscription.ClientId,
                    "system",
                    NotificationTypes.PaymentFailed,
                    $"Your ₦{subscription.RecurringAmount:N2} subscription payment failed: {errorMessage}. We'll retry automatically. Please ensure your payment method is up to date.",
                    "Payment Failed",
                    subscriptionId,
                    failedLinkedOrderId));
            }

            await _dbContext.SaveChangesAsync();
        }

        public async Task<List<Subscription>> GetSubscriptionsPendingFinalCancellationAsync()
        {
            var now = DateTime.UtcNow;
            return await _dbContext.Subscriptions
                .Where(s =>
                    s.Status == SubscriptionStatus.PendingCancellation &&
                    s.CancelAtPeriodEnd &&
                    s.CurrentPeriodEnd <= now)
                .ToListAsync();
        }

        public async Task FinalizeCancellationAsync(string subscriptionId)
        {
            var subscription = await _dbContext.Subscriptions
                .FirstOrDefaultAsync(s => s.Id == subscriptionId);

            if (subscription == null) return;

            subscription.Status = SubscriptionStatus.Cancelled;
            subscription.TerminatedAt = DateTime.UtcNow;
            subscription.NextChargeDate = null;
            subscription.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Subscription {SubscriptionId} finalized as Cancelled", subscriptionId);

            await _mediator.Send(new SendNotificationCommand(
                subscription.ClientId,
                "system",
                NotificationTypes.SubscriptionCancelled,
                "Your subscription has ended. Thank you for using CarePro.",
                "Subscription Ended",
                subscriptionId));

            await _mediator.Send(new SendNotificationCommand(
                subscription.CaregiverId,
                "system",
                NotificationTypes.SubscriptionEnded,
                "A client's subscription for your service has ended.",
                "Subscription Ended",
                subscriptionId));
        }

        // ══════════════════════════════════════════
        //  BILLING HISTORY
        // ══════════════════════════════════════════

        public async Task<List<SubscriptionPaymentRecordDTO>> GetPaymentHistoryAsync(string subscriptionId)
        {
            var subscription = await _dbContext.Subscriptions
                .FirstOrDefaultAsync(s => s.Id == subscriptionId);

            if (subscription == null) return new List<SubscriptionPaymentRecordDTO>();

            return subscription.PaymentHistory
                .OrderByDescending(p => p.AttemptedAt)
                .Select(p => new SubscriptionPaymentRecordDTO
                {
                    Id = p.Id,
                    TransactionReference = p.TransactionReference,
                    FlutterwaveTransactionId = p.FlutterwaveTransactionId,
                    Amount = p.Amount,
                    Currency = p.Currency,
                    Status = p.Status,
                    ErrorMessage = p.ErrorMessage,
                    BillingCycleNumber = p.BillingCycleNumber,
                    AttemptedAt = p.AttemptedAt,
                    CompletedAt = p.CompletedAt,
                    ClientOrderId = p.ClientOrderId
                }).ToList();
        }

        // ══════════════════════════════════════════
        //  ADMIN
        // ══════════════════════════════════════════

        public async Task<SubscriptionAnalytics> GetSubscriptionAnalyticsAsync()
        {
            var subscriptions = await _dbContext.Subscriptions.ToListAsync();
            var now = DateTime.UtcNow;
            var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

            var active = subscriptions.Where(s => s.Status == SubscriptionStatus.Active).ToList();

            return new SubscriptionAnalytics
            {
                TotalActive = active.Count,
                TotalPastDue = subscriptions.Count(s => s.Status == SubscriptionStatus.PastDue),
                TotalCancelled = subscriptions.Count(s => s.Status == SubscriptionStatus.Cancelled),
                TotalSuspended = subscriptions.Count(s => s.Status == SubscriptionStatus.Suspended),
                MonthlyRecurringRevenue = active.Sum(s =>
                    s.BillingCycle == "weekly" ? s.RecurringAmount * 4.33m : s.RecurringAmount),
                NewSubscriptionsThisMonth = subscriptions.Count(s => s.CreatedAt >= monthStart),
                CancellationsThisMonth = subscriptions.Count(s =>
                    (s.Status == SubscriptionStatus.Cancelled || s.Status == SubscriptionStatus.Terminated) &&
                    s.TerminatedAt.HasValue && s.TerminatedAt.Value >= monthStart),
                ChurnRate = subscriptions.Count > 0
                    ? (decimal)subscriptions.Count(s =>
                        (s.Status == SubscriptionStatus.Cancelled || s.Status == SubscriptionStatus.Terminated) &&
                        s.TerminatedAt.HasValue && s.TerminatedAt.Value >= monthStart) / Math.Max(1, active.Count) * 100
                    : 0
            };
        }

        public async Task<List<SubscriptionDTO>> GetAllSubscriptionsAsync(string? statusFilter = null)
        {
            var query = _dbContext.Subscriptions.AsQueryable();

            if (!string.IsNullOrEmpty(statusFilter) && Enum.TryParse<SubscriptionStatus>(statusFilter, true, out var status))
            {
                query = query.Where(s => s.Status == status);
            }

            var subscriptions = await query.OrderByDescending(s => s.CreatedAt).ToListAsync();
            return subscriptions.Select(MapToDTO).ToList();
        }

        public async Task<Result<SubscriptionDTO>> LinkContractAsync(string subscriptionId, string contractId)
        {
            var subscription = await _dbContext.Subscriptions
                .FirstOrDefaultAsync(s => s.Id == subscriptionId);

            if (subscription == null)
                return Result<SubscriptionDTO>.Failure(new List<string> { "Subscription not found." });

            subscription.ContractId = contractId;
            subscription.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Contract {ContractId} linked to subscription {SubscriptionId}", contractId, subscriptionId);
            return Result<SubscriptionDTO>.Success(MapToDTO(subscription));
        }

        private async Task<Result<SubscriptionDTO>> TerminateSubscriptionInternalAsync(
            Subscription subscription,
            string actor,
            string reason,
            bool terminateLinkedContract)
        {
            if (IsSubscriptionTerminal(subscription.Status))
            {
                return Result<SubscriptionDTO>.Failure(new List<string>
                {
                    $"Subscription is already '{subscription.Status}'."
                });
            }

            var now = DateTime.UtcNow;
            subscription.Status = SubscriptionStatus.Terminated;
            subscription.TerminatedAt = now;
            subscription.CancellationReason = reason;
            subscription.CancelledBy = actor;
            subscription.AutoRenew = false;
            subscription.NextChargeDate = null;
            subscription.RefundAmount = null;
            subscription.UpdatedAt = now;

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation(
                "Subscription {SubscriptionId} terminated immediately by {Actor}. Reason: {Reason}",
                subscription.Id, actor, reason);

            if (terminateLinkedContract)
            {
                await TerminateLinkedContractIfNeededAsync(subscription.ContractId, now);
            }

            await ExpireLinkedCommitmentIfNeededAsync(subscription);
            await SendTerminationNotificationsAsync(subscription);

            return Result<SubscriptionDTO>.Success(MapToDTO(subscription));
        }

        private async Task TerminateLinkedContractIfNeededAsync(string? contractId, DateTime now)
        {
            if (string.IsNullOrEmpty(contractId))
                return;

            var contract = await _dbContext.Contracts
                .FirstOrDefaultAsync(c => c.Id == contractId);

            if (contract == null ||
                contract.Status == ContractStatus.Terminated ||
                contract.Status == ContractStatus.Completed)
                return;

            contract.Status = ContractStatus.Terminated;
            contract.UpdatedAt = now;
            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Linked contract {ContractId} also terminated", contractId);
        }

        private async Task ExpireLinkedCommitmentIfNeededAsync(Subscription subscription)
        {
            if (string.IsNullOrEmpty(subscription.OriginalOrderId))
                return;

            var linkedCommitment = await _dbContext.BookingCommitments
                .FirstOrDefaultAsync(bc => bc.AppliedToOrderId == subscription.OriginalOrderId
                                        && bc.Status == BookingCommitmentStatus.Completed);

            if (linkedCommitment == null)
                return;

            linkedCommitment.Status = BookingCommitmentStatus.Expired;
            await _dbContext.SaveChangesAsync();
            _logger.LogInformation(
                "Booking commitment {CommitmentId} expired due to subscription termination. Client {ClientId} must pay again to re-engage caregiver {CaregiverId}",
                linkedCommitment.Id, subscription.ClientId, subscription.CaregiverId);
        }

        private async Task SendTerminationNotificationsAsync(Subscription subscription)
        {
            var clientGuidance = string.IsNullOrEmpty(subscription.OriginalOrderId)
                ? string.Empty
                : " If you want an immediate refund for undelivered service, please cancel the current order as well.";

            await _mediator.Send(new SendNotificationCommand(
                subscription.ClientId,
                "system",
                NotificationTypes.SubscriptionTerminated,
                $"Your subscription has been terminated immediately.{clientGuidance}",
                "Subscription Terminated",
                subscription.Id));

            await _mediator.Send(new SendNotificationCommand(
                subscription.CaregiverId,
                "system",
                NotificationTypes.SubscriptionTerminated,
                "A subscription for your service has been terminated.",
                "Subscription Terminated",
                subscription.Id));
        }

        private static bool IsSubscriptionTerminal(SubscriptionStatus status)
        {
            var terminalStatuses = new[] { SubscriptionStatus.Cancelled, SubscriptionStatus.Terminated, SubscriptionStatus.Expired };
            return terminalStatuses.Contains(status);
        }

        // ══════════════════════════════════════════
        //  PRIVATE HELPERS
        // ══════════════════════════════════════════

        private static SubscriptionDTO MapToDTO(Subscription s) => new SubscriptionDTO
        {
            Id = s.Id,
            ClientId = s.ClientId,
            CaregiverId = s.CaregiverId,
            GigId = s.GigId,
            OriginalOrderId = s.OriginalOrderId,
            ContractId = s.ContractId,
            BillingCycle = s.BillingCycle,
            FrequencyPerWeek = s.FrequencyPerWeek,
            PricePerVisit = s.PricePerVisit,
            RecurringAmount = s.RecurringAmount,
            Currency = s.Currency,
            PriceBreakdown = new SubscriptionPriceBreakdownDTO
            {
                BasePrice = s.PriceBreakdown.BasePrice,
                FrequencyPerWeek = s.PriceBreakdown.FrequencyPerWeek,
                OrderFee = s.PriceBreakdown.OrderFee,
                ServiceCharge = s.PriceBreakdown.ServiceCharge,
                GatewayFees = s.PriceBreakdown.GatewayFees,
                TotalAmount = s.PriceBreakdown.TotalAmount
            },
            Status = s.Status.ToString(),
            AutoRenew = s.AutoRenew,
            IsServiceActive = s.IsServiceActive,
            CurrentPeriodStart = s.CurrentPeriodStart,
            CurrentPeriodEnd = s.CurrentPeriodEnd,
            NextChargeDate = s.NextChargeDate,
            BillingCyclesCompleted = s.BillingCyclesCompleted,
            RemainingDaysInPeriod = s.RemainingDaysInPeriod,
            CardLastFour = s.CardLastFour,
            CardBrand = s.CardBrand,
            CardExpiry = s.CardExpiry,
            CardUpdateState = string.IsNullOrWhiteSpace(s.CardUpdateState) ? "none" : s.CardUpdateState,
            PendingCardUpdateTxRef = s.PendingCardUpdateTxRef,
            CardUpdateStartedAt = s.CardUpdateStartedAt,
            CardUpdateCompletedAt = s.CardUpdateCompletedAt,
            CardUpdateFailureReason = s.CardUpdateFailureReason,
            CancelAtPeriodEnd = s.CancelAtPeriodEnd,
            CancellationRequestedAt = s.CancellationRequestedAt,
            CancellationReason = s.CancellationReason,
            FailedChargeAttempts = s.FailedChargeAttempts,
            LastChargeError = s.LastChargeError,
            CreatedAt = s.CreatedAt,
            UpdatedAt = s.UpdatedAt
        };

        private static decimal CalculateOrderFee(decimal basePrice, string serviceType, int frequencyPerWeek)
        {
            return serviceType switch
            {
                "one-time" => basePrice,
                "monthly" => basePrice * frequencyPerWeek * 4, // basePrice × visits/week × 4 weeks
                _ => basePrice
            };
        }

        private static decimal CalculateFlutterwaveFees(decimal amount)
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
    }
}
