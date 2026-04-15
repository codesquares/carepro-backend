using MongoDB.Bson;
using System;
using System.Collections.Generic;

namespace Domain.Entities
{
    /// <summary>
    /// Tracks a recurring service subscription between a client and caregiver.
    /// Created after a successful initial payment for a weekly/monthly service.
    /// Manages billing cycles, auto-renewal, and service termination.
    /// </summary>
    public class Subscription
    {
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

        // ── Parties ──
        public string ClientId { get; set; } = string.Empty;
        public string CaregiverId { get; set; } = string.Empty;

        // ── Source references ──
        public string GigId { get; set; } = string.Empty;
        public string OriginalOrderId { get; set; } = string.Empty;
        public string? ContractId { get; set; }

        // ── Plan details (snapshot of what the client chose) ──
        /// <summary>
        /// "weekly" or "monthly"
        /// </summary>
        public string BillingCycle { get; set; } = string.Empty;

        /// <summary>
        /// Number of visits per week (1-7)
        /// </summary>
        public int FrequencyPerWeek { get; set; } = 1;

        /// <summary>
        /// Base price per visit from the gig
        /// </summary>
        public decimal PricePerVisit { get; set; }

        /// <summary>
        /// Calculated recurring charge amount (orderFee + serviceCharge + gateway fees)
        /// </summary>
        public decimal RecurringAmount { get; set; }

        /// <summary>
        /// Breakdown of the recurring payment
        /// </summary>
        public SubscriptionPriceBreakdown PriceBreakdown { get; set; } = new();

        public string Currency { get; set; } = "NGN";

        // ── Billing state ──
        public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Active;

        /// <summary>
        /// Start of the current billing period
        /// </summary>
        public DateTime CurrentPeriodStart { get; set; }

        /// <summary>
        /// End of the current billing period (next charge date)
        /// </summary>
        public DateTime CurrentPeriodEnd { get; set; }

        /// <summary>
        /// Next scheduled charge date (same as CurrentPeriodEnd unless paused)
        /// </summary>
        public DateTime? NextChargeDate { get; set; }

        /// <summary>
        /// Total number of completed billing cycles
        /// </summary>
        public int BillingCyclesCompleted { get; set; }

        // ── Flutterwave tokenization ──
        /// <summary>
        /// Flutterwave payment token for recurring charges (from initial payment)
        /// </summary>
        public string? FlutterwavePaymentToken { get; set; }

        /// <summary>
        /// Masked card info for display (e.g., "**** **** **** 4081")
        /// </summary>
        public string? CardLastFour { get; set; }

        /// <summary>
        /// Card brand (e.g., "VISA", "MASTERCARD")
        /// </summary>
        public string? CardBrand { get; set; }

        /// <summary>
        /// Card expiry (MM/YY)
        /// </summary>
        public string? CardExpiry { get; set; }

        /// <summary>
        /// Customer email used for Flutterwave
        /// </summary>
        public string Email { get; set; } = string.Empty;

        // ── Retry & failure tracking ──
        /// <summary>
        /// Number of consecutive failed charge attempts in current cycle
        /// </summary>
        public int FailedChargeAttempts { get; set; }

        /// <summary>
        /// Maximum retries before suspending (default: 3)
        /// </summary>
        public int MaxRetryAttempts { get; set; } = 3;

        /// <summary>
        /// Last failed charge error message
        /// </summary>
        public string? LastChargeError { get; set; }

        /// <summary>
        /// Date of last failed charge attempt
        /// </summary>
        public DateTime? LastFailedChargeAt { get; set; }

        // ── Auto-renewal ──
        /// <summary>
        /// Whether the subscription auto-renews at period end
        /// </summary>
        public bool AutoRenew { get; set; } = true;

        // ── Cancellation ──
        /// <summary>
        /// If set, the subscription will cancel at the end of the current period
        /// (service continues until CurrentPeriodEnd)
        /// </summary>
        public bool CancelAtPeriodEnd { get; set; }

        /// <summary>
        /// When cancellation was requested (null if not cancelled)
        /// </summary>
        public DateTime? CancellationRequestedAt { get; set; }

        /// <summary>
        /// Reason for cancellation provided by the user
        /// </summary>
        public string? CancellationReason { get; set; }

        /// <summary>
        /// Who initiated cancellation: "client", "caregiver", "admin", "system"
        /// </summary>
        public string? CancelledBy { get; set; }

        /// <summary>
        /// When the subscription was actually terminated
        /// </summary>
        public DateTime? TerminatedAt { get; set; }

        /// <summary>
        /// Refund amount issued on termination (if any)
        /// </summary>
        public decimal? RefundAmount { get; set; }

        /// <summary>
        /// Flutterwave refund transaction ID
        /// </summary>
        public string? RefundTransactionId { get; set; }

        // ── Plan change history ──
        /// <summary>
        /// History of plan changes (upgrade/downgrade)
        /// </summary>
        public List<PlanChangeRecord> PlanChangeHistory { get; set; } = new();

        // ── Payment history ──
        /// <summary>
        /// All payment attempts for this subscription
        /// </summary>
        public List<SubscriptionPaymentRecord> PaymentHistory { get; set; } = new();

        // ── Timestamps ──
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // ── Helpers ──
        /// <summary>
        /// Returns true if the subscription is in a state where service should be delivered
        /// </summary>
        public bool IsServiceActive =>
            Status == SubscriptionStatus.Active ||
            (Status == SubscriptionStatus.PendingCancellation && DateTime.UtcNow < CurrentPeriodEnd);

        /// <summary>
        /// Returns remaining days in the current billing period
        /// </summary>
        public int RemainingDaysInPeriod =>
            Math.Max(0, (int)(CurrentPeriodEnd - DateTime.UtcNow).TotalDays);

        /// <summary>
        /// Returns the number of days in the current billing cycle
        /// </summary>
        public int TotalDaysInPeriod =>
            Math.Max(1, (int)(CurrentPeriodEnd - CurrentPeriodStart).TotalDays);

        /// <summary>
        /// Calculates pro-rated refund amount based on remaining days
        /// </summary>
        public decimal CalculateProRatedRefund()
        {
            if (RemainingDaysInPeriod <= 0 || TotalDaysInPeriod <= 0)
                return 0;

            var dailyRate = RecurringAmount / TotalDaysInPeriod;
            return Math.Round(dailyRate * RemainingDaysInPeriod, 2);
        }
    }

    /// <summary>
    /// Snapshot of price breakdown stored on the subscription
    /// </summary>
    public class SubscriptionPriceBreakdown
    {
        public decimal BasePrice { get; set; }
        public int FrequencyPerWeek { get; set; }
        public decimal OrderFee { get; set; }
        public decimal ServiceCharge { get; set; }
        public decimal GatewayFees { get; set; }
        public decimal TotalAmount { get; set; }
    }

    /// <summary>
    /// Records a plan change (upgrade/downgrade)
    /// </summary>
    public class PlanChangeRecord
    {
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public string PreviousBillingCycle { get; set; } = string.Empty;
        public int PreviousFrequencyPerWeek { get; set; }
        public decimal PreviousAmount { get; set; }
        public string NewBillingCycle { get; set; } = string.Empty;
        public int NewFrequencyPerWeek { get; set; }
        public decimal NewAmount { get; set; }
        /// <summary>
        /// "upgrade", "downgrade", or "change"
        /// </summary>
        public string ChangeType { get; set; } = string.Empty;
        public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
        /// <summary>
        /// When the new plan takes effect (usually next billing cycle)
        /// </summary>
        public DateTime EffectiveDate { get; set; }
    }

    /// <summary>
    /// Records each payment attempt (successful or failed) for the subscription
    /// </summary>
    public class SubscriptionPaymentRecord
    {
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public string TransactionReference { get; set; } = string.Empty;
        public string? FlutterwaveTransactionId { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "NGN";
        public string Status { get; set; } = string.Empty; // "successful", "failed", "pending"
        public string? ErrorMessage { get; set; }
        public int BillingCycleNumber { get; set; }
        public DateTime AttemptedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }
        /// <summary>
        /// The ClientOrder ID created for this billing cycle (if payment succeeded)
        /// </summary>
        public string? ClientOrderId { get; set; }
    }

    public enum SubscriptionStatus
    {
        /// <summary>
        /// Subscription is active and will auto-renew
        /// </summary>
        Active,

        /// <summary>
        /// Client has requested cancellation; service continues until period end
        /// </summary>
        PendingCancellation,

        /// <summary>
        /// Payment failed and retries are in progress
        /// </summary>
        PastDue,

        /// <summary>
        /// All retry attempts exhausted; service suspended
        /// </summary>
        Suspended,

        /// <summary>
        /// Subscription paused by client or admin (can be resumed)
        /// </summary>
        Paused,

        /// <summary>
        /// Subscription has been cancelled and service has ended
        /// </summary>
        Cancelled,

        /// <summary>
        /// Subscription terminated immediately (with potential refund)
        /// </summary>
        Terminated,

        /// <summary>
        /// Subscription expired naturally (non-renewing plan ended)
        /// </summary>
        Expired,

        /// <summary>
        /// A recurring charge is currently being processed (prevents double-charge)
        /// </summary>
        Charging
    }
}
