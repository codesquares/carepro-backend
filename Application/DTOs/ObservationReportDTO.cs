using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Application.DTOs
{
    // ── Request DTOs ──

    public class CreateObservationReportRequest
    {
        [Required]
        public string OrderId { get; set; } = string.Empty;

        [Required]
        public string TaskSheetId { get; set; } = string.Empty;

        [Required]
        public string Category { get; set; } = string.Empty;

        [Required]
        [MaxLength(2000)]
        public string Description { get; set; } = string.Empty;

        [Required]
        public string Severity { get; set; } = string.Empty;

        public List<string>? Photos { get; set; }

        [Required]
        public DateTime ReportedAt { get; set; }
    }

    // ── Response DTOs ──

    public class ObservationReportDTO
    {
        public string Id { get; set; } = string.Empty;
        public string OrderId { get; set; } = string.Empty;
        public string TaskSheetId { get; set; } = string.Empty;
        public string CaregiverId { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;
        public List<string> PhotoUrls { get; set; } = new List<string>();
        public DateTime ReportedAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
