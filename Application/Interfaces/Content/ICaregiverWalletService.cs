using Application.DTOs;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    public interface ICaregiverWalletService
    {
        /// <summary>
        /// Gets the wallet for a caregiver, creating one if it doesn't exist (lazy initialization).
        /// </summary>
        Task<CaregiverWalletDTO> GetOrCreateWalletAsync(string caregiverId);

        /// <summary>
        /// Gets wallet summary with caregiver name for display.
        /// </summary>
        Task<WalletSummaryResponse> GetWalletSummaryAsync(string caregiverId);

        /// <summary>
        /// Increments TotalEarned when any order is created for the caregiver's gig.
        /// Also increments PendingBalance for one-time orders.
        /// </summary>
        Task CreditOrderReceivedAsync(string caregiverId, decimal amount, bool isRecurring);

        /// <summary>
        /// Releases funds from PendingBalance to WithdrawableBalance (for one-time orders).
        /// </summary>
        Task ReleasePendingFundsAsync(string caregiverId, decimal amount);

        /// <summary>
        /// Credits WithdrawableBalance directly (for recurring subscription payments).
        /// </summary>
        Task CreditRecurringPaymentAsync(string caregiverId, decimal amount);

        /// <summary>
        /// Debits WithdrawableBalance and increments TotalWithdrawn when a withdrawal completes.
        /// </summary>
        Task DebitWithdrawalAsync(string caregiverId, decimal amount);

        /// <summary>
        /// Debits WithdrawableBalance for refunds (e.g., subscription termination with pro-rated refund).
        /// </summary>
        Task DebitRefundAsync(string caregiverId, decimal amount);

        /// <summary>
        /// Freezes pending funds when a dispute is raised on an unreleased order.
        /// (Amounts stay in PendingBalance but are not eligible for auto-release.)
        /// </summary>
        Task<bool> HasSufficientWithdrawableBalance(string caregiverId, decimal amount);
    }
}
