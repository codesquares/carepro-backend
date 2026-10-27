using System;
using System.Collections.Generic;

namespace Application.DTOs
{
    public class CreateVisitAbsenceRequest
    {
        /// <summary>The day the caregiver cannot attend, as yyyy-MM-dd (Africa/Lagos calendar day).</summary>
        public string Date { get; set; } = string.Empty;
        /// <summary>Required: illness | family_emergency | transport | other.</summary>
        public string Category { get; set; } = string.Empty;
        /// <summary>Optional free text (max 500). Staff-only: never shown to the client.</summary>
        public string? Note { get; set; }
    }

    /// <summary>The giving caregiver's own view (includes their own note and whether they can still withdraw).</summary>
    public class VisitAbsenceNoticeDTO
    {
        public string Id { get; set; } = string.Empty;
        public string AssignmentId { get; set; } = string.Empty;
        public string PackageRequestId { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string CategoryLabel { get; set; } = string.Empty;
        public string? Note { get; set; }
        public bool ShortNotice { get; set; }
        public string Status { get; set; } = string.Empty;
        public bool CanWithdraw { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// The client's view: the DATE and CATEGORY only. There is deliberately no note/reason property here, nor any staff note or
    /// admin id — adding one would be a privacy regression (a test asserts the serialized shape).
    /// </summary>
    public class ClientVisitAbsenceNoticeDTO
    {
        public string Id { get; set; } = string.Empty;
        public string PackageRequestId { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string CategoryLabel { get; set; } = string.Empty;
        public bool ShortNotice { get; set; }
        /// <summary>Coarse, client-safe outcome: null (in progress) | cover_arranged | credited | withdrawn.</summary>
        public string? Outcome { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class AdminVisitAbsenceNoticeDTO
    {
        public string Id { get; set; } = string.Empty;
        public string AssignmentId { get; set; } = string.Empty;
        public string PackageRequestId { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty;
        public string PackageName { get; set; } = string.Empty;
        /// <summary>First name only — the list is for triage, not for client contact details.</summary>
        public string ClientFirstName { get; set; } = string.Empty;
        public string CaregiverId { get; set; } = string.Empty;
        public string CaregiverName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string CategoryLabel { get; set; } = string.Empty;
        /// <summary>The caregiver's full note (staff may see it).</summary>
        public string Note { get; set; } = string.Empty;
        public bool ShortNotice { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? StatusNote { get; set; }
        public string? StatusChangedByAdminId { get; set; }
        public DateTime? StatusChangedAt { get; set; }
        public DateTime? WithdrawnAt { get; set; }
        public string? WithdrawnById { get; set; }
        public DateTime? EscalatedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public double AgeHours { get; set; }
        public List<AdminVisitAbsenceEventDTO>? History { get; set; }
    }

    public class AdminVisitAbsenceEventDTO
    {
        public string Kind { get; set; } = string.Empty;
        public string? FromStatus { get; set; }
        public string? ToStatus { get; set; }
        public string ActorType { get; set; } = string.Empty;
        public string? ActorId { get; set; }
        public string? Note { get; set; }
        public DateTime At { get; set; }
    }

    public class SetAbsenceStatusRequest
    {
        public string Status { get; set; } = string.Empty;
        /// <summary>Required (max 500). Staff-only.</summary>
        public string Note { get; set; } = string.Empty;
    }
}
