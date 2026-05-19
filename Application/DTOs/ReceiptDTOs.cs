namespace Application.DTOs
{
    public class CommitmentReceiptData
    {
        public string TransactionReference { get; set; } = string.Empty;
        public string? FlutterwaveTransactionId { get; set; }
        public string ClientName { get; set; } = string.Empty;
        public string ClientEmail { get; set; } = string.Empty;
        public string CaregiverName { get; set; } = string.Empty;
        public string GigTitle { get; set; } = string.Empty;
        public decimal CommitmentFee { get; set; }
        public decimal GatewayFees { get; set; }
        public decimal TotalCharged { get; set; }
        public string Currency { get; set; } = "NGN";
        public DateTime PaidAt { get; set; }
    }

    public class OrderReceiptData
    {
        public string TransactionReference { get; set; } = string.Empty;
        public string? FlutterwaveTransactionId { get; set; }
        public string? ClientOrderId { get; set; }
        public string ClientName { get; set; } = string.Empty;
        public string ClientEmail { get; set; } = string.Empty;
        public string CaregiverName { get; set; } = string.Empty;
        public string GigTitle { get; set; } = string.Empty;
        public string ServiceType { get; set; } = string.Empty;
        public int FrequencyPerWeek { get; set; }
        public decimal BasePrice { get; set; }
        public decimal OrderFee { get; set; }
        public decimal ServiceCharge { get; set; }
        public decimal GatewayFees { get; set; }
        public decimal CommitmentFeeDeducted { get; set; }
        public decimal TotalCharged { get; set; }
        public string Currency { get; set; } = "NGN";
        public DateTime PaidAt { get; set; }
    }
}
