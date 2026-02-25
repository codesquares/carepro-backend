using MongoDB.Bson;
using System;

namespace Domain.Entities
{
    /// <summary>
    /// Auto-generated billing receipt per payment (initial or recurring).
    /// Created alongside every ClientOrder to provide a clear financial record
    /// of what was paid, for what period, and when the next charge happens.
    /// </summary>
    public class BillingRecord
    {
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

        /// <summary>
        /// The ClientOrder this billing record belongs to
        /// </summary>
        public string OrderId { get; set; } = string.Empty;

        /// <summary>
        /// The subscription that generated this billing record (null for one-time)
        /// </summary>
        public string? SubscriptionId { get; set; }

        /// <summary>
        /// The service agreement contract (if linked)
        /// </summary>
        public string? ContractId { get; set; }

        public string CaregiverId { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string GigId { get; set; } = string.Empty;

        /// <summary>
        /// Which billing cycle: 1 for one-time or initial, 2+ for renewals
        /// </summary>
        public int BillingCycleNumber { get; set; } = 1;

        /// <summary>
        /// "one-time" or "monthly"
        /// </summary>
        public string ServiceType { get; set; } = string.Empty;

        /// <summary>
        /// Number of weekly visits (for monthly subscriptions, e.g. 3 = 3x per week)
        /// </summary>
        public int FrequencyPerWeek { get; set; } = 1;

        // ── Period Coverage ──

        /// <summary>
        /// Start of the service period this payment covers.
        /// For one-time: the order date.
        /// For recurring: the start of the billing cycle.
        /// </summary>
        public DateTime PeriodStart { get; set; }

        /// <summary>
        /// End of the service period.
        /// For one-time: null (or estimated completion date).
        /// For recurring: PeriodStart + 30 days.
        /// </summary>
        public DateTime? PeriodEnd { get; set; }

        /// <summary>
        /// When the client will be charged next.
        /// For one-time: null.
        /// For recurring: same as PeriodEnd (billing anchor).
        /// </summary>
        public DateTime? NextChargeDate { get; set; }

        // ── Financial Breakdown ──

        /// <summary>
        /// Total amount the client paid (includes all fees)
        /// </summary>
        public decimal AmountPaid { get; set; }

        /// <summary>
        /// The base service cost (what the caregiver earns before platform cut)
        /// </summary>
        public decimal OrderFee { get; set; }

        /// <summary>
        /// Platform service charge (10% of OrderFee)
        /// </summary>
        public decimal ServiceCharge { get; set; }

        /// <summary>
        /// Payment gateway processing fees (Flutterwave)
        /// </summary>
        public decimal GatewayFees { get; set; }

        /// <summary>
        /// Flutterwave transaction reference
        /// </summary>
        public string PaymentTransactionId { get; set; } = string.Empty;

        /// <summary>
        /// "Paid", "Refunded", "Disputed"
        /// </summary>
        public string Status { get; set; } = "Paid";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
