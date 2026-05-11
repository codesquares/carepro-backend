using MongoDB.Bson;
using System;

namespace Domain.Entities
{
    /// <summary>
    /// LinkedIn-style work experience entry shown on a caregiver's
    /// public service/gig detail page.
    /// </summary>
    public class CaregiverWorkExperience
    {
        public ObjectId Id { get; set; }

        public string CaregiverId { get; set; } = string.Empty;

        public string JobTitle { get; set; } = string.Empty;

        /// <summary>
        /// Allowed values: Full-time, Part-time, Contract, Self-employed, Internship, Volunteer.
        /// </summary>
        public string EmploymentType { get; set; } = string.Empty;

        public string OrganisationName { get; set; } = string.Empty;

        public string Location { get; set; } = string.Empty;

        /// <summary>1-12.</summary>
        public int StartMonth { get; set; }

        public int StartYear { get; set; }

        /// <summary>1-12. Null when CurrentlyWorkingHere = true.</summary>
        public int? EndMonth { get; set; }

        /// <summary>Null when CurrentlyWorkingHere = true.</summary>
        public int? EndYear { get; set; }

        public bool CurrentlyWorkingHere { get; set; }

        public string? Industry { get; set; }

        public string? Description { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }
    }
}
