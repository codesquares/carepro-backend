using System;
using System.Collections.Generic;

namespace Application.DTOs
{
    // ── Raise a dispute (client action) ──
    public class RaiseDisputeRequest
    {
        /// <summary>
        /// The order this dispute is about.
        /// </summary>
        public string OrderId { get; set; } = string.Empty;

        /// <summary>
        /// For visit disputes: the task sheet ID. Null/empty for order-level disputes.
        /// </summary>
        public string? TaskSheetId { get; set; }

        /// <summary>
        /// "Visit" or "Order"
        /// </summary>
        public string DisputeType { get; set; } = string.Empty;

        /// <summary>
        /// Category from DisputeCategory constants.
        /// </summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>
        /// Client-typed reason explaining the dispute.
        /// </summary>
        public string Reason { get; set; } = string.Empty;
    }

    // ── Admin resolves a dispute ──
    public class ResolveDisputeRequest
    {
        /// <summary>
        /// Action taken: FullRefund, PartialRefund, FundsReleased, OrderCancelled, NoAction, Escalated
        /// </summary>
        public string ResolutionAction { get; set; } = string.Empty;

        /// <summary>
        /// Steps taken, evidence reviewed, details used to reach the decision.
        /// </summary>
        public string AdminNotes { get; set; } = string.Empty;

        /// <summary>
        /// Brief summary of the resolution outcome.
        /// </summary>
        public string ResolutionSummary { get; set; } = string.Empty;
    }

    // ── Response DTO ──
    public class DisputeResponse
    {
        public string Id { get; set; } = string.Empty;
        public string OrderId { get; set; } = string.Empty;
        public string? TaskSheetId { get; set; }
        public string DisputeType { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;

        public string RaisedBy { get; set; } = string.Empty;
        public string? RaisedByName { get; set; }
        public string ClientId { get; set; } = string.Empty;
        public string? ClientName { get; set; }
        public string CaregiverId { get; set; } = string.Empty;
        public string? CaregiverName { get; set; }

        public string Status { get; set; } = string.Empty;

        // Resolution
        public string? ResolutionAction { get; set; }
        public string? AdminNotes { get; set; }
        public string? ResolutionSummary { get; set; }
        public string? ResolvedBy { get; set; }
        public string? ResolvedByName { get; set; }
        public DateTime? ResolvedAt { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    // ── Client reviews a visit (task sheet) ──
    public class ReviewVisitRequest
    {
        /// <summary>
        /// "Approved" or "Disputed"
        /// </summary>
        public string ReviewStatus { get; set; } = string.Empty;

        /// <summary>
        /// Required if ReviewStatus is "Disputed". The reason for the dispute.
        /// </summary>
        public string? DisputeReason { get; set; }

        /// <summary>
        /// Required if ReviewStatus is "Disputed". Category from DisputeCategory visit categories.
        /// </summary>
        public string? DisputeCategory { get; set; }
    }
}
