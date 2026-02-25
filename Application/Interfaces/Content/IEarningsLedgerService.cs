using Application.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    public interface IEarningsLedgerService
    {
        /// <summary>
        /// Records an OrderReceived ledger entry when a new order is created.
        /// </summary>
        Task RecordOrderReceivedAsync(string caregiverId, decimal amount, string clientOrderId,
            string? subscriptionId, int? billingCycleNumber, string serviceType, string description);

        /// <summary>
        /// Records a FundsReleased ledger entry when funds move to withdrawable.
        /// </summary>
        Task RecordFundsReleasedAsync(string caregiverId, decimal amount, string clientOrderId,
            string? subscriptionId, int? billingCycleNumber, string serviceType,
            string releaseReason, string description);

        /// <summary>
        /// Records a WithdrawalCompleted ledger entry (negative amount).
        /// </summary>
        Task RecordWithdrawalAsync(string caregiverId, decimal amount, string withdrawalRequestId, string description);

        /// <summary>
        /// Records a Refund ledger entry (negative amount).
        /// </summary>
        Task RecordRefundAsync(string caregiverId, decimal amount, string? clientOrderId,
            string? subscriptionId, string description);

        /// <summary>
        /// Records a DisputeHold ledger entry.
        /// </summary>
        Task RecordDisputeHoldAsync(string caregiverId, decimal amount, string clientOrderId, string description);

        /// <summary>
        /// Gets the full ledger history for a caregiver, ordered by most recent first.
        /// </summary>
        Task<List<LedgerHistoryResponse>> GetLedgerHistoryAsync(string caregiverId, int? limit = null);

        /// <summary>
        /// Gets combined transaction history (ledger entries) for the caregiver.
        /// Replaces the old EarningsService.GetCaregiverTransactionHistoryAsync.
        /// </summary>
        Task<List<TransactionHistoryResponse>> GetTransactionHistoryAsync(string caregiverId);

        /// <summary>
        /// Checks if a FundsReleased entry already exists for a given order (prevents double-release).
        /// </summary>
        Task<bool> HasFundsBeenReleasedForOrderAsync(string clientOrderId);
    }
}
