using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Entities
{
    public class Caregiver
    {
        public ObjectId Id { get; set; }

        public string FirstName { get; set; }

        public string? MiddleName { get; set; }

        public string LastName { get; set; }

        public string Email { get; set; }

        public string? PhoneNo { get; set; }

        public string Password { get; set; }




        public string Role { get; set; }

        public bool Status { get; set; }

        public string? ProfileImage { get; set; }

        public bool IsDeleted { get; set; }
        public DateTime? DeletedOn { get; set; }

        public bool IsAvailable { get; set; }

        public DateTime CreatedAt { get; set; }


        public string? HomeAddress { get; set; }
        public string? Location { get; set; }
        public string? ReasonForDeactivation { get; set; }


        public string? AboutMe { get; set; }
        public string? IntroVideo { get; set; }

        // Location-related properties for service delivery
        public string? ServiceCity { get; set; }
        public string? ServiceState { get; set; }
        public string? ServiceAddress { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        
        // Google OAuth
        public string? GoogleId { get; set; }
        public string? AuthProvider { get; set; } // "local", "google", "both", or null for existing users

        // Identity verification (Dojah KYC)
        public bool? IsIdentityVerified { get; set; }
        public string? IdentityVerificationStatus { get; set; } // "success", "pending", "failed"
        public DateTime? IdentityVerifiedAt { get; set; }

        // Account deletion (GDPR right-to-erasure)
        // Set when a deletion is requested. UserHardDeleteProcessor anonymises
        // data 30 days after DeletedOn. Can be cleared if user cancels within grace period.
        public DateTime? AccountDeletionRequestedAt { get; set; }

        // ── Vetting: professional classification (Phase 2) ──
        // Nullable so legacy MongoDB documents without the field continue to
        // deserialize (the MongoDB EF Core provider rejects missing non-nullable
        // properties). Null = caregiver has not yet declared a type.
        public CaregiverType? CaregiverType { get; set; }

        // Free-text sub-specialty, e.g. "Midwifery". Only meaningful when
        // CaregiverType == RegisteredNurse, but stored as a general nullable
        // field rather than being type-restricted at the schema level.
        public string? Specialty { get; set; }

        // ── Payroll: experience classification (Phase 9.1) ──
        // Nullable for the same reason as CaregiverType: legacy documents
        // without the field must keep deserializing, and null = not yet
        // classified. Admin-set (drives CaregiverPayRate lookup), not
        // self-declared by the caregiver.
        public ExperienceTier? ExperienceTier { get; set; }

    }

    /// <summary>
    /// Professional classification of a caregiver, captured during vetting.
    /// Distinct from uploaded <see cref="Certification"/> documents and from
    /// LinkedIn-style <see cref="CaregiverQualification"/> entries — this is a
    /// single high-level category, not a credential record.
    /// </summary>
    public enum CaregiverType
    {
        AuxiliaryNurse = 0,
        CHEW = 1,
        RegisteredNurse = 2
    }

    /// <summary>
    /// Experience classification used for payroll rate lookup (Phase 9), alongside
    /// <see cref="CaregiverType"/> in <see cref="CaregiverPayRate"/>. Admin-assigned,
    /// not self-declared, since it directly determines pay.
    /// </summary>
    public enum ExperienceTier
    {
        Junior = 0,
        Mid = 1,
        Senior = 2
    }
}
