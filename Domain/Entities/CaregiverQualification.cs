using MongoDB.Bson;
using System;

namespace Domain.Entities
{
    /// <summary>
    /// LinkedIn-style certification / professional qualification record shown on
    /// a caregiver's public service/gig detail page. Distinct from the document-
    /// verification `Certification` entity which represents uploaded credential
    /// files reviewed via Dojah / admin workflow.
    /// </summary>
    public class CaregiverQualification
    {
        public ObjectId Id { get; set; }

        public string CaregiverId { get; set; } = string.Empty;

        public string CertificationName { get; set; } = string.Empty;

        public string IssuingOrganisation { get; set; } = string.Empty;

        /// <summary>1-12.</summary>
        public int IssueMonth { get; set; }

        public int IssueYear { get; set; }

        /// <summary>1-12. Null when DoesNotExpire = true.</summary>
        public int? ExpiryMonth { get; set; }

        /// <summary>Null when DoesNotExpire = true.</summary>
        public int? ExpiryYear { get; set; }

        public bool DoesNotExpire { get; set; }

        public string? CredentialId { get; set; }

        public string? CredentialUrl { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }
    }
}
