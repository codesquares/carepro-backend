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
        public string PhoneNo { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime? VerifiedAt { get; set; }
        /// <summary>"self_serve" | "staff_override" | null (pending).</summary>
        public string? ConfirmationMethod { get; set; }
        public string? ConfirmedByAdminId { get; set; }
        public string? ConfirmedByAdminEmail { get; set; }
        public int AttemptCount { get; set; }
        public DateTime? LastAttemptAt { get; set; }
        public DateTime? CooldownUntil { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedOn { get; set; }
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

        [Required, StringLength(100, MinimumLength = 2)]
        public string RelationshipToCaregiver { get; set; } = string.Empty;

        [Required, Phone, StringLength(20)]
        public string PhoneNo { get; set; } = string.Empty;

        [Required, EmailAddress, StringLength(256)]
        public string Email { get; set; } = string.Empty;

        [Required, StringLength(500, MinimumLength = 2)]
        public string Address { get; set; } = string.Empty;
    }

    public class UpdateGuarantorRequest : AddGuarantorRequest { }

    /// <summary>Result of a guarantor clicking their self-serve confirmation link.</summary>
    public class GuarantorConfirmationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? GuarantorId { get; set; }
        public string? CaregiverId { get; set; }
        /// <summary>True when this click flipped the status; false when it was already confirmed.</summary>
        public bool NewlyConfirmed { get; set; }
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
