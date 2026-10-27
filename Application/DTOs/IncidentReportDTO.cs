using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Application.DTOs
{
    // ── Request DTOs ──

    public class CreateIncidentReportRequest
    {
        /// <summary>Legacy order-based report. Exactly one of OrderId / AssignmentId must be supplied.</summary>
        public string? OrderId { get; set; }

        /// <summary>Package-assignment report (alternative to OrderId).</summary>
        public string? AssignmentId { get; set; }

        public string? TaskSheetId { get; set; }

        [Required]
        public string IncidentType { get; set; } = string.Empty;

        [Required]
        public DateTime DateTime { get; set; }

        [Required]
        [MaxLength(3000)]
        public string Description { get; set; } = string.Empty;

        [MaxLength(2000)]
        public string? ActionsTaken { get; set; }

        public string? Witnesses { get; set; }

        [Required]
        public string Severity { get; set; } = string.Empty;

        public List<string>? Photos { get; set; }

        [Required]
        public DateTime ReportedAt { get; set; }
    }

    // ── Response DTOs ──

    public class IncidentReportDTO
    {
        public string Id { get; set; } = string.Empty;
        public string OrderId { get; set; } = string.Empty;
        public string? AssignmentId { get; set; }
        public string? PackageRequestId { get; set; }
        public string? TaskSheetId { get; set; }
        public string CaregiverId { get; set; } = string.Empty;
        public string IncidentType { get; set; } = string.Empty;
        public DateTime DateTime { get; set; }
        public string Description { get; set; } = string.Empty;
        public string? ActionsTaken { get; set; }
        public string? Witnesses { get; set; }
        public string Severity { get; set; } = string.Empty;
        public List<string> PhotoUrls { get; set; } = new List<string>();
        public DateTime ReportedAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
