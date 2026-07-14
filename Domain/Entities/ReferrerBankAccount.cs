using MongoDB.Bson;

namespace Domain.Entities
{
    public class ReferrerBankAccount
    {
        public ObjectId Id { get; set; } = ObjectId.GenerateNewId();
        public string ReferrerId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string BankName { get; set; } = string.Empty;
        public string AccountNumber { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
