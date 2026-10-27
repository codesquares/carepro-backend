using MongoDB.Bson;
using System;
using System.Collections.Generic;

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

        /// <summary>
        /// One of <see cref="GuarantorRelationshipCategories"/>. Declared by the caregiver and compared
        /// against what the guarantor says. Null on guarantors added before the questionnaire existed.
        /// </summary>
        public string? RelationshipCategory { get; set; }

        /// <summary>Years the caregiver says they have known the guarantor. Null on pre-questionnaire guarantors.</summary>
        public int? YearsKnown { get; set; }

        /// <summary>"pending" | "needs_review" | "confirmed" | "rejected" — see <see cref="GuarantorStatuses"/>.</summary>
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

        // ── Guarantor's own questionnaire answers (anonymous submission via the emailed link) ──
        // Flat nullable fields so pre-existing documents deserialize unchanged.

        /// <summary>When the guarantor submitted the questionnaire. Null until they do.</summary>
        public DateTime? ResponsesSubmittedAt { get; set; }

        /// <summary>The caregiver's full name as the guarantor typed it.</summary>
        public string? RespCaregiverName { get; set; }

        /// <summary>One of <see cref="GuarantorRelationshipCategories"/>, chosen by the guarantor.</summary>
        public string? RespRelationshipCategory { get; set; }

        public int? RespYearsKnown { get; set; }

        /// <summary>One of <see cref="GuarantorLastContact"/>.</summary>
        public string? RespLastContact { get; set; }

        /// <summary>Where the guarantor says the caregiver lives. Shown to staff only — never auto-checked.</summary>
        public string? RespCaregiverArea { get; set; }

        /// <summary>Free text: how they know the caregiver and what they have seen of how they treat others.</summary>
        public string? RespHowTheyKnow { get; set; }

        /// <summary>One of <see cref="GuarantorTrustAnswers"/>.</summary>
        public string? RespTrustAnswer { get; set; }

        public string? RespTrustExplanation { get; set; }

        /// <summary>Optional phone number the guarantor typed, compared with <see cref="PhoneNo"/>.</summary>
        public string? RespPhoneNo { get; set; }

        /// <summary>
        /// Auto-check outcome: names of the checks that did not pass (see <see cref="GuarantorCheckNames"/>).
        /// Null before submission; empty after a clean submission.
        /// </summary>
        public List<string>? ReviewFlags { get; set; }

        // ── Staff review (needs_review → confirmed | rejected) ──

        public string? ReviewedByAdminId { get; set; }

        public string? ReviewedByAdminEmail { get; set; }

        public DateTime? ReviewedAt { get; set; }

        public string? ReviewNote { get; set; }

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

        /// <summary>Guarantor answered the questionnaire but at least one auto-check flagged; staff decide.</summary>
        public const string NeedsReview = "needs_review";

        /// <summary>Staff rejected the guarantor after review. The caregiver must remove and re-add.</summary>
        public const string Rejected = "rejected";
    }

    public static class GuarantorConfirmationMethods
    {
        public const string SelfServe = "self_serve";
        public const string StaffOverride = "staff_override";

        /// <summary>Guarantor's answers were flagged and a staff member approved them after reading them.</summary>
        public const string StaffReview = "staff_review";
    }

    public static class GuarantorRelationshipCategories
    {
        public const string Relative = "relative";
        public const string Friend = "friend";
        public const string FormerEmployerOrClient = "former_employer_or_client";
        public const string Neighbour = "neighbour";
        public const string ReligiousLeader = "religious_leader";
        public const string Colleague = "colleague";
        public const string Other = "other";

        public static readonly string[] All =
        {
            Relative, Friend, FormerEmployerOrClient, Neighbour, ReligiousLeader, Colleague, Other
        };
    }

    public static class GuarantorLastContact
    {
        public const string ThisWeek = "this_week";
        public const string ThisMonth = "this_month";
        public const string Within6Months = "within_6_months";
        public const string WithinYear = "within_year";
        public const string OverYear = "over_year";

        public static readonly string[] All = { ThisWeek, ThisMonth, Within6Months, WithinYear, OverYear };
    }

    public static class GuarantorTrustAnswers
    {
        public const string Yes = "yes";
        public const string YesWithReservations = "yes_with_reservations";
        public const string No = "no";

        public static readonly string[] All = { Yes, YesWithReservations, No };
    }

    /// <summary>Stable identifiers for the soft-gate auto-checks; persisted in <see cref="Guarantor.ReviewFlags"/>.</summary>
    public static class GuarantorCheckNames
    {
        public const string NameMismatch = "name_mismatch";
        public const string RelationshipMismatch = "relationship_mismatch";
        public const string YearsBelowMinimum = "years_below_minimum";
        public const string YearsMismatch = "years_mismatch";
        public const string StaleContact = "stale_contact";
        public const string TrustReservation = "trust_reservation";
        public const string PhoneMismatch = "phone_mismatch";

        /// <summary>The caregiver's declared relationship/years are missing (guarantor predates the questionnaire).</summary>
        public const string NoDeclaredData = "no_declared_data";
    }
}
