using MongoDB.Bson;
using System;
using System.Collections.Generic;

namespace Domain.Entities
{
    /// <summary>
    /// A caregiver's police clearance submission (vetting step). One document per submission:
    /// resubmitting creates a NEW record and marks the older one <see cref="Superseded"/>, so history
    /// is never deleted by the caregiver. Only the latest non-superseded record counts for readiness.
    ///
    /// Every property except <see cref="Id"/> is nullable on purpose: no schema is enforced in MongoDB and the
    /// EF provider rejects a stored document that is missing a non-nullable property, so code reading
    /// these fields must tolerate absence (see the "tolerate missing" notes on VisitCheckin/Guarantor).
    ///
    /// <see cref="Status"/> and <see cref="ReviewedAt"/> are configured as optimistic-concurrency tokens
    /// (CareProDbContext), so two staff racing to review the same record cannot both succeed.
    /// </summary>
    public class PoliceClearance
    {
        public ObjectId Id { get; set; }

        public string? CaregiverId { get; set; }

        /// <summary><see cref="PoliceClearanceStatuses"/>.</summary>
        public string? Status { get; set; }

        /// <summary>1 to 5 stored files (a single PDF, or up to 5 images). Private storage references only — never a URL.</summary>
        public List<PoliceClearanceFile>? Files { get; set; }

        /// <summary>The name as printed on the document, typed by the caregiver; staff compare it to the profile name.</summary>
        public string? NameOnDocument { get; set; }

        public string? ReferenceNumber { get; set; }

        /// <summary>Date the clearance was issued (UTC date). Validated against PoliceClearance:MaxAgeMonths at submission.</summary>
        public DateTime? IssueDate { get; set; }

        public DateTime? SubmittedAt { get; set; }

        public string? ReviewedByAdminId { get; set; }

        public string? ReviewedByAdminEmail { get; set; }

        public DateTime? ReviewedAt { get; set; }

        public string? ReviewNotes { get; set; }

        public string? RejectionReason { get; set; }

        /// <summary>What the reviewer confirmed at approval. Persisted as evidence of what was actually checked.</summary>
        public PoliceClearanceChecklist? Checklist { get; set; }

        /// <summary>True once a newer submission replaced this one. Null/false = current.</summary>
        public bool? Superseded { get; set; }

        public DateTime? RevokedAt { get; set; }

        public string? RevokedReason { get; set; }

        public string? RevokedByAdminId { get; set; }

        public string? RevokedByAdminEmail { get; set; }
    }

    /// <summary>Reference to one privately stored file. Deliberately holds no URL.</summary>
    public class PoliceClearanceFile
    {
        /// <summary>Storage (Cloudinary) public id of the authenticated asset.</summary>
        public string? PublicId { get; set; }

        /// <summary>Cloudinary resource type the asset was uploaded as (needed to delete/download it correctly).</summary>
        public string? ResourceType { get; set; }

        public string? OriginalFileName { get; set; }

        public string? ContentType { get; set; }

        public long? SizeBytes { get; set; }
    }

    public class PoliceClearanceChecklist
    {
        public bool? NameMatchesProfile { get; set; }
        public bool? IssuedByNigeriaPoliceForce { get; set; }
        public bool? IssueDateWithinAllowedAge { get; set; }
        public bool? ReferenceNumberCaptured { get; set; }
        public bool? ScanLegibleAndUnaltered { get; set; }

        public bool AllTrue() =>
            NameMatchesProfile == true && IssuedByNigeriaPoliceForce == true && IssueDateWithinAllowedAge == true
            && ReferenceNumberCaptured == true && ScanLegibleAndUnaltered == true;
    }

    public static class PoliceClearanceStatuses
    {
        public const string Pending = "pending";
        public const string Approved = "approved";
        public const string Rejected = "rejected";
        public const string Revoked = "revoked";
    }
}
