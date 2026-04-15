using MongoDB.Bson;
using System;

namespace Domain.Entities
{
    /// <summary>
    /// Persistent wallet tracking a client's credit balance.
    /// Credits are added when visits are cancelled within policy (24h notice).
    /// Credits are applied automatically toward future service payments.
    /// All monetary fields are in NGN.
    /// </summary>
    public class ClientWallet
    {
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

        /// <summary>
        /// The client this wallet belongs to (1:1 relationship).
        /// </summary>
        public string ClientId { get; set; } = string.Empty;

        /// <summary>
        /// Current available credit balance that can be applied to future services.
        /// </summary>
        public decimal CreditBalance { get; set; }

        /// <summary>
        /// Lifetime cumulative total of all credits issued (cancellation refunds, adjustments, etc.).
        /// </summary>
        public decimal TotalCredited { get; set; }

        /// <summary>
        /// Lifetime cumulative total of all credits spent on services.
        /// </summary>
        public decimal TotalSpent { get; set; }

        /// <summary>
        /// Optimistic concurrency version. Incremented on every mutation.
        /// </summary>
        public long Version { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
