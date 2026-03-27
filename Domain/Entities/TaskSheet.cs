using MongoDB.Bson;
using System;
using System.Collections.Generic;

namespace Domain.Entities
{
    public class TaskSheet
    {
        public ObjectId Id { get; set; } = ObjectId.GenerateNewId();
        public string OrderId { get; set; } = string.Empty;
        public string CaregiverId { get; set; } = string.Empty;
        public int SheetNumber { get; set; }
        public int BillingCycleNumber { get; set; } = 1;
        public List<TaskSheetItem> Tasks { get; set; } = new List<TaskSheetItem>();
        public string Status { get; set; } = "in-progress";

        /// <summary>
        /// The calendar date this task sheet is for (Nigerian time, date-only).
        /// Ensures only one task sheet per scheduled day. Null for legacy sheets.
        /// </summary>
        public DateTime? ScheduledDate { get; set; }

        public DateTime? SubmittedAt { get; set; }
        public string? ClientSignatureUrl { get; set; }
        public DateTime? ClientSignatureSignedAt { get; set; }

        // ── Client visit review fields ──
        /// <summary>
        /// Client review status: "Pending", "Approved", or "Disputed". Nullable for pre-existing documents.
        /// </summary>
        public string? ClientReviewStatus { get; set; }
        public DateTime? ClientReviewedAt { get; set; }
        public string? ClientDisputeReason { get; set; }

        /// <summary>
        /// Duration of the visit in minutes, calculated from check-in to submission.
        /// </summary>
        public double? VisitDurationMinutes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class TaskSheetItem
    {
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public string Text { get; set; } = string.Empty;
        public bool Completed { get; set; } = false;
        public bool AddedByCaregiver { get; set; } = false;

        /// <summary>
        /// True if the task was proposed by the client. Nullable for backward compatibility with existing documents.
        /// </summary>
        public bool? AddedByClient { get; set; }

        /// <summary>
        /// Acceptance status for client-proposed tasks.
        /// Original and caregiver-added tasks default to "Accepted".
        /// Client-proposed tasks start as "Pending" and must be accepted by the caregiver.
        /// Values: "Accepted", "Pending", "Rejected". Nullable for existing documents.
        /// </summary>
        public string? ProposalStatus { get; set; }
    }
}
