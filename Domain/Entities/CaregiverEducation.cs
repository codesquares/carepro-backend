using MongoDB.Bson;
using System;

namespace Domain.Entities
{
    /// <summary>
    /// LinkedIn-style educational background entry shown on a caregiver's
    /// public service/gig detail page. Distinct from the document-verification
    /// `Certification` entity which represents uploaded credential files.
    /// </summary>
    public class CaregiverEducation
    {
        public ObjectId Id { get; set; }

        public string CaregiverId { get; set; } = string.Empty;

        public string SchoolName { get; set; } = string.Empty;

        /// <summary>
        /// Degree type. Allowed values: B.Sc, HND, OND, M.Sc, PhD, Diploma, Certificate, Other.
        /// </summary>
        public string DegreeType { get; set; } = string.Empty;

        public string FieldOfStudy { get; set; } = string.Empty;

        /// <summary>1-12.</summary>
        public int StartMonth { get; set; }

        public int StartYear { get; set; }

        /// <summary>1-12. Null when CurrentlyStudying = true.</summary>
        public int? EndMonth { get; set; }

        /// <summary>Null when CurrentlyStudying = true.</summary>
        public int? EndYear { get; set; }

        public bool CurrentlyStudying { get; set; }

        public string? Grade { get; set; }

        public string? Activities { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }
    }
}
