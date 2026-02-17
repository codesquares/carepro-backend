using MongoDB.Bson;
using System;
using System.Collections.Generic;

namespace Domain.Entities
{
    /// <summary>
    /// Defines the requirements (assessments, certificates, scores) for each service category.
    /// Stored in MongoDB so requirements can be updated without redeployment.
    /// </summary>
    public class ServiceRequirement
    {
        public ObjectId Id { get; set; }

        /// <summary>
        /// The service category key, e.g. "MedicalSupport", "PalliativeCare", "HomeCare"
        /// </summary>
        public string ServiceCategory { get; set; } = string.Empty;

        /// <summary>
        /// Human-readable display name, e.g. "Medical Support", "Palliative Care"
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// "general" or "specialized"
        /// </summary>
        public string Tier { get; set; } = "general";

        /// <summary>
        /// List of certificate names required for this service category.
        /// </summary>
        public List<string> RequiredCertificates { get; set; } = new();

        /// <summary>
        /// The assessment category key required (e.g. "MedicalSupport", "General")
        /// </summary>
        public string RequiredAssessment { get; set; } = "General";

        /// <summary>
        /// Minimum passing score (0-100) for the assessment
        /// </summary>
        public int PassingScore { get; set; } = 70;

        /// <summary>
        /// How many questions to serve for this category's assessment
        /// </summary>
        public int QuestionCount { get; set; } = 30;

        /// <summary>
        /// Cooldown in hours between assessment retakes
        /// </summary>
        public int CooldownHours { get; set; } = 24;

        /// <summary>
        /// How many months a passing assessment remains valid. 0 = never expires.
        /// </summary>
        public int AssessmentValidityMonths { get; set; } = 0;

        /// <summary>
        /// How many minutes an assessment session is valid before it expires. Default 60.
        /// </summary>
        public int SessionDurationMinutes { get; set; } = 60;

        public bool Active { get; set; } = true;

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
