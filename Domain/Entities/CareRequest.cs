using MongoDB.Bson;
using System;
using System.Collections.Generic;

namespace Domain.Entities
{
    public class CareRequest
    {
        public ObjectId Id { get; set; }

        public string ClientId { get; set; } = string.Empty;

        public string ServiceCategory { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public string? ServiceGroup { get; set; }

        public string? Notes { get; set; }

        public string Urgency { get; set; } = string.Empty;

        public List<string> Schedule { get; set; } = new List<string>();

        public string Frequency { get; set; } = string.Empty;

        public string? Duration { get; set; }

        public string? Location { get; set; }

        /// <summary>
        /// Legacy free-text budget field (kept for backward compatibility).
        /// New requests should populate BudgetMin/BudgetMax/BudgetType instead.
        /// </summary>
        public string? Budget { get; set; }

        /// <summary>Structured budget minimum (e.g. 50000)</summary>
        public decimal? BudgetMin { get; set; }

        /// <summary>Structured budget maximum (e.g. 100000)</summary>
        public decimal? BudgetMax { get; set; }

        /// <summary>Budget type: "Fixed" or "Negotiable"</summary>
        public string? BudgetType { get; set; }

        public string? SpecialRequirements { get; set; }

        public List<string>? Tasks { get; set; }

        public string? ExperiencePreference { get; set; }

        public string? CertificationPreference { get; set; }

        public string? LanguagePreference { get; set; }

        /// <summary>Service package type, e.g. "Basic", "Standard", "Premium"</summary>
        public string? ServicePackageType { get; set; }

        /// <summary>Service mode, e.g. "Live-in", "Visit", "Remote"</summary>
        public string? ServiceMode { get; set; }

        // Geocoded coordinates resolved from Location or Client's address
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }

        /// <summary>
        /// Status of the care request: pending, matched, unmatched, accepted, completed, cancelled, paused, closed
        /// </summary>
        public string Status { get; set; } = "pending";

        public DateTime CreatedAt { get; set; }

        public DateTime? UpdatedAt { get; set; }

        public DateTime? MatchedAt { get; set; }

        public int? MatchCount { get; set; }

        /// <summary>Cached count of caregiver responses received</summary>
        public int? RespondersCount { get; set; }

        /// <summary>Soft-delete timestamp. Null = not deleted.</summary>
        public DateTime? DeletedAt { get; set; }
    }
}
