using MongoDB.Bson;
using System;

namespace Domain.Entities
{
    /// <summary>
    /// A caregiver-nominated guarantor (Phase 2 vetting). Exactly two confirmed
    /// guarantors are required per caregiver — that cardinality rule is enforced
    /// at the service layer, not as a hard DB constraint, matching the existing
    /// project convention for similar rules.
    ///
    /// Guarantors are NOT platform users: they confirm via a self-serve signed
    /// link (JWT with a "guarantor_confirmation" purpose claim identifying the
    /// GuarantorId) delivered by email. The attempt/cooldown fields mirror
    /// <see cref="Verification"/> so the same cost-control gating logic applies.
    /// </summary>
    public class Guarantor
    {
        public ObjectId Id { get; set; }

        public string CaregiverId { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        /// <summary>e.g. "Uncle", "Former employer", "Pastor".</summary>
        public string RelationshipToCaregiver { get; set; } = string.Empty;

        public string PhoneNo { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public string Address { get; set; } = string.Empty;

        /// <summary>"pending" | "confirmed" — mirrors Verification.VerificationStatus.</summary>
        public string Status { get; set; } = GuarantorStatuses.Pending;

        /// <summary>Set when the guarantor confirms (self-serve link OR staff override). Null while pending.</summary>
        public DateTime? VerifiedAt { get; set; }

        /// <summary>
        /// How the confirmation happened: <see cref="GuarantorConfirmationMethods.SelfServe"/>
        /// (guarantor clicked their emailed link) or
        /// <see cref="GuarantorConfirmationMethods.StaffOverride"/> (an operations staff member
        /// manually confirmed — e.g. email bounced or verified by phone). Null while pending.
        /// </summary>
        public string? ConfirmationMethod { get; set; }

        /// <summary>AdminId of the staff member who performed a manual override. Null for self-serve.</summary>
        public string? ConfirmedByAdminId { get; set; }

        /// <summary>Email of the staff member who performed a manual override. Null for self-serve.</summary>
        public string? ConfirmedByAdminEmail { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime? UpdatedOn { get; set; }

        // ── Cost-control gate fields (shape copied from Verification.cs) ──
        // Nullable / default 0 so existing MongoDB documents deserialize cleanly.

        /// <summary>Number of confirmation links sent for this guarantor. Treat null as 0.</summary>
        public int? AttemptCount { get; set; }

        /// <summary>Timestamp of the most recent confirmation link send.</summary>
        public DateTime? LastAttemptAt { get; set; }

        /// <summary>
        /// If set and in the future, a new confirmation link cannot be sent yet.
        /// Cleared on confirmation.
        /// </summary>
        public DateTime? CooldownUntil { get; set; }
    }

    public static class GuarantorStatuses
    {
        public const string Pending = "pending";
        public const string Confirmed = "confirmed";
    }

    public static class GuarantorConfirmationMethods
    {
        public const string SelfServe = "self_serve";
        public const string StaffOverride = "staff_override";
    }
}
