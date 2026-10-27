using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Application.DTOs
{
    // ─────────────────── 2.1  CLASSIFICATION (type + specialty) ───────────────────

    public class CaregiverClassificationResponse
    {
        public string CaregiverId { get; set; } = string.Empty;

        /// <summary>One of: AuxiliaryNurse, CHEW, RegisteredNurse. Null when not yet set.</summary>
        public string? CaregiverType { get; set; }

        /// <summary>Free-text sub-specialty, e.g. "Midwifery". Only meaningful for RegisteredNurse.</summary>
        public string? Specialty { get; set; }
    }

    public class SetCaregiverClassificationRequest
    {
        [Required]
        [RegularExpression("^(AuxiliaryNurse|CHEW|RegisteredNurse)$",
            ErrorMessage = "CaregiverType must be one of: AuxiliaryNurse, CHEW, RegisteredNurse.")]
        public string CaregiverType { get; set; } = string.Empty;

        [StringLength(120)]
        public string? Specialty { get; set; }
    }

    // ─────────────────── 9.1  EXPERIENCE TIER (payroll) ───────────────────

    public class CaregiverExperienceTierResponse
    {
        public string CaregiverId { get; set; } = string.Empty;

        /// <summary>One of: Junior, Mid, Senior. Null when not yet classified.</summary>
        public string? ExperienceTier { get; set; }
    }

    public class SetCaregiverExperienceTierRequest
    {
        [Required]
        [RegularExpression("^(Junior|Mid|Senior)$",
            ErrorMessage = "ExperienceTier must be one of: Junior, Mid, Senior.")]
        public string ExperienceTier { get; set; } = string.Empty;
    }

    // ─────────────────────────── 2.2  GUARANTORS ───────────────────────────

    public class GuarantorResponse
    {
        public string Id { get; set; } = string.Empty;
        public string CaregiverId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string RelationshipToCaregiver { get; set; } = string.Empty;
        /// <summary>One of the GuarantorRelationshipCategories values; null for guarantors added before the questionnaire.</summary>
        public string? RelationshipCategory { get; set; }
        public int? YearsKnown { get; set; }
        public string PhoneNo { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        /// <summary>"pending" | "needs_review" | "confirmed" | "rejected".</summary>
        public string Status { get; set; } = string.Empty;
        public DateTime? VerifiedAt { get; set; }
        /// <summary>"self_serve" | "staff_override" | "staff_review" | null (not confirmed).</summary>
        public string? ConfirmationMethod { get; set; }
        public string? ConfirmedByAdminId { get; set; }
        public string? ConfirmedByAdminEmail { get; set; }
        public int AttemptCount { get; set; }
        public DateTime? LastAttemptAt { get; set; }
        public DateTime? CooldownUntil { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedOn { get; set; }
    }

    /// <summary>
    /// Staff-only view of a guarantor: the caregiver-facing fields plus what the guarantor answered and
    /// which auto-checks flagged. Never returned from a caregiver endpoint.
    /// </summary>
    public class AdminGuarantorResponse : GuarantorResponse
    {
        public string CaregiverName { get; set; } = string.Empty;
        public DateTime? ResponsesSubmittedAt { get; set; }
        public string? RespCaregiverName { get; set; }
        public string? RespRelationshipCategory { get; set; }
        public int? RespYearsKnown { get; set; }
        public string? RespLastContact { get; set; }
        public string? RespCaregiverArea { get; set; }
        public string? RespHowTheyKnow { get; set; }
        public string? RespTrustAnswer { get; set; }
        public string? RespTrustExplanation { get; set; }
        public string? RespPhoneNo { get; set; }
        /// <summary>Names of the auto-checks that did not pass; null before submission, empty if all passed.</summary>
        public List<string>? ReviewFlags { get; set; }
        public string? ReviewedByAdminEmail { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public string? ReviewNote { get; set; }
    }

    /// <summary>Staff decision on a guarantor sitting in needs_review.</summary>
    public class AdminReviewGuarantorRequest
    {
        [Required, RegularExpression("^(approve|reject)$", ErrorMessage = "Decision must be 'approve' or 'reject'.")]
        public string Decision { get; set; } = string.Empty;

        [Required, MinLength(5, ErrorMessage = "Note must be at least 5 characters")]
        public string Note { get; set; } = string.Empty;
    }

    /// <summary>Body for the staff manual-confirm (override) endpoint. Reason is required for the audit trail.</summary>
    public class AdminConfirmGuarantorRequest
    {
        [Required]
        [MinLength(5, ErrorMessage = "Reason must be at least 5 characters")]
        public string Reason { get; set; } = string.Empty;
    }

    public class AddGuarantorRequest
    {
        [Required, StringLength(150, MinimumLength = 2)]
        public string Name { get; set; } = string.Empty;

        /// <summary>Detail wording, e.g. "Uncle", "Pastor".</summary>
        [Required, StringLength(100, MinimumLength = 2)]
        public string RelationshipToCaregiver { get; set; } = string.Empty;

        [Required, RegularExpression("^(relative|friend|former_employer_or_client|neighbour|religious_leader|colleague|other)$",
            ErrorMessage = "RelationshipCategory is not a recognised category.")]
        public string RelationshipCategory { get; set; } = string.Empty;

        [Range(2, 80, ErrorMessage = "A guarantor must have known you for at least 2 years.")]
        public int YearsKnown { get; set; }

        [Required, Phone, StringLength(20)]
        public string PhoneNo { get; set; } = string.Empty;

        [Required, EmailAddress, StringLength(256)]
        public string Email { get; set; } = string.Empty;

        [Required, StringLength(500, MinimumLength = 2)]
        public string Address { get; set; } = string.Empty;
    }

    public class UpdateGuarantorRequest : AddGuarantorRequest { }

    /// <summary>
    /// What the anonymous questionnaire page may know before it renders. Deliberately carries nothing the
    /// caregiver declared (relationship, years, phone) — the page must not be able to echo the answers.
    /// </summary>
    public class GuarantorQuizInfo
    {
        public string GuarantorName { get; set; } = string.Empty;
        /// <summary>True once answers were submitted (any outcome) — the page shows "already responded".</summary>
        public bool AlreadyResponded { get; set; }
    }

    /// <summary>The guarantor's answers, submitted from the emailed link.</summary>
    public class SubmitGuarantorResponsesRequest
    {
        [Required, StringLength(150, MinimumLength = 2)]
        public string CaregiverName { get; set; } = string.Empty;

        [Required, RegularExpression("^(relative|friend|former_employer_or_client|neighbour|religious_leader|colleague|other)$",
            ErrorMessage = "RelationshipCategory is not a recognised category.")]
        public string RelationshipCategory { get; set; } = string.Empty;

        [Range(0, 80, ErrorMessage = "Years known must be between 0 and 80.")]
        public int YearsKnown { get; set; }

        [Required, RegularExpression("^(this_week|this_month|within_6_months|within_year|over_year)$",
            ErrorMessage = "LastContact is not a recognised option.")]
        public string LastContact { get; set; } = string.Empty;

        [Required, StringLength(200, MinimumLength = 2)]
        public string CaregiverArea { get; set; } = string.Empty;

        [Required, StringLength(2000)]
        public string HowTheyKnow { get; set; } = string.Empty;

        [Required, RegularExpression("^(yes|yes_with_reservations|no)$",
            ErrorMessage = "TrustAnswer is not a recognised option.")]
        public string TrustAnswer { get; set; } = string.Empty;

        [StringLength(2000)]
        public string? TrustExplanation { get; set; }

        [Phone, StringLength(20)]
        public string? PhoneNo { get; set; }

        /// <summary>The guarantor ticked the declaration that the answers are true.</summary>
        [Range(typeof(bool), "true", "true", ErrorMessage = "The declaration must be accepted.")]
        public bool Declaration { get; set; }
    }

    /// <summary>
    /// Result of a guarantor submitting the questionnaire. Identical wording whether the answers
    /// auto-confirmed or went to staff review, so a failed check gives the submitter no feedback.
    /// </summary>
    public class GuarantorConfirmationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? GuarantorId { get; set; }
    }

    // ─────────────────────── 2.3  ADDRESS HISTORY ───────────────────────

    public class CaregiverAddressHistoryResponse
    {
        public string Id { get; set; } = string.Empty;
        public string CaregiverId { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public DateTime MovedIn { get; set; }
        public DateTime? MovedOut { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public AddressProofInfoDTO Proof { get; set; } = new();
    }

    public class AddCaregiverAddressHistoryRequest
    {
        [Required, StringLength(500, MinimumLength = 2)]
        public string Address { get; set; } = string.Empty;

        [Required]
        public DateTime MovedIn { get; set; }

        /// <summary>Omit / null for the current address.</summary>
        public DateTime? MovedOut { get; set; }
    }

    public class UpdateCaregiverAddressHistoryRequest : AddCaregiverAddressHistoryRequest { }

    /// <summary>Whether the caregiver's address history satisfies the 5-year / 2-address rule.</summary>
    public class CaregiverAddressHistoryCoverageResponse
    {
        public bool IsComplete { get; set; }
        /// <summary>complete | awaiting_review | incomplete.</summary>
        public string State { get; set; } = "incomplete";
        /// <summary>Whether the date coverage alone is satisfied (so the UI can tell "need a bill" from "need more addresses").</summary>
        public bool DatesCovered { get; set; }
        /// <summary>Whether a bill is required yet (AddressProof enforcement date reached).</summary>
        public bool ProofRequired { get; set; }
        public DateTime? ProofEnforcementDate { get; set; }
        public int RecordCount { get; set; }
        public int RequiredCount { get; set; }
        public int RequiredYears { get; set; }
    }

    // ─────────────────────── 2.4  SOCIAL MEDIA HANDLES ───────────────────────

    public class CaregiverSocialMediaHandleResponse
    {
        public string Id { get; set; } = string.Empty;
        public string CaregiverId { get; set; } = string.Empty;
        public string Platform { get; set; } = string.Empty;
        public string Handle { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class AddCaregiverSocialMediaHandleRequest
    {
        [Required, StringLength(40, MinimumLength = 1)]
        public string Platform { get; set; } = string.Empty;

        [Required, StringLength(300, MinimumLength = 1)]
        public string Handle { get; set; } = string.Empty;
    }

    public class UpdateCaregiverSocialMediaHandleRequest : AddCaregiverSocialMediaHandleRequest { }
}
