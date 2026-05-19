using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Entities
{
    public class Client
    {
        public ObjectId Id { get; set; }

        public string FirstName { get; set; }

        public string? MiddleName { get; set; }

        public string LastName { get; set; }

        public string Email { get; set; }

        public string Role { get; set; }

        public string Password { get; set; }

        public string? HomeAddress { get; set; }

        public string? PhoneNo { get; set; }

        public bool IsDeleted { get; set; }

        public DateTime? DeletedOn { get; set; }

        public bool Status { get; set; }
        public string? ProfileImage { get; set; }

        public DateTime CreatedAt { get; set; }

        // Location-related properties for service preferences
        public string? PreferredCity { get; set; }
        public string? PreferredState { get; set; }
        public string? Address { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        
        // Google OAuth
        public string? GoogleId { get; set; }
        public string? AuthProvider { get; set; } // "local", "google", "both", or null for existing users

        // Identity verification (Dojah KYC) — added May 2026 to mirror Caregiver
        // All nullable so existing Client documents in MongoDB continue to work.
        public bool? IsIdentityVerified { get; set; }
        public string? IdentityVerificationStatus { get; set; } // "success", "pending", "failed"
        public DateTime? IdentityVerifiedAt { get; set; }

        // Account deletion (GDPR right-to-erasure)
        // Set when a deletion is requested. UserHardDeleteProcessor anonymises
        // data 30 days after DeletedOn. Can be cleared if user cancels within grace period.
        public DateTime? AccountDeletionRequestedAt { get; set; }

    }
}
