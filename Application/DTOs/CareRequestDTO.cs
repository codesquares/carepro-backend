using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Application.DTOs
{
    /// <summary>
    /// DTO for creating a new care request
    /// </summary>
    public class CreateCareRequestDTO
    {
        [Required]
        public string ClientId { get; set; } = string.Empty;

        [Required]
        public string ServiceCategory { get; set; } = string.Empty;

        [Required]
        [MaxLength(120)]
        public string Title { get; set; } = string.Empty;

        [Required]
        public string ServiceGroup { get; set; } = string.Empty;

        [MaxLength(2000)]
        public string? Notes { get; set; }

        [Required]
        public string Urgency { get; set; } = string.Empty;

        [Required]
        public List<string> Schedule { get; set; } = new List<string>();

        [Required]
        public string Frequency { get; set; } = string.Empty;

        public string? Duration { get; set; }

        public string? Location { get; set; }

        /// <summary>Legacy free-text budget field (still supported)</summary>
        public string? Budget { get; set; }

        /// <summary>Structured budget minimum</summary>
        public decimal? BudgetMin { get; set; }

        /// <summary>Structured budget maximum</summary>
        public decimal? BudgetMax { get; set; }

        /// <summary>Budget type: "Fixed" or "Negotiable"</summary>
        public string? BudgetType { get; set; }

        [MaxLength(1000)]
        public string? SpecialRequirements { get; set; }

        public List<string>? Tasks { get; set; }

        public string? ExperiencePreference { get; set; }

        public string? CertificationPreference { get; set; }

        public string? LanguagePreference { get; set; }

        public string? ServicePackageType { get; set; }

        public string? ServiceMode { get; set; }
    }

    /// <summary>
    /// DTO for updating a care request
    /// </summary>
    public class UpdateCareRequestDTO
    {
        public string? ServiceCategory { get; set; }

        [MaxLength(120)]
        public string? Title { get; set; }

        public string? ServiceGroup { get; set; }

        [MaxLength(2000)]
        public string? Notes { get; set; }

        public string? Urgency { get; set; }

        public List<string>? Schedule { get; set; }

        public string? Frequency { get; set; }

        public string? Duration { get; set; }

        public string? Location { get; set; }

        public string? Budget { get; set; }

        public decimal? BudgetMin { get; set; }

        public decimal? BudgetMax { get; set; }

        public string? BudgetType { get; set; }

        [MaxLength(1000)]
        public string? SpecialRequirements { get; set; }

        public List<string>? Tasks { get; set; }

        public string? ExperiencePreference { get; set; }

        public string? CertificationPreference { get; set; }

        public string? LanguagePreference { get; set; }

        public string? ServicePackageType { get; set; }

        public string? ServiceMode { get; set; }
    }

    /// <summary>
    /// DTO for returning care request data
    /// </summary>
    public class CareRequestDTO
    {
        public string Id { get; set; } = string.Empty;

        public string ClientId { get; set; } = string.Empty;

        public string ServiceCategory { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public string ServiceGroup { get; set; } = string.Empty;

        public string? Notes { get; set; }

        public string Urgency { get; set; } = string.Empty;

        public List<string> Schedule { get; set; } = new List<string>();

        public string Frequency { get; set; } = string.Empty;

        public string? Duration { get; set; }

        public string? Location { get; set; }

        public string? Budget { get; set; }

        public decimal? BudgetMin { get; set; }

        public decimal? BudgetMax { get; set; }

        public string? BudgetType { get; set; }

        public string? SpecialRequirements { get; set; }

        public List<string> Tasks { get; set; } = new List<string>();

        public string? ExperiencePreference { get; set; }

        public string? CertificationPreference { get; set; }

        public string? LanguagePreference { get; set; }

        public string? ServicePackageType { get; set; }

        public string? ServiceMode { get; set; }

        public string Status { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }

        public DateTime? UpdatedAt { get; set; }

        public DateTime? MatchedAt { get; set; }

        public int MatchCount { get; set; }

        public int RespondersCount { get; set; }
    }

    /// <summary>
    /// A single caregiver match result with scoring breakdown
    /// </summary>
    public class CaregiverMatchDTO
    {
        public int Rank { get; set; }
        public string CaregiverId { get; set; } = string.Empty;
        public string CaregiverName { get; set; } = string.Empty;
        public string? ProfileImage { get; set; }
        public bool IsAvailable { get; set; }
        public string? AboutMe { get; set; }
        public string? Location { get; set; }
        public double MatchScore { get; set; }
        public string MatchedServiceCategory { get; set; } = string.Empty;
        public string? GigTitle { get; set; }
        public int? GigPrice { get; set; }
        public double? DistanceKm { get; set; }
        public double AverageRating { get; set; }
        public int ReviewCount { get; set; }
        public MatchScoreBreakdownDTO ScoreBreakdown { get; set; } = new();
    }

    public class MatchScoreBreakdownDTO
    {
        public double CategoryScore { get; set; }
        public double ProximityScore { get; set; }
        public double BudgetScore { get; set; }
        public double RatingScore { get; set; }
        public double PreferenceScore { get; set; }
        public double EngagementScore { get; set; }
        public double ProfileScore { get; set; }
    }

    /// <summary>
    /// Response wrapper for care request matches
    /// </summary>
    public class CareRequestMatchResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string CareRequestId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int TotalMatches { get; set; }
        public bool HasAlternatives { get; set; }
        public List<CaregiverMatchDTO> Matches { get; set; } = new();
    }

    /// <summary>
    /// Response wrapper for care request operations
    /// </summary>
    public class CareRequestResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public CareRequestDTO? Data { get; set; }
    }

    /// <summary>
    /// Response wrapper for multiple care requests
    /// </summary>
    public class CareRequestListResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public List<CareRequestDTO> Data { get; set; } = new List<CareRequestDTO>();
        public int TotalCount { get; set; }
    }
}
