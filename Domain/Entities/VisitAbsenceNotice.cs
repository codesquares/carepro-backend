using MongoDB.Bson;
using System;

namespace Domain.Entities
{
    /// <summary>Allowed values for <see cref="VisitAbsenceNotice.Category"/> and <see cref="VisitAbsenceNotice.Status"/>.</summary>
    public static class AbsenceCategories
    {
        public const string Illness = "illness";
        public const string FamilyEmergency = "family_emergency";
        public const string Transport = "transport";
        public const string Other = "other";
        public static readonly string[] All = { Illness, FamilyEmergency, Transport, Other };

        /// <summary>A missing/unknown category (notices created before categories existed) reads as "other".</summary>
        public static string Normalize(string? c) =>
            c != null && Array.IndexOf(All, c.Trim().ToLowerInvariant()) >= 0 ? c.Trim().ToLowerInvariant() : Other;

        public static string Label(string? c) => Normalize(c) switch
        {
            Illness => "Illness",
            FamilyEmergency => "Family emergency",
            Transport => "Transport problem",
            _ => "Other"
        };
    }

    public static class AbsenceStatuses
    {
        public const string Open = "open";
        public const string CoverArranged = "cover_arranged";
        public const string Credited = "credited";
        public const string NoActionNeeded = "no_action_needed";
        public const string Closed = "closed";
        public const string Withdrawn = "withdrawn";
        public static readonly string[] All = { Open, CoverArranged, Credited, NoActionNeeded, Closed, Withdrawn };

        /// <summary>A missing status (notices created before the workflow existed) reads as open.</summary>
        public static string Normalize(string? s) => string.IsNullOrWhiteSpace(s) ? Open : s;
    }

    /// <summary>
    /// A package-assignment caregiver telling the client and CarePro they cannot attend a given day.
    /// A notice does NOT end at an alert: staff work it through <see cref="Status"/> (open → cover_arranged /
    /// credited / no_action_needed / closed). Every status value is a RECORD for staff — it moves no money,
    /// changes no payroll, no task sheet and no assignment; any actual credit or cover is arranged manually
    /// outside the system for now. Every field added after the first release is nullable so earlier documents
    /// still load (the Mongo EF provider rejects missing non-nullable properties).
    /// </summary>
    public class VisitAbsenceNotice
    {
        public ObjectId Id { get; set; } = ObjectId.GenerateNewId();
        public string AssignmentId { get; set; } = string.Empty;
        public string PackageRequestId { get; set; } = string.Empty;
        public string CaregiverId { get; set; } = string.Empty;
        /// <summary>The calendar day (Africa/Lagos, date-only) the caregiver cannot attend.</summary>
        public DateTime AbsenceDate { get; set; }

        /// <summary>The caregiver's optional free-text note (legacy: the old required "reason"). Shown to staff only —
        /// never to the client, never logged. Empty when none was given.</summary>
        public string Reason { get; set; } = string.Empty;

        /// <summary>Required reason category; null on pre-category notices (reads as "other"). This is all the client sees.</summary>
        public string? Category { get; set; }

        /// <summary>FLAG ONLY: given less than 24h before the assumed 09:00 Lagos start. Drives review priority and wording;
        /// nothing penalises the caregiver (no matching/payroll effect).</summary>
        public bool ShortNotice { get; set; }

        /// <summary>See <see cref="AbsenceStatuses"/>; null reads as open. Concurrency token — a caregiver withdrawal and a staff
        /// change can't both win.</summary>
        public string? Status { get; set; }
        public string? StatusChangedByAdminId { get; set; }
        public DateTime? StatusChangedAt { get; set; }
        public string? StatusNote { get; set; }

        public DateTime? WithdrawnAt { get; set; }
        /// <summary>The caregiver id or admin id that withdrew it.</summary>
        public string? WithdrawnById { get; set; }

        /// <summary>Once-only claim (concurrency token) for the "still open, day within 24h" re-alert to admins.</summary>
        public DateTime? EscalatedAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>Audit trail of everything that happened to a notice after it was given.</summary>
    public class VisitAbsenceNoticeEvent
    {
        public ObjectId Id { get; set; } = ObjectId.GenerateNewId();
        public string NoticeId { get; set; } = string.Empty;
        /// <summary>"status" | "withdrawn" | "escalated"</summary>
        public string Kind { get; set; } = "status";
        public string? FromStatus { get; set; }
        public string? ToStatus { get; set; }
        /// <summary>"admin" | "caregiver" | "system"</summary>
        public string ActorType { get; set; } = "admin";
        public string? ActorId { get; set; }
        public string? Note { get; set; }
        public DateTime At { get; set; } = DateTime.UtcNow;
    }
}
