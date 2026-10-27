using System;
using System.Collections.Generic;

namespace Application.DTOs
{
    /// <summary>Proof-of-address (power-holding / NEPA / electricity bill) state attached to one address-history record.</summary>
    public class AddressProofInfoDTO
    {
        /// <summary>True only for the current address (no move-out date) — past addresses are never asked for a bill.</summary>
        public bool Required { get; set; }
        /// <summary>none | pending | approved | rejected.</summary>
        public string Status { get; set; } = "none";
        public DateTime? BillDate { get; set; }
        public string? NameOnBill { get; set; }
        public DateTime? SubmittedAt { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public string? RejectionReason { get; set; }
        public bool CanSubmit { get; set; }
        public int MaxBillAgeMonths { get; set; }
        public int MaxFileSizeMB { get; set; }
        /// <summary>True once the AddressProof enforcement date has been reached (a bill is then required for the step to be complete).</summary>
        public bool Enforced { get; set; }
        public DateTime? EnforcementDate { get; set; }
    }

    /// <summary>Server-side request built by the controller from the multipart form.</summary>
    public class SubmitAddressProofRequest
    {
        public string? BillDate { get; set; }
        public string? NameOnBill { get; set; }
        public ClearanceUploadFile? File { get; set; }
    }

    // ── Admin review ──

    public class AddressProofChecklistDTO
    {
        public bool? NameMatchesProfile { get; set; }
        public bool? AddressMatchesEntry { get; set; }
        public bool? BillWithinAllowedAge { get; set; }
        public bool? ScanLegibleAndUnaltered { get; set; }
    }

    public class ApproveAddressProofRequest
    {
        public AddressProofChecklistDTO? Checklist { get; set; }
        public string? Notes { get; set; }
    }

    public class AdminAddressProofListItemDTO
    {
        /// <summary>The address-history record id (proof lives on the record).</summary>
        public string Id { get; set; } = string.Empty;
        public string? CaregiverId { get; set; }
        public string CaregiverName { get; set; } = string.Empty;
        public string? CaregiverEmail { get; set; }
        public string Status { get; set; } = "pending";
        public string Address { get; set; } = string.Empty;
        public DateTime MovedIn { get; set; }
        public DateTime? SubmittedAt { get; set; }
        public DateTime? BillDate { get; set; }
        public string? NameOnBill { get; set; }
        public DateTime? DecisionAt { get; set; }
        public string? DecidedByEmail { get; set; }
    }

    public class AdminAddressProofDetailDTO : AdminAddressProofListItemDTO
    {
        public string ProfileName { get; set; } = string.Empty;
        /// <summary>likely_match | check_manually — a hint only; the reviewer decides.</summary>
        public string NameMatchHint { get; set; } = "check_manually";
        public int MaxBillAgeMonths { get; set; }
        public string? FileName { get; set; }
        public string? FileContentType { get; set; }
        public AddressProofChecklistDTO? Checklist { get; set; }
        public string? ReviewNotes { get; set; }
        public string? RejectionReason { get; set; }
        public bool CanReview { get; set; }
    }
}
