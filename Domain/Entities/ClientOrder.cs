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
    }
}
