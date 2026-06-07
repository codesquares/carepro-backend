using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Entities
{
    public class ClientOrder
    {
        public ObjectId Id { get; set; }
        public string ClientId { get; set; }
        public string GigId { get; set; }
        public string CaregiverId { get; set; }
        public string PaymentOption { get; set; }
        public int Amount { get; set; }

        /// <summary>
        /// The caregiver's OrderFee (base service cost before platform/gateway fees).
        /// Used for wallet crediting — caregiver receives OrderFee minus 20% platform commission.
        /// For orders where a commitment fee was charged, this reflects the full gig price
        /// (commitment fee added back) so the caregiver receives their correct earnings.
        /// </summary>
        public decimal? OrderFee { get; set; }

        /// <summary>
        /// The commitment fee (₦5,000) that was deducted from the client's second payment.
        /// Stored for audit purposes. OrderFee already includes this amount added back.
        /// Null on orders created before this field was introduced or where no commitment applied.
        /// </summary>
        public decimal? CommitmentFeeDeducted { get; set; }

        public string TransactionId { get; set; }

        /// <summary>
        /// The CAREPRO- prefixed transaction reference used for receipt download.
        /// Null on orders created before this field was introduced.
        /// </summary>
        public string? TransactionReference { get; set; }

        public string? ClientOrderStatus { get; set; }
        public bool IsOrderStatusApproved { get; set; }
        public DateTime OrderCreatedAt { get; set; }

        public DateTime? OrderUpdatedOn { get; set; }
        public string? DisputeReason { get; set; }
        public bool? HasDispute { get; set; }

        // ── Recurring service tracking ──
        /// <summary>
        /// For recurring orders: the subscription ID that generated this order
        /// </summary>
        public string? SubscriptionId { get; set; }

        /// <summary>
        /// Billing cycle number this order belongs to (1 = initial, 2+ = recurring)
        /// </summary>
        public int? BillingCycleNumber { get; set; }

        /// <summary>
        /// Service frequency per week (1-7) — preserved from payment choice
        /// </summary>
        public int? FrequencyPerWeek { get; set; }

        /// <summary>
        /// Service type detail: "one-time", "weekly", "monthly"
        /// </summary>
        public string? ServiceType { get; set; }
    }
}
