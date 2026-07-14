namespace Application.DTOs
{
    public class CreateReferrerRequest
    {
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? PhoneNo { get; set; }
        public ReferrerBankAccountRequest? BankAccount { get; set; }
    }

    public class ReferrerBankAccountRequest
    {
        public string FullName { get; set; } = string.Empty;
        public string BankName { get; set; } = string.Empty;
        public string AccountNumber { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
    }

    public class CreateReferralCodeRequest
    {
        public string ReferrerId { get; set; } = string.Empty;
    }

    public class ReferrerListItem
    {
        public string Id { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? PhoneNo { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class ReferralCheckoutApplication
    {
        public string ReferralCodeId { get; set; } = string.Empty;
        public string ReferrerId { get; set; } = string.Empty;
        public decimal DiscountAmount { get; set; }
    }

    public class ReferralRedemptionReportItem
    {
        public string RedemptionId { get; set; } = string.Empty;
        public string ReferralCode { get; set; } = string.Empty;
        public string ReferrerName { get; set; } = string.Empty;
        public string ReferrerEmail { get; set; } = string.Empty;
        public string? ReferrerPhoneNo { get; set; }
        public string ClientId { get; set; } = string.Empty;
        public string OrderId { get; set; } = string.Empty;
        public decimal DiscountAmount { get; set; }
        public string PayoutStatus { get; set; } = string.Empty;
        public decimal PayoutAmount { get; set; }
        public DateTime RedeemedAt { get; set; }
        public DateTime? PaidAt { get; set; }
    }
}
