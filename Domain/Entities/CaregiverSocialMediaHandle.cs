using MongoDB.Bson;
using System;

namespace Domain.Entities
{
    /// <summary>
    /// A self-declared social media handle for a caregiver (Phase 2 vetting).
    /// Information capture only — no verification step, mirroring the
    /// <see cref="CaregiverQualification"/> pattern.
    /// </summary>
    public class CaregiverSocialMediaHandle
    {
        public ObjectId Id { get; set; }

        public string CaregiverId { get; set; } = string.Empty;

        /// <summary>e.g. "Facebook", "Instagram", "X", "LinkedIn", "TikTok", "Other".</summary>
        public string Platform { get; set; } = string.Empty;

        /// <summary>The handle or profile URL as entered by the caregiver.</summary>
        public string Handle { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }
    }
}
