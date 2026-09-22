using MongoDB.Bson;
using System;

namespace Domain.Entities
{
    /// <summary>
    /// Stores server-calculated payment details for an admin-initiated Package purchase
    /// before sending to Flutterwave, so the amount actually paid can be verified against
    /// what staff quoted. Structurally cloned from <see cref="BookingCommitment"/> — a
    /// standalone, non-Gig payment record with its own tx_ref prefix (CAREPRO-PKG-) — not
    /// from <see cref="PendingPayment"/>, which is Gig-purchase-specific.
    ///
    /// Unlike the client self-service Gig checkout, this payment is initiated by staff on
    /// behalf of a client (via WhatsApp), so there is no client-authenticated request to
    /// attach it to — <see cref="ClientId"/> and <see cref="PackageId"/> are supplied by
    /// the admin caller instead.
    /// </summary>
    public class PendingPackagePayment
    {
        public ObjectId Id { get; set; }

        /// <summary>Unique transaction reference sent to Flutterwave (tx_ref), prefixed CAREPRO-PKG-.</summary>
        public string TransactionReference { get; set; } = string.Empty;

        public string ClientId { get; set; } = string.Empty;

        public string PackageId { get; set; } = string.Empty;

        /// <summary>Extra-day add-on count, only meaningful when the Package has an AdditionalDayPrice.</summary>
        public int ExtraDays { get; set; }

        /// <summary>Snapshot of Package.BasePrice at the time the link was generated.</summary>
        public decimal BasePrice { get; set; }

        /// <summary>ExtraDays × Package.AdditionalDayPrice (0 when no add-on was selected).</summary>
        public decimal AdditionalDayAmount { get; set; }

        /// <summary>BasePrice + AdditionalDayAmount — what the client is charged.</summary>
        public decimal TotalAmount { get; set; }

        public string Currency { get; set; } = "NGN";

        /// <summary>Client's email, resolved server-side from the Client record — never staff-supplied.</summary>
        public string Email { get; set; } = string.Empty;

        public string RedirectUrl { get; set; } = string.Empty;

        public string? PaymentLink { get; set; }

        public PendingPackagePaymentStatus Status { get; set; } = PendingPackagePaymentStatus.Pending;

        public string? FlutterwaveTransactionId { get; set; }

        /// <summary>Optional staff note, carried over onto the resulting PackageRequest.</summary>
        public string? Notes { get; set; }

        /// <summary>The PackageRequest ID created after successful payment.</summary>
        public string? PackageRequestId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? CompletedAt { get; set; }

        public string? ErrorMessage { get; set; }

        /// <summary>Admin id who generated this link, for traceability.</summary>
        public string? InitiatedByAdminId { get; set; }
    }

    public enum PendingPackagePaymentStatus
    {
        Pending,
        Completed,
        Failed,
        Expired,
        AmountMismatch
    }
}
