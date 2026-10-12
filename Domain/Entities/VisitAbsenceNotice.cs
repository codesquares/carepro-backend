using MongoDB.Bson;
using System;

namespace Domain.Entities
{
    /// <summary>
    /// A package-assignment caregiver telling the client and CarePro they cannot attend a given day.
    /// Informational only: it moves no money, cancels no visit sheet and changes no assignment status
    /// (package visits are not pre-scheduled, and caregivers are paid through payroll on approved hours,
    /// so an unattended day simply accrues nothing). The record exists so staff and the client have a
    /// durable account of who said what, when.
    /// </summary>
    public class VisitAbsenceNotice
    {
        public ObjectId Id { get; set; } = ObjectId.GenerateNewId();
        public string AssignmentId { get; set; } = string.Empty;
        public string PackageRequestId { get; set; } = string.Empty;
        public string CaregiverId { get; set; } = string.Empty;
        /// <summary>The calendar day (Africa/Lagos, date-only) the caregiver cannot attend.</summary>
        public DateTime AbsenceDate { get; set; }
        public string Reason { get; set; } = string.Empty;
        /// <summary>True when given less than 24h before 09:00 Lagos on that day — CarePro reviews these.</summary>
        public bool ShortNotice { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
