using System;
using System.Collections.Generic;

namespace Application.DTOs
{
    // ══════════════════════════════════════════
    //  SUBSCRIPTION DTOs
    // ══════════════════════════════════════════

    /// <summary>
    /// Response representing a subscription
    /// </summary>
    public class SubscriptionDTO
    {
        public string Id { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string CaregiverId { get; set; } = string.Empty;
        public string GigId { get; set; } = string.Empty;
        public string OriginalOrderId { get; set; } = string.Empty;
        public string? ContractId { get; set; }

        // Plan
        public string BillingCycle { get; set; } = string.Empty;
        public int FrequencyPerWeek { get; set; }
        public decimal PricePerVisit { get; set; }
        public decimal RecurringAmount { get; set; }
        public string Currency { get; set; } = "NGN";
        public SubscriptionPriceBreakdownDTO PriceBreakdown { get; set; } = new();

        // State
        public string Status { get; set; } = string.Empty;
        public bool AutoRenew { get; set; }
        public bool IsServiceActive { get; set; }

        // Billing
        public DateTime CurrentPeriodStart { get; set; }
        public DateTime CurrentPeriodEnd { get; set; }
        public DateTime? NextChargeDate { get; set; }
        public int BillingCyclesCompleted { get; set; }
        public int RemainingDaysInPeriod { get; set; }

        // Card
        public string? CardLastFour { get; set; }
        public string? CardBrand { get; set; }
        public string? CardExpiry { get; set; }

        // Cancellation
        public bool CancelAtPeriodEnd { get; set; }
        public DateTime? CancellationRequestedAt { get; set; }
        public string? CancellationReason { get; set; }

        // Failure info
        public int FailedChargeAttempts { get; set; }
        public string? LastChargeError { get; set; }

        // Timestamps
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class SubscriptionPriceBreakdownDTO
    {
        public decimal BasePrice { get; set; }
        public int FrequencyPerWeek { get; set; }
        public decimal OrderFee { get; set; }
        public decimal ServiceCharge { get; set; }
        public decimal GatewayFees { get; set; }
        public decimal TotalAmount { get; set; }
    }

    // ── Subscription Creation (called internally after initial payment) ──

    /// <summary>
    /// Internal request to create a subscription after first successful payment.
    /// Not exposed via API — called by PendingPaymentService.
    /// </summary>
    public class CreateSubscriptionRequest
    {
        public string ClientId { get; set; } = string.Empty;
        public string CaregiverId { get; set; } = string.Empty;
        public string GigId { get; set; } = string.Empty;
        public string OrderId { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string BillingCycle { get; set; } = string.Empty; // "weekly" or "monthly"
        public int FrequencyPerWeek { get; set; }
        public decimal PricePerVisit { get; set; }
        public decimal RecurringAmount { get; set; }
        public SubscriptionPriceBreakdownDTO PriceBreakdown { get; set; } = new();
        public string Currency { get; set; } = "NGN";

        // Flutterwave token from initial payment
        public string? FlutterwavePaymentToken { get; set; }
        public string? CardLastFour { get; set; }
        public string? CardBrand { get; set; }
        public string? CardExpiry { get; set; }
    }

    // ── Cancel / Terminate ──

    /// <summary>
    /// Client requests to cancel at period end (graceful cancellation)
    /// </summary>
    public class CancelSubscriptionRequest
    {
        /// <summary>
        /// Reason for cancellation
        /// </summary>
        public string? Reason { get; set; }
    }

    /// <summary>
    /// Immediate termination with optional refund
    /// </summary>
    public class TerminateSubscriptionRequest
    {
        /// <summary>
        /// Reason for immediate termination
        /// </summary>
        public string Reason { get; set; } = string.Empty;

        /// <summary>
        /// Whether to issue a pro-rated refund for unused days
        /// </summary>
        public bool IssueProRatedRefund { get; set; } = true;
    }

    // ── Plan Change ──

    /// <summary>
    /// Request to change the subscription plan (upgrade/downgrade)
    /// Takes effect at the next billing cycle
    /// </summary>
    public class ChangePlanRequest
    {
        /// <summary>
        /// New billing cycle: "weekly" or "monthly"
        /// </summary>
        public string? NewBillingCycle { get; set; }

        /// <summary>
        /// New frequency per week (1-7)
        /// </summary>
        public int? NewFrequencyPerWeek { get; set; }
    }

    /// <summary>
    /// Response after a plan change request
    /// </summary>
    public class PlanChangeResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string ChangeType { get; set; } = string.Empty; // "upgrade", "downgrade", "change"
        public decimal CurrentAmount { get; set; }
        public decimal NewAmount { get; set; }
        public DateTime EffectiveDate { get; set; }
        public SubscriptionDTO Subscription { get; set; } = new();
    }

    // ── Payment Method Update ──

    /// <summary>
    /// Request to update the payment method for a subscription.
    /// Triggers a small verification charge via Flutterwave.
    /// </summary>
    public class UpdatePaymentMethodRequest
    {
        /// <summary>
        /// Redirect URL after Flutterwave card authorization
        /// </summary>
        public string RedirectUrl { get; set; } = string.Empty;
    }

    public class UpdatePaymentMethodResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        /// <summary>
        /// Flutterwave payment link for card authorization
        /// </summary>
        public string? AuthorizationLink { get; set; }
        public string? TransactionReference { get; set; }
    }

    // ── Pause / Resume ──

    public class PauseSubscriptionRequest
    {
        /// <summary>
        /// Reason for pausing
        /// </summary>
        public string? Reason { get; set; }

        /// <summary>
        /// Optional: auto-resume date. If not set, must be resumed manually.
        /// </summary>
        public DateTime? ResumeDate { get; set; }
    }

    // ── Billing History ──

    public class SubscriptionPaymentRecordDTO
    {
        public string Id { get; set; } = string.Empty;
        public string TransactionReference { get; set; } = string.Empty;
        public string? FlutterwaveTransactionId { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "NGN";
        public string Status { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
        public int BillingCycleNumber { get; set; }
        public DateTime AttemptedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string? ClientOrderId { get; set; }
    }

    public class PlanChangeRecordDTO
    {
        public string Id { get; set; } = string.Empty;
        public string PreviousBillingCycle { get; set; } = string.Empty;
        public int PreviousFrequencyPerWeek { get; set; }
        public decimal PreviousAmount { get; set; }
        public string NewBillingCycle { get; set; } = string.Empty;
        public int NewFrequencyPerWeek { get; set; }
        public decimal NewAmount { get; set; }
        public string ChangeType { get; set; } = string.Empty;
        public DateTime ChangedAt { get; set; }
        public DateTime EffectiveDate { get; set; }
    }

    // ── Summary / Dashboard ──

    /// <summary>
    /// Summary view for client dashboard showing all active subscriptions
    /// </summary>
    public class ClientSubscriptionSummary
    {
        public int TotalActiveSubscriptions { get; set; }
        public decimal TotalMonthlySpend { get; set; }
        public DateTime? NextPaymentDate { get; set; }
        public decimal NextPaymentAmount { get; set; }
        public List<SubscriptionDTO> Subscriptions { get; set; } = new();
    }

    /// <summary>
    /// Admin overview of subscription metrics
    /// </summary>
    public class SubscriptionAnalytics
    {
        public int TotalActive { get; set; }
        public int TotalPastDue { get; set; }
        public int TotalCancelled { get; set; }
        public int TotalSuspended { get; set; }
        public decimal MonthlyRecurringRevenue { get; set; }
        public decimal ChurnRate { get; set; }
        public int NewSubscriptionsThisMonth { get; set; }
        public int CancellationsThisMonth { get; set; }
    }
}
