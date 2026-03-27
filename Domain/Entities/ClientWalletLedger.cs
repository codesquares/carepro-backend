using MongoDB.Bson;
using System;

namespace Domain.Entities
{
    /// <summary>
    /// Immutable audit log of every credit/debit event affecting a client's wallet.
    /// Positive amounts are credits, negative are debits.
    /// </summary>
    public class ClientWalletLedger
    {
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

        public string ClientId { get; set; } = string.Empty;

        /// <summary>
        /// The type of wallet event.
        /// </summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// Positive for credits, negative for debits.
        /// </summary>
        public decimal Amount { get; set; }

        /// <summary>
        /// The order associated with this entry (if applicable).
        /// </summary>
        public string? ClientOrderId { get; set; }

        /// <summary>
        /// The task sheet (visit) this entry relates to — for visit cancellation credits.
        /// </summary>
        public string? TaskSheetId { get; set; }

        /// <summary>
        /// Human-readable description.
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Snapshot of CreditBalance after this entry was applied.
        /// </summary>
        public decimal BalanceAfter { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public static class ClientLedgerEntryType
    {
        public const string CancellationCredit = "CancellationCredit";
        public const string CreditApplied = "CreditApplied";
        public const string Adjustment = "Adjustment";
    }
}
