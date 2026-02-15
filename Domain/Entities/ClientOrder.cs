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
        public string TransactionId { get; set; }
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
