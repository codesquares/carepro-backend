using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Entities
{
    public class Verification
    {
        public ObjectId VerificationId { get; set; }
        public string UserId { get; set; }
        //public string VerifiedFirstName { get; set; }
        //public string VerifiedLastName { get; set; }
        public string VerificationMethod { get; set; }
        public string VerificationNo { get; set; }
        public string VerificationStatus { get; set; }
        public bool IsVerified { get; set; }
        public DateTime VerifiedOn { get; set; }
        public DateTime? UpdatedOn { get; set; }

        // --- Cost-control gate fields (added May 2026) ---
        // These are nullable / default to 0 so that existing MongoDB documents
        // without these fields continue to deserialize cleanly.

        /// <summary>
        /// Number of times the user has initiated a Dojah widget session.
        /// Each successful POST /api/Dojah/initiate-session increments this.
        /// Used to enforce a hard cap on lifetime verification attempts.
        /// Nullable so that legacy MongoDB documents without this field
        /// continue to deserialize (the MongoDB EF Core provider rejects
        /// missing non-nullable properties). Treat null as 0 in code.
        /// </summary>
        public int? AttemptCount { get; set; }

        /// <summary>
        /// Timestamp of the most recent session initiation. Informational
        /// for admin review and analytics.
        /// </summary>
        public DateTime? LastAttemptAt { get; set; }

        /// <summary>
        /// If set and in the future, the user is in a cooldown window after
        /// a failed/abandoned verification and cannot initiate a new session.
        /// Cleared on success; left untouched on pending; set on failure.
        /// </summary>
        public DateTime? CooldownUntil { get; set; }

        /// <summary>
        /// Distinguishes which profile collection to update on webhook receipt.
        /// "Caregiver" or "Client". Null for legacy records — treated as
        /// "Caregiver" for backward compatibility.
        /// </summary>
        public string? UserType { get; set; }
    }
}


//1.UserId
//2.StoredFirstName
//3.StoredLastName
//4.VerifiedFirstName
//5.VerifyLastName
//6.Message
//7.Method
//8.UserType
//9.VerifiedStatus
//10.VerifiedAt
//11.UpdatedAt