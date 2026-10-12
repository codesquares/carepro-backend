using MongoDB.Bson;
using System;
using System.Collections.Generic;

namespace Domain.Entities
{
    public class ObservationReport
    {
        public ObjectId Id { get; set; } = ObjectId.GenerateNewId();
        public string OrderId { get; set; } = string.Empty;
        /// <summary>Set instead of OrderId for a package-assignment report (same alternate-reference pattern as
        /// TaskSheet.AssignmentId). Nullable so pre-existing documents still load; legacy reports leave it null.</summary>
        public string? AssignmentId { get; set; }
        /// <summary>Set alongside AssignmentId — denormalised from Assignment.PackageRequestId; this is what the owning
        /// client is resolved from (it survives a caregiver reassignment).</summary>
        public string? PackageRequestId { get; set; }
        public string TaskSheetId { get; set; } = string.Empty;
        public string CaregiverId { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;
        public List<string> PhotoUrls { get; set; } = new List<string>();
        public DateTime ReportedAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
