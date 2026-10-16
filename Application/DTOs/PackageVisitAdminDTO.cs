using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Application.DTOs
{
    /// <summary>One package-assignment visit (check-in) as shown to admins reviewing GPS flags.</summary>
    public class PackageVisitAdminRowDTO
    {
        public string CheckinId { get; set; } = string.Empty;
        public string TaskSheetId { get; set; } = string.Empty;
        public int SheetNumber { get; set; }
        public string AssignmentId { get; set; } = string.Empty;
        public string PackageRequestId { get; set; } = string.Empty;
        public string CaregiverId { get; set; } = string.Empty;
        public string CaregiverName { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string ClientName { get; set; } = string.Empty;
        public DateTime? VisitDate { get; set; }
        public DateTime CheckinTimestamp { get; set; }
        public double? DistanceMeters { get; set; }
        public double AccuracyMeters { get; set; }
        public List<string> Reasons { get; set; } = new();
        /// <summary>"hard" | "soft" | "none"</summary>
        public string Severity { get; set; } = "none";
        public bool FlagCleared { get; set; }
        public DateTime? FlagClearedAt { get; set; }
        public string? FlagClearedByAdminEmail { get; set; }
        public string? FlagClearNote { get; set; }
        public string TaskSheetStatus { get; set; } = string.Empty;
        public string? ClientReviewStatus { get; set; }
        public bool HasOpenDispute { get; set; }
    }

    public class PackageVisitAdminQuery
    {
        /// <summary>"hard" | "soft" | "any" (any flag) | null/"all"</summary>
        public string? Flagged { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public string? CaregiverId { get; set; }
        /// <summary>Only flags no admin has cleared yet.</summary>
        public bool UnclearedOnly { get; set; }
        public int Limit { get; set; } = 200;
    }

    public class ClearVisitFlagRequest
    {
        [Required, StringLength(1000, MinimumLength = 3)]
        public string Note { get; set; } = string.Empty;
    }
}
