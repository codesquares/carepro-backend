using MongoDB.Bson;
using System;

namespace Domain.Entities
{
    /// <summary>
    /// Stores a caregiver's bank account information for withdrawals.
    /// One bank account record per caregiver (1:1 relationship).
    /// </summary>
    public class CaregiverBankAccount
    {
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public string CaregiverId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string BankName { get; set; } = string.Empty;
        public string AccountNumber { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
