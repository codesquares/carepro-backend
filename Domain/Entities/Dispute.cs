using MongoDB.Bson;
using System;
using System.Collections.Generic;

namespace Domain.Entities
{
    public class Dispute
    {
        public ObjectId Id { get; set; } = ObjectId.GenerateNewId();

        /// <summary>
        /// The order this dispute is associated with (always present).
        /// </summary>
        public string OrderId { get; set; } = string.Empty;

        /// <summary>
        /// For visit disputes: the specific task sheet being disputed. Null for order-level disputes.
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

        /// <summary>
        /// UserId of the person who raised the dispute (typically the client).
        /// </summary>
        public string RaisedBy { get; set; } = string.Empty;

        /// <summary>
        /// The client on the order.
        /// </summary>
        public string ClientId { get; set; } = string.Empty;

        /// <summary>
        /// The caregiver on the order.
        /// </summary>
        public string CaregiverId { get; set; } = string.Empty;

        /// <summary>
        /// Current status: Open → UnderReview → Resolved | Dismissed
        /// </summary>
        public string Status { get; set; } = DisputeStatus.Open;

        // ── Resolution fields (populated by admin) ──

        /// <summary>
        /// Admin's description of what action was taken.
        /// </summary>
        public string? ResolutionAction { get; set; }

        /// <summary>
        /// Detailed notes from admin: steps taken, details reviewed, evidence considered.
        /// </summary>
        public string? AdminNotes { get; set; }

        /// <summary>
        /// Summary of the resolution outcome.
        /// </summary>
        public string? ResolutionSummary { get; set; }

        /// <summary>
        /// UserId of the admin who resolved the dispute.
        /// </summary>
        public string? ResolvedBy { get; set; }

        public DateTime? ResolvedAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    // ── Dispute Type Constants ──
    public static class DisputeType
    {
        public const string Visit = "Visit";
        public const string Order = "Order";
    }

    // ── Dispute Status Constants ──
    public static class DisputeStatus
    {
        public const string Open = "Open";
        public const string UnderReview = "UnderReview";
        public const string Resolved = "Resolved";
        public const string Dismissed = "Dismissed";
    }

    // ── Dispute Category Constants ──
    public static class DisputeCategory
    {
        // Visit-level categories
        public const string CaregiverNoShow = "CaregiverNoShow";
        public const string TasksNotCompleted = "TasksNotCompleted";
        public const string QualityOfCare = "QualityOfCare";
        public const string Punctuality = "Punctuality";
        public const string UnauthorizedAction = "UnauthorizedAction";

        // Order-level (financial) categories
        public const string DoubleCharged = "DoubleCharged";
        public const string WrongBillingType = "WrongBillingType";
        public const string SuspendedWithoutReason = "SuspendedWithoutReason";
        public const string IncorrectAmount = "IncorrectAmount";
        public const string UnauthorizedCharge = "UnauthorizedCharge";
        public const string Other = "Other";

        public static readonly HashSet<string> VisitCategories = new()
        {
            CaregiverNoShow, TasksNotCompleted, QualityOfCare, Punctuality, UnauthorizedAction
        };

        public static readonly HashSet<string> OrderCategories = new()
        {
            DoubleCharged, WrongBillingType, SuspendedWithoutReason, IncorrectAmount, UnauthorizedCharge, Other
        };
    }

    // ── Resolution Action Constants ──
    public static class DisputeResolutionAction
    {
        public const string FullRefund = "FullRefund";
        public const string PartialRefund = "PartialRefund";
        public const string FundsReleased = "FundsReleased";
        public const string OrderCancelled = "OrderCancelled";
        public const string NoAction = "NoAction";
        public const string Escalated = "Escalated";
    }
}
