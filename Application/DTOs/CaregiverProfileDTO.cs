using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Application.DTOs
{
    // ───────────────────── EDUCATION ─────────────────────

    public class CaregiverEducationResponse
    {
        public string Id { get; set; } = string.Empty;
        public string CaregiverId { get; set; } = string.Empty;
        public string SchoolName { get; set; } = string.Empty;
        public string DegreeType { get; set; } = string.Empty;
        public string FieldOfStudy { get; set; } = string.Empty;
        public int StartMonth { get; set; }
        public int StartYear { get; set; }
        public int? EndMonth { get; set; }
        public int? EndYear { get; set; }
        public bool CurrentlyStudying { get; set; }
        public string? Grade { get; set; }
        public string? Activities { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class AddCaregiverEducationRequest
    {
        [Required, StringLength(200, MinimumLength = 1)]
        public string SchoolName { get; set; } = string.Empty;

        [Required, StringLength(50, MinimumLength = 1)]
        public string DegreeType { get; set; } = string.Empty;

        [Required, StringLength(200, MinimumLength = 1)]
        public string FieldOfStudy { get; set; } = string.Empty;

        [Range(1, 12)]
        public int StartMonth { get; set; }

        [Range(1900, 2100)]
        public int StartYear { get; set; }

        [Range(1, 12)]
        public int? EndMonth { get; set; }

        [Range(1900, 2100)]
        public int? EndYear { get; set; }

        public bool CurrentlyStudying { get; set; }

        [StringLength(50)]
        public string? Grade { get; set; }

        [StringLength(1000)]
        public string? Activities { get; set; }
    }

    public class UpdateCaregiverEducationRequest : AddCaregiverEducationRequest { }

    // ─────────────── CERTIFICATIONS / QUALIFICATIONS ───────────────

    public class CaregiverQualificationResponse
    {
        public string Id { get; set; } = string.Empty;
        public string CaregiverId { get; set; } = string.Empty;
        public string CertificationName { get; set; } = string.Empty;
        public string IssuingOrganisation { get; set; } = string.Empty;
        public int IssueMonth { get; set; }
        public int IssueYear { get; set; }
        public int? ExpiryMonth { get; set; }
        public int? ExpiryYear { get; set; }
        public bool DoesNotExpire { get; set; }
        public string? CredentialId { get; set; }
        public string? CredentialUrl { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class AddCaregiverQualificationRequest
    {
        [Required, StringLength(200, MinimumLength = 1)]
        public string CertificationName { get; set; } = string.Empty;

        [Required, StringLength(200, MinimumLength = 1)]
        public string IssuingOrganisation { get; set; } = string.Empty;

        [Range(1, 12)]
        public int IssueMonth { get; set; }

        [Range(1900, 2100)]
        public int IssueYear { get; set; }

        [Range(1, 12)]
        public int? ExpiryMonth { get; set; }

        [Range(1900, 2100)]
        public int? ExpiryYear { get; set; }

        public bool DoesNotExpire { get; set; }

        [StringLength(200)]
        public string? CredentialId { get; set; }

        [StringLength(2000)]
        [Url]
        public string? CredentialUrl { get; set; }
    }

    public class UpdateCaregiverQualificationRequest : AddCaregiverQualificationRequest { }

    // ───────────────────── WORK EXPERIENCE ─────────────────────

    public class CaregiverWorkExperienceResponse
    {
        public string Id { get; set; } = string.Empty;
        public string CaregiverId { get; set; } = string.Empty;
        public string JobTitle { get; set; } = string.Empty;
        public string EmploymentType { get; set; } = string.Empty;
        public string OrganisationName { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public int StartMonth { get; set; }
        public int StartYear { get; set; }
        public int? EndMonth { get; set; }
        public int? EndYear { get; set; }
        public bool CurrentlyWorkingHere { get; set; }
        public string? Industry { get; set; }
        public string? Description { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class AddCaregiverWorkExperienceRequest
    {
        [Required, StringLength(200, MinimumLength = 1)]
        public string JobTitle { get; set; } = string.Empty;

        [Required, StringLength(50, MinimumLength = 1)]
        public string EmploymentType { get; set; } = string.Empty;

        [Required, StringLength(200, MinimumLength = 1)]
        public string OrganisationName { get; set; } = string.Empty;

        [Required, StringLength(200, MinimumLength = 1)]
        public string Location { get; set; } = string.Empty;

        [Range(1, 12)]
        public int StartMonth { get; set; }

        [Range(1900, 2100)]
        public int StartYear { get; set; }

        [Range(1, 12)]
        public int? EndMonth { get; set; }

        [Range(1900, 2100)]
        public int? EndYear { get; set; }

        public bool CurrentlyWorkingHere { get; set; }

        [StringLength(100)]
        public string? Industry { get; set; }

        [StringLength(4000)]
        public string? Description { get; set; }
    }

    public class UpdateCaregiverWorkExperienceRequest : AddCaregiverWorkExperienceRequest { }
}
