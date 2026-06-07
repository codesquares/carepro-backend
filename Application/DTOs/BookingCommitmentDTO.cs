namespace Application.DTOs
{
    /// <summary>
    /// Request to initiate a ₦5,000 booking commitment fee payment
    /// </summary>
    public class BookingCommitmentRequest
    {
        /// <summary>
        /// The gig to unlock messaging access for
        /// </summary>
        public string GigId { get; set; } = string.Empty;

        /// <summary>
        /// Customer email for Flutterwave checkout
        /// </summary>
        public string Email { get; set; } = string.Empty;

        /// <summary>
        /// Where to redirect after payment completion
        /// </summary>
        public string RedirectUrl { get; set; } = string.Empty;
    }

    /// <summary>
    /// Response after initiating or completing a booking commitment payment
    /// </summary>
    public class BookingCommitmentResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string TransactionReference { get; set; } = string.Empty;
        public string? PaymentLink { get; set; }
        public decimal Amount { get; set; }
        public decimal FlutterwaveFees { get; set; }
        public decimal TotalCharged { get; set; }
        public string Currency { get; set; } = "NGN";
    }

    /// <summary>
    /// Response for checking if a client has unlocked a gig
    /// </summary>
    public class CommitmentStatusResponse
    {
        public bool HasAccess { get; set; }
        public string? GigId { get; set; }
        public string? CaregiverId { get; set; }
        public DateTime? UnlockedAt { get; set; }
        public bool IsAppliedToOrder { get; set; }

        /// <summary>
        /// True when no commitment fee applies to this gig at all — e.g., special gigs created
        /// through the CareRequest hire path. The cart page should skip the commitment gate
        /// display entirely when this is true.
        /// </summary>
        public bool CommitmentNotRequired { get; set; }
    }

    /// <summary>
    /// Summary of a booking commitment for the client's commitments list.
    /// Used by GET /api/booking-commitment/client
    /// </summary>
    public class BookingCommitmentListItem
    {
        public string Id { get; set; } = string.Empty;
        public string GigId { get; set; } = string.Empty;
        public string CaregiverId { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Status { get; set; } = string.Empty;
        public string TransactionReference { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public bool IsAppliedToOrder { get; set; }
        public string? AppliedToOrderId { get; set; }
    }

    /// <summary>
    /// Request body for cancelling a booking commitment.
    /// The client must explicitly confirm they understand the fee is non-refundable.
    /// </summary>
    public class CancelCommitmentRequest
    {
        /// <summary>
        /// Must be true. Confirms the client acknowledges the ₦5,000 fee is forfeited.
        /// The endpoint will reject the request if this is false.
        /// </summary>
        public bool ConfirmForfeit { get; set; }
    }

    /// <summary>
    /// Response after a commitment cancellation.
    /// </summary>
    public class CancelCommitmentResponse
    {
        public string CommitmentId { get; set; } = string.Empty;
        public string GigId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime CancelledAt { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
