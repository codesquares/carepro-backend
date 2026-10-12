using System;

namespace Application.DTOs
{
    public class CreateVisitAbsenceRequest
    {
        /// <summary>The day the caregiver cannot attend, as yyyy-MM-dd (Africa/Lagos calendar day).</summary>
        public string Date { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
    }

    public class VisitAbsenceNoticeDTO
    {
        public string Id { get; set; } = string.Empty;
        public string AssignmentId { get; set; } = string.Empty;
        public string PackageRequestId { get; set; } = string.Empty;
        /// <summary>yyyy-MM-dd</summary>
        public string Date { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public bool ShortNotice { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
