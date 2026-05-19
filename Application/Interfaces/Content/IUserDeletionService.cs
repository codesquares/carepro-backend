using Application.DTOs;

namespace Application.Interfaces.Content
{
    public interface IUserDeletionService
    {
        /// <summary>
        /// Schedules a caregiver account for deletion after a 30-day grace period.
        /// Blocks if active orders, pending withdrawals, or non-zero wallet balance exist.
        /// Sends in-app notification and email confirmation on success.
        /// </summary>
        Task<AccountDeletionResult> RequestCaregiverAccountDeletionAsync(string caregiverId, string reason, string? origin = null);

        /// <summary>
        /// Schedules a client account for deletion after a 30-day grace period.
        /// Blocks if active orders exist.
        /// Sends in-app notification and email confirmation on success.
        /// </summary>
        Task<AccountDeletionResult> RequestClientAccountDeletionAsync(string clientId, string reason, string? origin = null);

        /// <summary>
        /// Cancels a pending caregiver account deletion within the 30-day grace period.
        /// Restores the account and all associated gigs back to their soft-deleted (restorable) state.
        /// </summary>
        Task<string> CancelCaregiverAccountDeletionAsync(string caregiverId);

        /// <summary>
        /// Cancels a pending client account deletion within the 30-day grace period.
        /// Restores the account.
        /// </summary>
        Task<string> CancelClientAccountDeletionAsync(string clientId);

        /// <summary>
        /// Admin-initiated deletion. Same logic as user self-deletion but bypasses
        /// the wallet-balance blocker, records an AdminAuditLog entry, and requires a reason.
        /// </summary>
        Task<AccountDeletionResult> AdminDeleteCaregiverAccountAsync(string caregiverId, string adminId, string adminEmail, string reason);

        /// <summary>
        /// Admin-initiated deletion for a client account.
        /// </summary>
        Task<AccountDeletionResult> AdminDeleteClientAccountAsync(string clientId, string adminId, string adminEmail, string reason);
    }
}
