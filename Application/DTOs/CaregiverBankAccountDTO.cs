using System;

namespace Application.DTOs
{
    /// <summary>
    /// Request DTO for creating or updating a caregiver's bank account info.
    /// </summary>
    public class CaregiverBankAccountRequest
    {
        public string FullName { get; set; } = string.Empty;
        public string BankName { get; set; } = string.Empty;
        public string AccountNumber { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
    }

    /// <summary>
    /// Response DTO returned when querying a caregiver's bank account info.
    /// </summary>
    public class CaregiverBankAccountResponse
    {
        public string Id { get; set; }
        public string CaregiverId { get; set; }
        public string FullName { get; set; }
        public string BankName { get; set; }
        public string AccountNumber { get; set; }
        public string AccountName { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// Admin-facing response combining caregiver profile, wallet, and bank account info.
    /// </summary>
    public class AdminCaregiverFinancialSummary
    {
        public string CaregiverId { get; set; }
        public string CaregiverName { get; set; }
        public string Email { get; set; }
        public string PhoneNo { get; set; }

        // Wallet info
        public decimal TotalEarned { get; set; }
        public decimal WithdrawableBalance { get; set; }
        public decimal PendingBalance { get; set; }
        public decimal TotalWithdrawn { get; set; }

        // Bank account info
        public CaregiverBankAccountResponse BankAccount { get; set; }
    }
}
