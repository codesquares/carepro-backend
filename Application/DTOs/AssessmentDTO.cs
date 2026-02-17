using Domain.Entities;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.DTOs
{
    public class AssessmentDTO
    {
        public string Id { get; set; }
        public string UserId { get; set; }
        public string CaregiverId { get; set; }
        public string UserType { get; set; } // "Cleaner" or "Caregiver"
        public string? ServiceCategory { get; set; }
        public DateTime StartTimestamp { get; set; }
        public DateTime EndTimestamp { get; set; }
        public int Score { get; set; }
        public bool Passed { get; set; }
        public int PassingThreshold { get; set; }
        public List<AssessmentQuestion> Questions { get; set; }
        public string Status { get; set; }
        public DateTime AssessedDate { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class AssessmentQuestionDTO
    {
        public string QuestionId { get; set; }
        public string Question { get; set; }
        public List<string> Options { get; set; }
        public string CorrectAnswer { get; set; }
        public string UserAnswer { get; set; }
        public bool IsCorrect { get; set; }
    }

    public class AddAssessmentRequest
    {
        public string UserId { get; set; }
        public string CaregiverId { get; set; }
        public string UserType { get; set; } // "Cleaner" or "Caregiver"

        /// <summary>
        /// Null or empty for general assessments.
        /// For specialized: "MedicalSupport", "PostSurgeryCare", "SpecialNeedsCare", "Palliative", "TherapyAndWellness"
        /// </summary>
        public string? ServiceCategory { get; set; }

        /// <summary>
        /// The session ID returned by GET /questions. Required for specialized assessments.
        /// </summary>
        public string? SessionId { get; set; }

        public List<AssessmentQuestion> Questions { get; set; }
        public string Status { get; set; }
        public int Score { get; set; }
    }

    /// <summary>
    /// Response returned after submitting an assessment.
    /// </summary>
    public class AssessmentSubmitResponse
    {
        public string AttemptId { get; set; }
        public bool Passed { get; set; }
        public int Score { get; set; }
        public int Threshold { get; set; }
        public string? ServiceCategory { get; set; }

        /// <summary>
        /// If the caregiver failed and a cooldown applies, this is when they can retry.
        /// Null if no cooldown or if they passed.
        /// </summary>
        public DateTime? CooldownUntil { get; set; }
    }

    /// <summary>
    /// A single entry in the assessment history for a caregiver.
    /// </summary>
    public class AssessmentHistoryDTO
    {
        public string AttemptId { get; set; }
        public string? ServiceCategory { get; set; }
        public string? ServiceCategoryDisplayName { get; set; }
        public int Score { get; set; }
        public bool Passed { get; set; }
        public int Threshold { get; set; }
        public DateTime Date { get; set; }

        /// <summary>
        /// When the caregiver is next eligible to retake this category's assessment.
        /// Null if they passed or cooldown has expired.
        /// </summary>
        public DateTime? NextRetryDate { get; set; }
    }

    /// <summary>
    /// Eligibility status for a single service category.
    /// </summary>
    public class CategoryEligibilityDTO
    {
        public string ServiceCategory { get; set; }
        public string DisplayName { get; set; }
        public string Tier { get; set; } // "general" or "specialized"
        public bool IsEligible { get; set; }
        public bool AssessmentPassed { get; set; }
        public bool AssessmentExpired { get; set; }
        public bool CertificatesVerified { get; set; }
        public List<string> MissingCertificates { get; set; } = new();
        public bool AssessmentCompleted { get; set; }
        public int? AssessmentScore { get; set; }
        public DateTime? AssessmentCompletedAt { get; set; }
        public DateTime? AssessmentExpiresAt { get; set; }
        public DateTime? CooldownUntil { get; set; }
    }

    /// <summary>
    /// Full eligibility map returned by the eligibility endpoint.
    /// </summary>
    public class EligibilityResponse
    {
        public string CaregiverId { get; set; }
        public List<CategoryEligibilityDTO> Categories { get; set; } = new();
    }

    /// <summary>
    /// Error response when gig publishing is rejected due to missing eligibility.
    /// </summary>
    public class GigEligibilityError
    {
        public string Error { get; set; } = "ELIGIBILITY_REQUIRED";
        public List<string> Missing { get; set; } = new();
        public string Category { get; set; }
        public string Message { get; set; }
    }

    /// <summary>
    /// Service requirement information returned by the requirements endpoint.
    /// </summary>
    public class ServiceRequirementDTO
    {
        public string Id { get; set; }
        public string ServiceCategory { get; set; }
        public string DisplayName { get; set; }
        public string Tier { get; set; }
        public List<string> RequiredCertificates { get; set; } = new();
        public string RequiredAssessment { get; set; }
        public int PassingScore { get; set; }
        public int QuestionCount { get; set; }
        public int CooldownHours { get; set; }
        public int AssessmentValidityMonths { get; set; }
        public int SessionDurationMinutes { get; set; }
        public bool Active { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// Request body for creating a new service requirement.
    /// </summary>
    public class AddServiceRequirementRequest
    {
        public string ServiceCategory { get; set; }
        public string DisplayName { get; set; }
        public string Tier { get; set; } = "specialized";
        public List<string> RequiredCertificates { get; set; } = new();
        public string RequiredAssessment { get; set; } = "General";
        public int PassingScore { get; set; } = 70;
        public int QuestionCount { get; set; } = 20;
        public int CooldownHours { get; set; } = 48;
        public int AssessmentValidityMonths { get; set; } = 0;
        public int SessionDurationMinutes { get; set; } = 60;
        public bool Active { get; set; } = true;
    }

    /// <summary>
    /// Request body for updating an existing service requirement.
    /// </summary>
    public class UpdateServiceRequirementRequest
    {
        public string? DisplayName { get; set; }
        public string? Tier { get; set; }
        public List<string>? RequiredCertificates { get; set; }
        public string? RequiredAssessment { get; set; }
        public int? PassingScore { get; set; }
        public int? QuestionCount { get; set; }
        public int? CooldownHours { get; set; }
        public int? AssessmentValidityMonths { get; set; }
        public int? SessionDurationMinutes { get; set; }
        public bool? Active { get; set; }
    }

    public class AssessmentQuestionSubmitDTO
    {

        public string QuestionId { get; set; }
        public string UserAnswer { get; set; }
    }

    public class GetAssessmentQuestionsRequest
    {
        public string UserType { get; set; } // "Cleaner" or "Caregiver"
    }

    /// <summary>
    /// Response for GET /questions?serviceCategory=X — includes a session ID to bind answers.
    /// </summary>
    public class SpecializedQuestionsResponse
    {
        public string SessionId { get; set; }
        public string ServiceCategory { get; set; }
        public int SessionDurationMinutes { get; set; }
        public DateTime ExpiresAt { get; set; }
        public List<AssessmentQuestionBankDTO> Questions { get; set; } = new();
    }

    /// <summary>
    /// Paginated response wrapper.
    /// </summary>
    public class PaginatedResponse<T>
    {
        public List<T> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public bool HasMore { get; set; }
    }
}
