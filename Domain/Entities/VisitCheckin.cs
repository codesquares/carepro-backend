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
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
