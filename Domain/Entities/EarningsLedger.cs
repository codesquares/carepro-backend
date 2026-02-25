using MongoDB.Bson;
using System;

namespace Domain.Entities
{
    /// <summary>
    /// Immutable audit log of every financial event affecting a caregiver's wallet.
    /// Acts as a double-entry ledger — positive amounts are credits, negative are debits.
    /// The wallet balances can always be reconstructed by replaying the ledger.
    /// </summary>
    public class EarningsLedger
    {
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

        public string CaregiverId { get; set; } = string.Empty;

        /// <summary>
        /// The type of financial event.
        /// Values: "OrderReceived", "FundsReleased", "WithdrawalCompleted", "Refund", "DisputeHold", "Adjustment"
        /// </summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// Positive for credits (earnings, releases), negative for debits (withdrawals, refunds).
        /// </summary>
        public decimal Amount { get; set; }

        /// <summary>
        /// The ClientOrder that triggered this ledger entry (null for adjustments).
        /// </summary>
        public string? ClientOrderId { get; set; }

        /// <summary>
        /// The contract governing this transaction (null if no contract yet).
        /// </summary>
        public string? ContractId { get; set; }

        /// <summary>
        /// The subscription this entry relates to (null for one-time orders).
        /// </summary>
        public string? SubscriptionId { get; set; }

        /// <summary>
        /// Which billing cycle this entry belongs to (null for one-time).
        /// </summary>
        public int? BillingCycleNumber { get; set; }

        /// <summary>
        /// "one-time" or "monthly"
        /// </summary>
        public string? ServiceType { get; set; }

        /// <summary>
        /// Human-readable description of the event.
        /// e.g. "Payment received for monthly home care - cycle 3"
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Snapshot of WithdrawableBalance after this entry was applied.
        /// Used for quick balance history display.
        /// </summary>
        public decimal BalanceAfter { get; set; }

        /// <summary>
        /// Optional reference to a withdrawal request ID.
        /// </summary>
        public string? WithdrawalRequestId { get; set; }

        /// <summary>
        /// Release reason for FundsReleased entries:
        /// "ClientApproved", "AutoReleased", "RecurringPayment", "InitialSubscription"
        /// </summary>
        public string? ReleaseReason { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Constants for EarningsLedger.Type values
    /// </summary>
    public static class LedgerEntryType
    {
        public const string OrderReceived = "OrderReceived";
        public const string FundsReleased = "FundsReleased";
        public const string WithdrawalCompleted = "WithdrawalCompleted";
        public const string Refund = "Refund";
        public const string DisputeHold = "DisputeHold";
        public const string Adjustment = "Adjustment";
    }

    /// <summary>
    /// Constants for EarningsLedger.ReleaseReason values
    /// </summary>
    public static class FundsReleaseReason
    {
        public const string ClientApproved = "ClientApproved";
        public const string AutoReleased = "AutoReleased";
        public const string RecurringPayment = "RecurringPayment";
        public const string InitialSubscription = "InitialSubscription";
    }
}
