using MongoDB.Bson;

namespace Domain.Entities
{
    public class ReferralRedemption
    {
        public ObjectId Id { get; set; } = ObjectId.GenerateNewId();
        public string ReferralCodeId { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string OrderId { get; set; } = string.Empty;
        public decimal DiscountAmount { get; set; }
        public DateTime RedeemedAt { get; set; } = DateTime.UtcNow;
        public string PayoutStatus { get; set; } = ReferralPayoutStatus.Pending;
        public decimal PayoutAmount { get; set; }
        public DateTime? PaidAt { get; set; }
    }

    public static class ReferralPayoutStatus
    {
        public const string Pending = "Pending";
        public const string Paid = "Paid";
    }
}
