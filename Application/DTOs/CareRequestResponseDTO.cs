using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Application.DTOs
{
    // ── Caregiver Browse Page DTOs ──

    /// <summary>
    /// A care request card as seen by a caregiver on the browse page.
    /// Client identity is anonymized (first name only, no clientId).
    /// </summary>
    public class CaregiverMatchedRequestDTO
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string ServiceGroup { get; set; } = string.Empty;
        public string ServiceCategory { get; set; } = string.Empty;
        public string? Location { get; set; }
        public string? Budget { get; set; }
        public decimal? BudgetMin { get; set; }
        public decimal? BudgetMax { get; set; }
        public string? BudgetType { get; set; }
        public string Urgency { get; set; } = string.Empty;
        public DateTime PostedAt { get; set; }
        public int RespondersCount { get; set; }
        public bool HasResponded { get; set; }
        public string? ClientFirstName { get; set; }
        public string? Notes { get; set; }
    }

    /// <summary>
    /// Full care request detail as seen by a caregiver (anonymized client).
    /// </summary>
    public class CaregiverRequestDetailDTO
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string ServiceGroup { get; set; } = string.Empty;
        public string ServiceCategory { get; set; } = string.Empty;
        public string? ServicePackageType { get; set; }
        public string? ServiceMode { get; set; }
        public List<string> Tasks { get; set; } = new();
        public string? Notes { get; set; }
        public string? ExperiencePreference { get; set; }
        public string? CertificationPreference { get; set; }
        public string? LanguagePreference { get; set; }
        public string Urgency { get; set; } = string.Empty;
        public List<string> Schedule { get; set; } = new();
        public string Frequency { get; set; } = string.Empty;
        public string? Duration { get; set; }
        public string? Location { get; set; }
        public string? Budget { get; set; }
        public decimal? BudgetMin { get; set; }
        public decimal? BudgetMax { get; set; }
        public string? BudgetType { get; set; }
        public DateTime PostedAt { get; set; }
        public int RespondersCount { get; set; }
        public bool HasResponded { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    // ── Caregiver Respond DTOs ──

    public class RespondToCareRequestDTO
    {
        [MaxLength(500)]
        public string? Message { get; set; }

        public decimal? ProposedRate { get; set; }
    }

    public class CareRequestRespondResult
    {
        public bool Success { get; set; }
        public string ResponseId { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    // ── Client Detail View DTOs ──

    /// <summary>
    /// Full client-side request detail with responders grouped by status.
    /// </summary>
    public class CareRequestDetailDTO
    {
        public CareRequestDTO Request { get; set; } = new();
        public CareRequestRespondersDTO Responders { get; set; } = new();
        public CareRequestCountsDTO Counts { get; set; } = new();
    }

    public class CareRequestRespondersDTO
    {
        public List<CaregiverResponseCardDTO> All { get; set; } = new();
        public List<CaregiverResponseCardDTO> Shortlisted { get; set; } = new();
        public List<CaregiverResponseCardDTO> Hired { get; set; } = new();
    }

    public class CareRequestCountsDTO
    {
        public int Responders { get; set; }
        public int Shortlisted { get; set; }
        public int Hired { get; set; }
    }

    /// <summary>
    /// A caregiver response card as seen by the client on their request detail page.
    /// </summary>
    public class CaregiverResponseCardDTO
    {
        public string ResponseId { get; set; } = string.Empty;
        public string CaregiverId { get; set; } = string.Empty;
        public string CaregiverName { get; set; } = string.Empty;
        public string? ProfileImage { get; set; }
        public string? Location { get; set; }
        public double AverageRating { get; set; }
        public int ReviewCount { get; set; }
        public double? MatchScore { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime RespondedAt { get; set; }
        public string? Message { get; set; }
        public decimal? ProposedRate { get; set; }
        public bool IsVerified { get; set; }
        public string? AboutMe { get; set; }
        public string? SpecialGigId { get; set; }
    }

    // ── Shortlist / Hire Result DTOs ──

    public class ShortlistResult
    {
        public bool Success { get; set; }
        public string ResponseId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }

    public class HireResult
    {
        public bool Success { get; set; }
        public string ResponseId { get; set; } = string.Empty;
        public string SpecialGigId { get; set; } = string.Empty;
        public string CaregiverId { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    // ── Paginated browse results ──

    public class CaregiverMatchedRequestsResponse
    {
        public List<CaregiverMatchedRequestDTO> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }
}
