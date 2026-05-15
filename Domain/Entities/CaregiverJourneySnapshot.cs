using MongoDB.Bson;

namespace Domain.Entities
{
    /// <summary>
    /// Pre-computed read model rebuilt every 15 minutes by CaregiverSnapshotProcessor.
    /// One document per active caregiver. Never written to directly by application code —
    /// only the background processor is the writer.
    /// </summary>
    public class CaregiverJourneySnapshot
    {
        public ObjectId Id { get; set; } = ObjectId.GenerateNewId();

        // ── Identity (denormalised for fast filter/export without joining Caregivers) ──
        public string CaregiverId { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? PhoneNo { get; set; }
        public string? ServiceCity { get; set; }
        public string? ServiceState { get; set; }
        public string? AuthProvider { get; set; }
        public DateTime CaregiverCreatedAt { get; set; }

        // ── KYC / Identity verification ──
        public bool IsIdentityVerified { get; set; }
        public string? IdentityVerificationStatus { get; set; } // "success" | "pending" | "failed" | null
        public DateTime? IdentityVerifiedAt { get; set; }

        // ── Assessment ──
        public bool HasPassedAnyAssessment { get; set; }
        public List<string> PassedAssessmentCategories { get; set; } = new();
        public int? LatestAssessmentScore { get; set; }

        // ── Certifications (uploaded credential documents) ──
        public int CertificatesUploadedCount { get; set; }
        public int CertificatesVerifiedCount { get; set; }

        // ── Profile completeness ──
        public bool HasProfilePicture { get; set; }
        public bool HasAboutMe { get; set; }

        // ── Professional data (LinkedIn-style) ──
        public bool HasWorkExperience { get; set; }
        public bool HasQualifications { get; set; }
        public bool HasEducation { get; set; }

        // ── Gigs ──
        public int GigsDraftCount { get; set; }
        public int GigsPublishedCount { get; set; }
        public int GigsDeletedCount { get; set; }

        /// <summary>
        /// Computed highest stage reached. One of:
        /// Registered → ProfileStarted → ProfessionalDataAdded → Verified →
        /// AssessmentPassed → ReadyToPublish → Published
        /// </summary>
        public string JourneyStage { get; set; } = "Registered";

        public DateTime LastRebuildAt { get; set; } = DateTime.UtcNow;
    }
}
