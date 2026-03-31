using MongoDB.Bson;
using System;

namespace Domain.Entities
{
    /// <summary>
    /// A client's request to withdraw funds from their wallet back to their bank account.
    /// Admins review and process these requests.
    /// </summary>
    public class RefundRequest
    {
        public ObjectId Id { get; set; } = ObjectId.GenerateNewId();

        public string ClientId { get; set; } = string.Empty;

        /// <summary>
        /// Amount the client is requesting to be refunded (in NGN).
        /// </summary>
        public decimal Amount { get; set; }

        /// <summary>
        /// Client-provided reason for the refund request.
        /// </summary>
        public string Reason { get; set; } = string.Empty;

        /// <summary>
        /// Current status: Pending, Approved, Rejected, Processing, Completed.
        /// </summary>
        public string Status { get; set; } = RefundRequestStatus.Pending;

        /// <summary>
        /// Admin who reviewed the request.
        /// </summary>
        public string? ReviewedByAdminId { get; set; }

        /// <summary>
        /// Admin's note when approving or rejecting.
        /// </summary>
        public string? AdminNote { get; set; }

        /// <summary>
        /// Wallet balance snapshot at time of request.
        /// </summary>
        public decimal WalletBalanceAtRequest { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ReviewedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
    }

    public static class RefundRequestStatus
    {
        public const string Pending = "Pending";
        public const string Approved = "Approved";
        public const string Rejected = "Rejected";
        public const string Completed = "Completed";
    }
}
