using MongoDB.Bson;
using System;

namespace Domain.Entities
{
    public class VisitCheckin
    {
        public ObjectId Id { get; set; } = ObjectId.GenerateNewId();
        public string TaskSheetId { get; set; } = string.Empty;
        public string OrderId { get; set; } = string.Empty;

        /// <summary>Set instead of OrderId for a package-assignment check-in (Phase 9.5),
        /// mirroring TaskSheet.AssignmentId/PackageRequestId.</summary>
        public string? AssignmentId { get; set; }
        public string? PackageRequestId { get; set; }

        public string CaregiverId { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double Accuracy { get; set; }
        public double? DistanceFromServiceAddress { get; set; }
        public DateTime CheckinTimestamp { get; set; }

        /// <summary>
        /// Server-authoritative arrival time (Phase 9.4) — DateTime.UtcNow captured the
        /// moment the check-in request reaches the server, before any validation runs.
        /// This, not the device-supplied CheckinTimestamp, is the authoritative source
        /// for hour/payroll calculations (see TaskSheetService.CheckoutAsync). Nullable
        /// because real VisitCheckin documents created before Phase 9.4 predate this
        /// field — the MongoDB EF Core provider rejects missing non-nullable properties
        /// on read.
        /// </summary>
        public DateTime? ServerReceivedAt { get; set; }

        /// <summary>
        /// True when CheckinTimestamp and ServerReceivedAt differ by more than the
        /// configured threshold (VisitCheckin:TimestampDiscrepancyThresholdSeconds,
        /// default 300s) — flagged for admin review rather than silently accepted.
        /// </summary>
        public bool HasTimestampDiscrepancy { get; set; }

        /// <summary>
        /// Absolute difference between CheckinTimestamp and ServerReceivedAt, in seconds.
        /// Null when ServerReceivedAt wasn't captured (legacy record).
        /// </summary>
        public double? TimestampDiscrepancySeconds { get; set; }

        public bool IsLateCheckin { get; set; }
        public double MinutesLate { get; set; }

        /// <summary>
        /// True when the check-in distance from a client-verified GPS service location
        /// exceeds VisitCheckin:PackageFlagDistanceMeters (package-assignment visits only).
        /// Flagged for admin review, not blocked — device GPS drift is a known real-world
        /// problem, so this never prevents a genuine visit from being recorded. Always false
        /// when the comparison point is a geocoded address rather than real client GPS
        /// (that comparison isn't precise enough to be worth flagging on). Nullable for the
        /// same reason as ServerReceivedAt above — real VisitCheckin documents created before
        /// this field existed predate it, and the MongoDB EF Core provider rejects missing
        /// non-nullable properties on read; treat a missing value as false (not flagged).
        /// </summary>
        public bool? IsFlaggedForDistanceReview { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
