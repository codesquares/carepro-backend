using Application.DTOs;
using Domain.Entities;

namespace Application.Interfaces
{
    public interface IBookingCommitmentService
    {
        /// <summary>
        /// Initiates a ₦5,000 booking commitment fee payment via Flutterwave.
        /// This unlocks messaging access between the client and the caregiver for the specified gig.
        /// </summary>
        Task<Result<BookingCommitmentResponse>> InitiateCommitmentAsync(BookingCommitmentRequest request, string clientId);

        /// <summary>
        /// Completes a commitment payment after Flutterwave webhook confirmation.
        /// Sends receipt email to client and notifications to both parties.
        /// </summary>
        Task<Result<BookingCommitment>> CompleteCommitmentAsync(string transactionReference, string flutterwaveTransactionId, decimal paidAmount);

        /// <summary>
        /// Checks whether the client has a completed (active) commitment for the given gig.
        /// </summary>
        Task<bool> HasActiveCommitmentAsync(string clientId, string gigId);

        /// <summary>
        /// Checks whether the client has any completed commitment with the given caregiver.
        /// Used by ChatHub to gate messaging access.
        /// </summary>
        Task<bool> HasActiveCommitmentWithCaregiverAsync(string clientId, string caregiverId);

        /// <summary>
        /// Returns the completed, not-yet-applied commitment for a client+gig pair.
        /// Used by PendingPaymentService to deduct the ₦5,000 from the full gig payment.
        /// </summary>
        Task<BookingCommitment?> GetApplicableCommitmentAsync(string clientId, string gigId);

        /// <summary>
        /// Marks a commitment as applied to a specific order after full gig payment.
        /// </summary>
        Task MarkCommitmentAppliedAsync(string commitmentId, string orderId);

        /// <summary>
        /// Gets the commitment status for a specific transaction reference.
        /// </summary>
        Task<BookingCommitment?> GetByTransactionReferenceAsync(string transactionReference);

        /// <summary>
        /// Gets the commitment status check for a client and gig.
        /// </summary>
        Task<CommitmentStatusResponse> GetCommitmentStatusAsync(string clientId, string gigId);
    }
}
