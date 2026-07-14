using MongoDB.Bson;

namespace Domain.Entities
{
    public class ReferralCode
    {
        public ObjectId Id { get; set; } = ObjectId.GenerateNewId();
        public string Code { get; set; } = string.Empty;
        public string ReferrerId { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsActive { get; set; } = true;
    }
}
