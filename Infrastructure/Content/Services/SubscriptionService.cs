using Application.DTOs;
using Application.Interfaces.Content;
using Domain.Entities;
using Infrastructure.Content.Data;
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
        private readonly INotificationService _notificationService;
        private readonly ILogger<SubscriptionService> _logger;
        private readonly IConfiguration _configuration;

        // Same fee structure as PendingPaymentService
        private const decimal SERVICE_CHARGE_RATE = 0.10m;
        private const decimal FLUTTERWAVE_FEE_RATE = 0.014m;
        private const decimal FLUTTERWAVE_FEE_CAP = 2000m;

        public SubscriptionService(
            CareProDbContext dbContext,
            IClientOrderService clientOrderService,
            FlutterwaveService flutterwaveService,
            INotificationService notificationService,
            ILogger<SubscriptionService> logger,
            IConfiguration configuration)
        {
            _dbContext = dbContext;
            _clientOrderService = clientOrderService;
            _flutterwaveService = flutterwaveService;
            _notificationService = notificationService;
            _logger = logger;
            _configuration = configuration;
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
            if (!new[] { "weekly", "monthly" }.Contains(request.BillingCycle))
                errors.Add("BillingCycle must be 'weekly' or 'monthly'.");
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
            var periodEnd = request.BillingCycle == "weekly"
                ? now.AddDays(7)
                : now.AddDays(30);

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
            await _notificationService.CreateNotificationAsync(
                request.ClientId,
                "system",
                "subscription_created",
                $"Your {request.BillingCycle} subscription has been activated. Next charge: {periodEnd:MMM dd, yyyy}.",
                "Subscription Activated",
                subscription.Id);

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
                TotalMonthlySpend = active.Sum(s =>
                    s.BillingCycle == "weekly" ? s.RecurringAmount * 4.33m : s.RecurringAmount),
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
            await _notificationService.CreateNotificationAsync(
                subscription.ClientId,
                "system",
                "subscription_cancellation_scheduled",
                $"Your subscription will be cancelled on {subscription.CurrentPeriodEnd:MMM dd, yyyy}. Service continues until then.",
                "Cancellation Scheduled",
                subscriptionId);

            // Notify caregiver
            await _notificationService.CreateNotificationAsync(
                subscription.CaregiverId,
                "system",
                "subscription_cancellation_notice",
                $"A client's subscription for your service will end on {subscription.CurrentPeriodEnd:MMM dd, yyyy}.",
                "Subscription Ending",
                subscriptionId);

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

            await _notificationService.CreateNotificationAsync(
                subscription.ClientId,
                "system",
                "subscription_reactivated",
                "Your subscription has been reactivated. Auto-renewal is back on.",
                "Subscription Reactivated",
                subscriptionId);

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

            var terminalStatuses = new[] { SubscriptionStatus.Cancelled, SubscriptionStatus.Terminated, SubscriptionStatus.Expired };
            if (terminalStatuses.Contains(subscription.Status))
                return Result<SubscriptionDTO>.Failure(new List<string>
                {
                    $"Subscription is already '{subscription.Status}'."
                });

            var now = DateTime.UtcNow;
            subscription.Status = SubscriptionStatus.Terminated;
            subscription.TerminatedAt = now;
            subscription.CancellationReason = request.Reason;
            subscription.CancelledBy = isClient ? "client" : "caregiver";
            subscription.AutoRenew = false;
            subscription.NextChargeDate = null;
            subscription.UpdatedAt = now;

            // Calculate pro-rated refund if requested
            if (request.IssueProRatedRefund)
            {
                var refundAmount = subscription.CalculateProRatedRefund();
                if (refundAmount > 0)
                {
                    subscription.RefundAmount = refundAmount;
                    _logger.LogInformation(
                        "Pro-rated refund of {RefundAmount} {Currency} calculated for subscription {SubscriptionId}. " +
                        "Remaining days: {RemainingDays}/{TotalDays}",
                        refundAmount, subscription.Currency, subscriptionId,
                        subscription.RemainingDaysInPeriod, subscription.TotalDaysInPeriod);

                    // TODO: Integrate with Flutterwave refund API when available
                    // For now, refund is recorded and admin handles manual refund
                }
            }

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation(
                "Subscription {SubscriptionId} terminated immediately by {Actor}. Reason: {Reason}",
                subscriptionId, subscription.CancelledBy, request.Reason);

            // Terminate the linked contract if exists
            if (!string.IsNullOrEmpty(subscription.ContractId))
            {
                var contract = await _dbContext.Contracts
                    .FirstOrDefaultAsync(c => c.Id == subscription.ContractId);
                if (contract != null && contract.Status != ContractStatus.Terminated && contract.Status != ContractStatus.Completed)
                {
                    contract.Status = ContractStatus.Terminated;
                    contract.UpdatedAt = now;
                    await _dbContext.SaveChangesAsync();
                    _logger.LogInformation("Linked contract {ContractId} also terminated", subscription.ContractId);
                }
            }

            // Notify both parties
            var refundMsg = subscription.RefundAmount > 0
                ? $" A pro-rated refund of {subscription.Currency} {subscription.RefundAmount:N2} will be processed."
                : "";

            await _notificationService.CreateNotificationAsync(
                subscription.ClientId,
                "system",
                "subscription_terminated",
                $"Your subscription has been terminated immediately.{refundMsg}",
                "Subscription Terminated",
                subscriptionId);

            await _notificationService.CreateNotificationAsync(
                subscription.CaregiverId,
                "system",
                "subscription_terminated",
                "A subscription for your service has been terminated.",
                "Subscription Terminated",
                subscriptionId);

            return Result<SubscriptionDTO>.Success(MapToDTO(subscription));
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

            if (!new[] { "weekly", "monthly" }.Contains(newCycle))
                return Result<PlanChangeResponse>.Failure(new List<string> { "BillingCycle must be 'weekly' or 'monthly'." });

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

            await _notificationService.CreateNotificationAsync(
                subscription.ClientId,
                "system",
                "subscription_plan_changed",
                $"Your plan has been {changeType}d. New amount: {subscription.Currency} {newTotal:N2}/{newCycle}. " +
                $"Changes take effect on {planChange.EffectiveDate:MMM dd, yyyy}.",
                "Plan Changed",
                subscriptionId);

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
                    request.RedirectUrl);

                var paymentLink = ExtractPaymentLink(flutterwaveResponse);
                if (string.IsNullOrEmpty(paymentLink))
                {
                    return Result<UpdatePaymentMethodResponse>.Failure(new List<string>
                    {
                        "Failed to initiate card verification with payment provider."
                    });
                }

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
            subscription.UpdatedAt = DateTime.UtcNow;

            // If subscription was suspended due to payment failures, reactivate it
            if (subscription.Status == SubscriptionStatus.Suspended)
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

            await _notificationService.CreateNotificationAsync(
                subscription.ClientId,
                "system",
                "payment_method_updated",
                $"Your payment method has been updated to {cardBrand} ****{cardLastFour}.",
                "Payment Method Updated",
                subscriptionId);

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

            await _notificationService.CreateNotificationAsync(
                subscription.ClientId,
                "system",
                "subscription_paused",
                "Your subscription has been paused. No charges will be made until you resume.",
                "Subscription Paused",
                subscriptionId);

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
            var periodEnd = subscription.BillingCycle == "weekly"
                ? now.AddDays(7)
                : now.AddDays(30);

            subscription.Status = SubscriptionStatus.Active;
            subscription.CurrentPeriodStart = now;
            subscription.CurrentPeriodEnd = periodEnd;
            subscription.NextChargeDate = periodEnd;
            subscription.AutoRenew = true;
            subscription.UpdatedAt = now;

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Subscription {SubscriptionId} resumed by {UserId}. Next charge: {NextCharge}",
                subscriptionId, userId, periodEnd);

            await _notificationService.CreateNotificationAsync(
                subscription.ClientId,
                "system",
                "subscription_resumed",
                $"Your subscription has been resumed. Next charge: {periodEnd:MMM dd, yyyy}.",
                "Subscription Resumed",
                subscriptionId);

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
                    s.Status == SubscriptionStatus.Active &&
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
                    var error = chargeResult?.ErrorMessage ?? "Charge failed";
                    paymentRecord.Status = "failed";
                    paymentRecord.ErrorMessage = error;
                    subscription.PaymentHistory.Add(paymentRecord);
                    await HandleFailedChargeAsync(subscriptionId, error);
                    return Result<SubscriptionPaymentRecordDTO>.Failure(new List<string> { error });
                }

                // Payment successful — create a new ClientOrder for this billing cycle
                var orderResult = await _clientOrderService.CreateClientOrderAsync(new AddClientOrderRequest
                {
                    ClientId = subscription.ClientId,
                    GigId = subscription.GigId,
                    PaymentOption = subscription.BillingCycle,
                    Amount = (int)subscription.RecurringAmount,
                    TransactionId = chargeResult.TransactionId
                });

                paymentRecord.Status = "successful";
                paymentRecord.FlutterwaveTransactionId = chargeResult.TransactionId;
                paymentRecord.CompletedAt = DateTime.UtcNow;
                paymentRecord.ClientOrderId = orderResult.Value?.Id;

                // Advance the billing period
                var now = DateTime.UtcNow;
                subscription.CurrentPeriodStart = now;
                subscription.CurrentPeriodEnd = subscription.BillingCycle == "weekly"
                    ? now.AddDays(7)
                    : now.AddDays(30);
                subscription.NextChargeDate = subscription.CurrentPeriodEnd;
                subscription.BillingCyclesCompleted = cycleNumber;
                subscription.FailedChargeAttempts = 0;
                subscription.LastChargeError = null;
                subscription.PaymentHistory.Add(paymentRecord);
                subscription.UpdatedAt = now;

                await _dbContext.SaveChangesAsync();

                _logger.LogInformation(
                    "Recurring charge successful for subscription {SubscriptionId}. Cycle #{Cycle}, Amount: {Amount}, OrderId: {OrderId}",
                    subscriptionId, cycleNumber, subscription.RecurringAmount, orderResult.Value?.Id);

                // Notify client of successful charge
                await _notificationService.CreateNotificationAsync(
                    subscription.ClientId,
                    "system",
                    "recurring_payment_successful",
                    $"Your {subscription.BillingCycle} subscription payment of {subscription.Currency} {subscription.RecurringAmount:N2} was successful. " +
                    $"Next charge: {subscription.CurrentPeriodEnd:MMM dd, yyyy}.",
                    "Payment Successful",
                    subscriptionId);

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

                await _notificationService.CreateNotificationAsync(
                    subscription.ClientId,
                    "system",
                    "subscription_suspended",
                    "Your subscription has been suspended due to repeated payment failures. Please update your payment method to continue service.",
                    "Subscription Suspended",
                    subscriptionId);
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

                await _notificationService.CreateNotificationAsync(
                    subscription.ClientId,
                    "system",
                    "payment_failed",
                    $"Your subscription payment failed: {errorMessage}. We'll retry automatically. Please ensure your payment method is up to date.",
                    "Payment Failed",
                    subscriptionId);
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

            await _notificationService.CreateNotificationAsync(
                subscription.ClientId,
                "system",
                "subscription_cancelled",
                "Your subscription has ended. Thank you for using CarePro.",
                "Subscription Ended",
                subscriptionId);

            await _notificationService.CreateNotificationAsync(
                subscription.CaregiverId,
                "system",
                "subscription_ended",
                "A client's subscription for your service has ended.",
                "Subscription Ended",
                subscriptionId);
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
                "weekly" => basePrice * frequencyPerWeek,
                "monthly" => basePrice * frequencyPerWeek * 4,
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
