using System;

namespace Application.DTOs
{
    // ── Wallet DTOs ──

    public class CaregiverWalletDTO
    {
        public string Id { get; set; } = string.Empty;
        public string CaregiverId { get; set; } = string.Empty;
        public decimal TotalEarned { get; set; }
        public decimal WithdrawableBalance { get; set; }
        public decimal PendingBalance { get; set; }
        public decimal TotalWithdrawn { get; set; }
        public long Version { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class WalletSummaryResponse
    {
        public string CaregiverId { get; set; } = string.Empty;
        public string CaregiverName { get; set; } = string.Empty;
        public decimal TotalEarned { get; set; }
        public decimal WithdrawableBalance { get; set; }
        public decimal PendingBalance { get; set; }
        public decimal TotalWithdrawn { get; set; }
    }

    // ── Ledger DTOs ──

    public class EarningsLedgerDTO
    {
        public string Id { get; set; } = string.Empty;
        public string CaregiverId { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string? ClientOrderId { get; set; }
        public string? ContractId { get; set; }
        public string? SubscriptionId { get; set; }
        public int? BillingCycleNumber { get; set; }
        public string? ServiceType { get; set; }
        public string Description { get; set; } = string.Empty;
        public decimal BalanceAfter { get; set; }
        public string? WithdrawalRequestId { get; set; }
        public string? ReleaseReason { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class LedgerHistoryResponse
    {
        public string Id { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Description { get; set; } = string.Empty;
        public string? ServiceType { get; set; }
        public int? BillingCycleNumber { get; set; }
        public decimal BalanceAfter { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    // ── Billing Record DTOs ──

    public class BillingRecordDTO
    {
        public string Id { get; set; } = string.Empty;
        public string OrderId { get; set; } = string.Empty;
        public string? SubscriptionId { get; set; }
        public string? ContractId { get; set; }
        public string CaregiverId { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string GigId { get; set; } = string.Empty;
        public int BillingCycleNumber { get; set; }
        public string ServiceType { get; set; } = string.Empty;
        public int FrequencyPerWeek { get; set; }
        public DateTime PeriodStart { get; set; }
        public DateTime? PeriodEnd { get; set; }
        public DateTime? NextChargeDate { get; set; }
        public decimal AmountPaid { get; set; }
        public decimal OrderFee { get; set; }
        public decimal ServiceCharge { get; set; }
        public decimal GatewayFees { get; set; }
        public string PaymentTransactionId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public class BillingRecordResponse
    {
        public string Id { get; set; } = string.Empty;
        public string OrderId { get; set; } = string.Empty;
        public string? SubscriptionId { get; set; }
        public string CaregiverName { get; set; } = string.Empty;
        public string ClientName { get; set; } = string.Empty;
        public string GigTitle { get; set; } = string.Empty;
        public int BillingCycleNumber { get; set; }
        public string ServiceType { get; set; } = string.Empty;
        public int FrequencyPerWeek { get; set; }
        public DateTime PeriodStart { get; set; }
        public DateTime? PeriodEnd { get; set; }
        public DateTime? NextChargeDate { get; set; }
        public decimal AmountPaid { get; set; }
        public decimal OrderFee { get; set; }
        public decimal ServiceCharge { get; set; }
        public decimal GatewayFees { get; set; }
        public string PaymentTransactionId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}
