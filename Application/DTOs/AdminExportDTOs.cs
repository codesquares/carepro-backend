namespace Application.DTOs
{
    // ── Export queries ──

    public class ExportQuery
    {
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
    }

    // ── Snapshot query + response ──

    public class CaregiverSnapshotQuery
    {
        /// <summary>
        /// Filter by journey stage: Registered, ProfileStarted, ProfessionalDataAdded,
        /// Verified, AssessmentPassed, ReadyToPublish, Published
        /// </summary>
        public string? JourneyStage { get; set; }
        public bool? IsIdentityVerified { get; set; }
        public bool? HasProfilePicture { get; set; }
        public bool? HasPassedAssessment { get; set; }
        public bool? HasPublishedGig { get; set; }
        public bool? HasCertificate { get; set; }
        public DateTime? RegisteredFrom { get; set; }
        public DateTime? RegisteredTo { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 50;
    }

    public class CaregiverJourneySnapshotDTO
    {
        public string CaregiverId { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? PhoneNo { get; set; }
        public string? ServiceCity { get; set; }
        public string? ServiceState { get; set; }
        public string? AuthProvider { get; set; }
        public DateTime CaregiverCreatedAt { get; set; }

        public bool IsIdentityVerified { get; set; }
        public string? IdentityVerificationStatus { get; set; }
        public DateTime? IdentityVerifiedAt { get; set; }

        public bool HasPassedAnyAssessment { get; set; }
        public List<string> PassedAssessmentCategories { get; set; } = new();
        public int? LatestAssessmentScore { get; set; }

        public int CertificatesUploadedCount { get; set; }
        public int CertificatesVerifiedCount { get; set; }

        public bool HasProfilePicture { get; set; }
        public bool HasAboutMe { get; set; }
        public bool HasWorkExperience { get; set; }
        public bool HasQualifications { get; set; }
        public bool HasEducation { get; set; }

        public int GigsDraftCount { get; set; }
        public int GigsPublishedCount { get; set; }
        public int GigsDeletedCount { get; set; }

        public string JourneyStage { get; set; } = string.Empty;
        public DateTime LastRebuildAt { get; set; }
    }

    public class CaregiverSnapshotResponse
    {
        public List<CaregiverJourneySnapshotDTO> Snapshots { get; set; } = new();
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
        public Dictionary<string, int> ByJourneyStage { get; set; } = new();
    }
}
