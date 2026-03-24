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

        public string? Budget { get; set; }

        public string? SpecialRequirements { get; set; }

        public List<string>? Tasks { get; set; }

        public string? ExperiencePreference { get; set; }

        public string? CertificationPreference { get; set; }

        public string? LanguagePreference { get; set; }

        // Geocoded coordinates resolved from Location or Client's address
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }

        /// <summary>
        /// Status of the care request: pending, matched, accepted, completed, cancelled
        /// </summary>
        public string Status { get; set; } = "pending";

        public DateTime CreatedAt { get; set; }

        public DateTime? UpdatedAt { get; set; }

        public DateTime? MatchedAt { get; set; }

        public int? MatchCount { get; set; }
    }
}
