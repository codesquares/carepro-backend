using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.DTOs
{
    public class VerificationDTO
    {
        public ObjectId VerificationId { get; set; }
        public string UserId { get; set; }
        public string VerifiedFirstName { get; set; }
        public string VerifiedLastName { get; set; }
        public string VerificationMethod { get; set; }
        public string VerificationStatus { get; set; }
        public DateTime VerifiedOn { get; set; }
        public DateTime? UpdatedOn { get; set; }
    }


    public class AddVerificationRequest
    {
        public string UserId { get; set; }
        public string VerifiedFirstName { get; set; }
        public string VerifiedLastName { get; set; }
        public string VerificationMethod { get; set; }
        public string VerificationNo { get; set; }

        public string VerificationStatus { get; set; }

        // Optional. "Caregiver" or "Client". Null is treated as "Caregiver"
        // for backward compatibility with pre-May-2026 callers.
        public string? UserType { get; set; }

    }

    public class VerificationResponse
    {
        public string VerificationId { get; set; }
        public string UserId { get; set; }
        public string VerificationMethod { get; set; }
        public string VerificationNo { get; set; }
        public bool IsVerified { get; set; }
        public string VerificationStatus { get; set; }
        public DateTime VerifiedOn { get; set; }
        public DateTime? UpdatedOn { get; set; }
        public DateTime? LastWebhookReceivedAt { get; set; }

        // --- Cost-control fields (added May 2026, all optional/defaulted) ---
        public int AttemptCount { get; set; }
        public DateTime? CooldownUntil { get; set; }
        public string? UserType { get; set; }
    }


    public class UpdateVerificationRequest
    {
        public string VerificationMode { get; set; }
        public string VerificationStatus { get; set; }

    }

    // ---------------------------------------------------------------------
    // Cost-control gate DTOs (added May 2026)
    // ---------------------------------------------------------------------

    /// <summary>
    /// Returned by GET /api/Dojah/eligibility and embedded in 403 responses
    /// from POST /api/Dojah/initiate-session when the user is blocked.
    /// </summary>
    public class VerificationGateResponse
    {
        public bool IsEligible { get; set; }

        /// <summary>
        /// Machine-readable reason. One of:
        /// "eligible", "already_verified", "pending_review",
        /// "cooldown_active", "max_attempts_reached".
        /// </summary>
        public string Reason { get; set; } = string.Empty;

        public int AttemptCount { get; set; }

        public int AttemptsRemaining { get; set; }

        public DateTime? CooldownUntil { get; set; }
    }

    /// <summary>
    /// Returned on a successful POST /api/Dojah/initiate-session. The
    /// referenceId here MUST be passed by the frontend into the Dojah widget
    /// metadata.reference_id — it is the only value that links the resulting
    /// webhook back to this CarePro user.
    /// </summary>
    public class InitiateSessionResponse
    {
        public string ReferenceId { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string UserType { get; set; } = string.Empty;
        public DateTime IssuedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public int AttemptCount { get; set; }
        public int AttemptsRemaining { get; set; }
    }
}
