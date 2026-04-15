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
    }
}
