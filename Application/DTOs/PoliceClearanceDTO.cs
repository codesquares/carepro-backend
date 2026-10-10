using System;
using System.Collections.Generic;

namespace Application.DTOs
{
    /// <summary>One uploaded part, already read into memory by the controller. The id of the caregiver is never part of this.</summary>
    public class ClearanceUploadFile
    {
        public string FileName { get; set; } = string.Empty;
        public string? ContentType { get; set; }
        public byte[] Bytes { get; set; } = Array.Empty<byte>();
    }

    public class SubmitPoliceClearanceRequest
    {
        public string? IssueDate { get; set; }
        public string? NameOnDocument { get; set; }
        public string? ReferenceNumber { get; set; }
        public List<ClearanceUploadFile> Files { get; set; } = new();
    }

    public class ClearanceFileInfoDTO
    {
        public int Index { get; set; }
        public string? FileName { get; set; }
        public string? ContentType { get; set; }
        public long? SizeBytes { get; set; }
    }

    public class ClearanceHistoryItemDTO
    {
        public string? Id { get; set; }
        public string Status { get; set; } = "none";
        public DateTime? SubmittedAt { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public DateTime? IssueDate { get; set; }
        public string? Reason { get; set; }
        public bool Superseded { get; set; }
    }

    /// <summary>The caregiver's own view. Never contains a storage URL or public id.</summary>
    public class PoliceClearanceStatusResponse
    {
        /// <summary>none | pending | approved | rejected | revoked</summary>
        public string Status { get; set; } = "none";
        public string? ClearanceId { get; set; }
        public DateTime? IssueDate { get; set; }
        public DateTime? SubmittedAt { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public string? NameOnDocument { get; set; }
        public string? ReferenceNumber { get; set; }
        /// <summary>Set when status is rejected.</summary>
        public string? RejectionReason { get; set; }
        /// <summary>Set when status is revoked.</summary>
        public string? RevokedReason { get; set; }
        public bool CanResubmit { get; set; }
        public int MaxAgeMonths { get; set; }
        /// <summary>True once the global enforcement date has been reached.</summary>
        public bool Enforced { get; set; }
        /// <summary>UTC enforcement date, or null when none is set ("required from" is shown only when set).</summary>
        public DateTime? EnforcementDate { get; set; }
        public List<ClearanceFileInfoDTO> Files { get; set; } = new();
        public List<ClearanceHistoryItemDTO> History { get; set; } = new();
    }

    public class ClearanceFileContent
    {
        public byte[] Bytes { get; set; } = Array.Empty<byte>();
        public string ContentType { get; set; } = "application/octet-stream";
        public string FileName { get; set; } = "clearance";
    }
}

namespace Application.DTOs
{
    // ── Admin review (Operations/Policy admins only) ──

    public class ClearanceChecklistDTO
    {
        public bool? NameMatchesProfile { get; set; }
        public bool? IssuedByNigeriaPoliceForce { get; set; }
        public bool? IssueDateWithinAllowedAge { get; set; }
        public bool? ReferenceNumberCaptured { get; set; }
        public bool? ScanLegibleAndUnaltered { get; set; }
    }

    /// <summary>No admin id field exists on purpose: the reviewer always comes from the JWT.</summary>
    public class ApprovePoliceClearanceRequest
    {
        public ClearanceChecklistDTO? Checklist { get; set; }
        public string? Notes { get; set; }
    }

    public class ClearanceReasonRequest
    {
        public string? Reason { get; set; }
    }

    public class AdminClearanceListItemDTO
    {
        public string? Id { get; set; }
        public string? CaregiverId { get; set; }
        public string? CaregiverName { get; set; }
        public string? CaregiverEmail { get; set; }
        public string Status { get; set; } = "pending";
        public DateTime? SubmittedAt { get; set; }
        public DateTime? IssueDate { get; set; }
        public string? NameOnDocument { get; set; }
        public string? ReferenceNumber { get; set; }
        public int FileCount { get; set; }
        public DateTime? DecisionAt { get; set; }
        public string? DecidedByEmail { get; set; }
    }

    public class AdminClearanceDetailDTO : AdminClearanceListItemDTO
    {
        public string? ProfileName { get; set; }
        /// <summary>A HINT only: likely_match | check_manually. Staff still confirm the checklist themselves.</summary>
        public string NameMatchHint { get; set; } = "check_manually";
        public int MaxAgeMonths { get; set; }
        public List<ClearanceFileInfoDTO> Files { get; set; } = new();
        public ClearanceChecklistDTO? Checklist { get; set; }
        public string? ReviewNotes { get; set; }
        public string? RejectionReason { get; set; }
        public string? RevokedReason { get; set; }
        public string? RevokedByEmail { get; set; }
        public DateTime? RevokedAt { get; set; }
        public bool IsLatest { get; set; }
        public bool CanApprove { get; set; }
        public bool CanReject { get; set; }
        public bool CanRevoke { get; set; }
        public List<ClearanceHistoryItemDTO> History { get; set; } = new();
    }

    public class AdminIdentity
    {
        public string Id { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }
}
