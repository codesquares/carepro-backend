using MongoDB.Bson;
using System;

namespace Domain.Entities
{
    /// <summary>
    /// One caregiver assigned to one <see cref="PackageRequest"/> by staff/system,
    /// pending that caregiver's acceptance (Phase 4). This is the reshaped, one-sided
    /// version of the competitive <see cref="CareRequestResponse"/> mechanism:
    /// the record is created directly by staff (not by a caregiver opting in), starts
    /// in <see cref="AssignmentStatuses.PendingAcceptance"/>, and the caregiver's
    /// action moves it to Accepted or Declined.
    ///
    /// There is deliberately NO automatic reassignment or timeout — a non-response
    /// sits in PendingAcceptance until a staff member intervenes. The admin
    /// "Pending Acceptance" view exists so stalled assignments are visible.
    /// </summary>
    public class Assignment
    {
        public ObjectId Id { get; set; }

        public string PackageRequestId { get; set; } = string.Empty;
        public string CaregiverId { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;

        /// <summary>PendingAcceptance | Accepted | Declined | Cancelled</summary>
        public string Status { get; set; } = AssignmentStatuses.PendingAcceptance;

        // ── Who assigned (accountability) ──
        public string AssignedByAdminId { get; set; } = string.Empty;
        public string? AssignedByAdminEmail { get; set; }
        /// <summary>"staff" for a human assignment, "system" for an automated one.</summary>
        public string AssignedBy { get; set; } = "staff";
        public DateTime AssignedAt { get; set; }

        /// <summary>Match score from the engine at assignment time, if the engine was consulted.</summary>
        public double? MatchScore { get; set; }

        // ── Caregiver response ──
        public DateTime? RespondedAt { get; set; }
        public string? DeclineReason { get; set; }

        // ── Staff cancellation / reassignment ──
        public string? CancelledByAdminId { get; set; }
        public string? CancelReason { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public static class AssignmentStatuses
    {
        public const string PendingAcceptance = "PendingAcceptance";
        public const string Accepted = "Accepted";
        public const string Declined = "Declined";
        public const string Cancelled = "Cancelled";
    }
}
