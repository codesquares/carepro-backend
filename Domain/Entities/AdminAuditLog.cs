using MongoDB.Bson;
using System;

namespace Domain.Entities
{
    /// <summary>
    /// Append-only audit trail for sensitive admin actions performed against
    /// caregiver / verification records. Never written to by any non-admin flow.
    /// </summary>
    public class AdminAuditLog
    {
        public ObjectId Id { get; set; }

        // Who performed the action
        public string AdminId { get; set; } = string.Empty;
        public string? AdminEmail { get; set; }

        // What entity was affected
        // e.g. "Verification", "Caregiver"
        public string TargetEntityType { get; set; } = string.Empty;
        // e.g. verificationId or caregiverId
        public string TargetEntityId { get; set; } = string.Empty;
        // The owning user (caregiver) for cross-entity audits
        public string? TargetUserId { get; set; }

        // Action label
        // e.g. "VerificationStatusOverride", "CaregiverNameEdit"
        public string Action { get; set; } = string.Empty;

        // Snapshot of fields BEFORE the change (JSON string)
        public string? BeforeJson { get; set; }
        // Snapshot of fields AFTER the change (JSON string)
        public string? AfterJson { get; set; }

        // Free-text reason supplied by the admin (required for sensitive actions)
        public string Reason { get; set; } = string.Empty;

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
