using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Entities
{
    public class WithdrawalRequest
    {
        public ObjectId Id { get; set; }
        public string CaregiverId { get; set; }
        public decimal AmountRequested { get; set; }
        public decimal ServiceCharge { get; set; }
        public decimal FinalAmount { get; set; }
        public string Token { get; set; }
        public string Status { get; set; } // Pending, Verified, Completed
        public DateTime CreatedAt { get; set; }
        public DateTime? VerifiedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string? AdminNotes { get; set; }
        public string? AdminId { get; set; } // ID of the admin who processed the withdrawal
        public string? AccountNumber { get; set; } // Caregiver's bank account number
        public string? BankName { get; set; } // Caregiver's bank name
        public string? AccountName { get; set; } // Caregiver's bank account name
    }

    // Constants for withdrawal statuses
    public static class WithdrawalStatus
    {
        public const string Pending = "Pending";
        public const string Verified = "Verified";
        public const string Completed = "Completed";
        public const string Rejected = "Rejected";
    }
}
