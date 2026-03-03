using MongoDB.Bson;
using System;

namespace Domain.Entities
{
    /// <summary>
    /// Tracks the ₦5,000 booking commitment fee a client pays to unlock
    /// messaging access with a caregiver for a specific gig.
    /// This amount is later deducted from the full gig payment.
    /// No ledger, wallet, or order is created — only a transaction receipt + notifications.
    /// </summary>
    public class BookingCommitment
    {
        public ObjectId Id { get; set; }

        /// <summary>
        /// The client who paid the commitment fee
        /// </summary>
        public string ClientId { get; set; } = string.Empty;

        /// <summary>
        /// The caregiver who owns the gig (denormalised for fast chat-gate lookups)
        /// </summary>
        public string CaregiverId { get; set; } = string.Empty;

        /// <summary>
        /// The gig being unlocked
        /// </summary>
        public string GigId { get; set; } = string.Empty;

        /// <summary>
        /// Commitment fee amount (always ₦5,000)
        /// </summary>
        public decimal Amount { get; set; }

        /// <summary>
        /// Flutterwave gateway fees on the commitment amount
        /// </summary>
        public decimal FlutterwaveFees { get; set; }

        /// <summary>
        /// Total charged to the client (Amount + FlutterwaveFees)
        /// </summary>
        public decimal TotalCharged { get; set; }

        /// <summary>
        /// Unique transaction reference sent to Flutterwave (tx_ref)
        /// </summary>
        public string TransactionReference { get; set; } = string.Empty;

        /// <summary>
        /// Flutterwave's transaction ID (populated after webhook confirmation)
        /// </summary>
        public string? FlutterwaveTransactionId { get; set; }

        /// <summary>
        /// Customer email for Flutterwave checkout
        /// </summary>
        public string Email { get; set; } = string.Empty;

        /// <summary>
        /// Frontend redirect URL after payment
        /// </summary>
        public string RedirectUrl { get; set; } = string.Empty;

        /// <summary>
        /// Flutterwave payment link
        /// </summary>
        public string? PaymentLink { get; set; }

        /// <summary>
        /// Payment status
        /// </summary>
        public BookingCommitmentStatus Status { get; set; } = BookingCommitmentStatus.Pending;

        /// <summary>
        /// When the commitment was created
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When the payment was confirmed via webhook
        /// </summary>
        public DateTime? CompletedAt { get; set; }

        /// <summary>
        /// Whether this commitment fee has been deducted from a full gig payment
        /// </summary>
        public bool IsAppliedToOrder { get; set; } = false;

        /// <summary>
        /// The ClientOrder ID to which this commitment fee was applied as a deduction
        /// </summary>
        public string? AppliedToOrderId { get; set; }

        /// <summary>
        /// Error message if payment failed
        /// </summary>
        public string? ErrorMessage { get; set; }
    }

    public enum BookingCommitmentStatus
    {
        Pending,
        Completed,
        Failed,
        Expired,
        AmountMismatch
    }
}
