using MongoDB.Bson;
using System;

namespace Domain.Entities
{
    /// <summary>
    /// Persistent wallet tracking a caregiver's financial state.
    /// One wallet per caregiver, created at registration time.
    /// All monetary fields are in NGN.
    /// </summary>
    public class CaregiverWallet
    {
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

        /// <summary>
        /// The caregiver this wallet belongs to (1:1 relationship)
        /// </summary>
        public string CaregiverId { get; set; } = string.Empty;

        /// <summary>
        /// Lifetime cumulative total of all order amounts attributed to this caregiver.
        /// Incremented every time a ClientOrder is created for a gig they own.
        /// </summary>
        public decimal TotalEarned { get; set; }

        /// <summary>
        /// Money currently available for withdrawal.
        /// Incremented when funds are released (client approval, auto-release, or recurring payment).
        /// Decremented when a withdrawal is completed or a refund is issued.
        /// </summary>
        public decimal WithdrawableBalance { get; set; }

        /// <summary>
        /// Money earned but still in hold period (one-time orders awaiting approval or 7-day auto-release).
        /// Moves to WithdrawableBalance when released.
        /// </summary>
        public decimal PendingBalance { get; set; }

        /// <summary>
        /// Lifetime cumulative total of all completed withdrawals.
        /// </summary>
        public decimal TotalWithdrawn { get; set; }

        /// <summary>
        /// Optimistic concurrency version. Incremented on every mutation.
        /// If a stale version is detected on save, the operation is rejected — 
        /// protecting against race conditions in multi-instance deployments.
        /// </summary>
        public long Version { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
