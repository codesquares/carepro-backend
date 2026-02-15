using Application.DTOs;
using Domain.Entities;

namespace Application.Interfaces.Content
{
    public interface ISubscriptionService
    {
        // ── Subscription Lifecycle ──

        /// <summary>
        /// Creates a new subscription after a successful initial payment for a recurring service.
        /// Called internally by PendingPaymentService, not exposed via API directly.
        /// </summary>
        Task<Result<SubscriptionDTO>> CreateSubscriptionAsync(CreateSubscriptionRequest request);

        /// <summary>
        /// Gets a subscription by ID
        /// </summary>
        Task<SubscriptionDTO?> GetSubscriptionByIdAsync(string subscriptionId);

        /// <summary>
        /// Gets a subscription by the original order ID
        /// </summary>
        Task<SubscriptionDTO?> GetSubscriptionByOrderIdAsync(string orderId);

        /// <summary>
        /// Gets all subscriptions for a client
        /// </summary>
        Task<List<SubscriptionDTO>> GetClientSubscriptionsAsync(string clientId);

        /// <summary>
        /// Gets all subscriptions for a caregiver
        /// </summary>
        Task<List<SubscriptionDTO>> GetCaregiverSubscriptionsAsync(string caregiverId);

        /// <summary>
        /// Gets a summary of all client subscriptions for dashboard display
        /// </summary>
        Task<ClientSubscriptionSummary> GetClientSubscriptionSummaryAsync(string clientId);

        // ── Cancellation & Termination ──

        /// <summary>
        /// Graceful cancellation: service continues until end of current period, then stops.
        /// The client can still use the service until CurrentPeriodEnd.
        /// </summary>
        Task<Result<SubscriptionDTO>> CancelSubscriptionAsync(string subscriptionId, string userId, CancelSubscriptionRequest request);

        /// <summary>
        /// Reverses a pending cancellation (re-enables auto-renewal).
        /// Only works if status is PendingCancellation and period hasn't ended.
        /// </summary>
        Task<Result<SubscriptionDTO>> ReactivateSubscriptionAsync(string subscriptionId, string userId);

        /// <summary>
        /// Immediate termination with optional pro-rated refund.
        /// Service stops immediately. Used for contract disputes or admin action.
        /// </summary>
        Task<Result<SubscriptionDTO>> TerminateSubscriptionAsync(string subscriptionId, string userId, TerminateSubscriptionRequest request);

        // ── Plan Changes ──

        /// <summary>
        /// Changes the subscription plan (billing cycle or frequency).
        /// Takes effect at the start of the next billing cycle.
        /// </summary>
        Task<Result<PlanChangeResponse>> ChangePlanAsync(string subscriptionId, string userId, ChangePlanRequest request);

        /// <summary>
        /// Gets the plan change history for a subscription
        /// </summary>
        Task<List<PlanChangeRecordDTO>> GetPlanChangeHistoryAsync(string subscriptionId);

        // ── Payment Method ──

        /// <summary>
        /// Initiates payment method update by creating a small Flutterwave charge
        /// to capture a new card token.
        /// </summary>
        Task<Result<UpdatePaymentMethodResponse>> InitiatePaymentMethodUpdateAsync(string subscriptionId, string userId, UpdatePaymentMethodRequest request);

        /// <summary>
        /// Completes payment method update after card authorization webhook
        /// </summary>
        Task<Result<SubscriptionDTO>> CompletePaymentMethodUpdateAsync(string subscriptionId, string flutterwaveToken, string cardLastFour, string cardBrand, string cardExpiry);

        // ── Pause / Resume ──

        /// <summary>
        /// Pauses a subscription. No charges during pause. Can be resumed.
        /// </summary>
        Task<Result<SubscriptionDTO>> PauseSubscriptionAsync(string subscriptionId, string userId, PauseSubscriptionRequest request);

        /// <summary>
        /// Resumes a paused subscription. Creates a new billing period starting now.
        /// </summary>
        Task<Result<SubscriptionDTO>> ResumeSubscriptionAsync(string subscriptionId, string userId);

        // ── Recurring Billing (called by background service) ──

        /// <summary>
        /// Gets all subscriptions due for billing (NextChargeDate <= now, status Active)
        /// </summary>
        Task<List<Subscription>> GetSubscriptionsDueForBillingAsync();

        /// <summary>
        /// Processes a recurring charge for a subscription. 
        /// Creates a new ClientOrder on success.
        /// </summary>
        Task<Result<SubscriptionPaymentRecordDTO>> ProcessRecurringChargeAsync(string subscriptionId);

        /// <summary>
        /// Handles a failed recurring charge (increment retry count, check max retries)
        /// </summary>
        Task HandleFailedChargeAsync(string subscriptionId, string errorMessage);

        /// <summary>
        /// Gets subscriptions that are PendingCancellation and past their period end
        /// </summary>
        Task<List<Subscription>> GetSubscriptionsPendingFinalCancellationAsync();

        /// <summary>
        /// Finalizes cancellation for subscriptions past their period end
        /// </summary>
        Task FinalizeCancellationAsync(string subscriptionId);

        // ── Billing History ──

        /// <summary>
        /// Gets all payment records for a subscription
        /// </summary>
        Task<List<SubscriptionPaymentRecordDTO>> GetPaymentHistoryAsync(string subscriptionId);

        // ── Admin ──

        /// <summary>
        /// Gets subscription analytics for admin dashboard
        /// </summary>
        Task<SubscriptionAnalytics> GetSubscriptionAnalyticsAsync();

        /// <summary>
        /// Admin: gets all subscriptions with optional status filter
        /// </summary>
        Task<List<SubscriptionDTO>> GetAllSubscriptionsAsync(string? statusFilter = null);

        /// <summary>
        /// Links a contract to a subscription
        /// </summary>
        Task<Result<SubscriptionDTO>> LinkContractAsync(string subscriptionId, string contractId);
    }
}
