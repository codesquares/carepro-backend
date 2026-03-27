using System;
using System.ComponentModel.DataAnnotations;

namespace Application.DTOs
{
    // ── Request DTOs ──

    public class VisitCheckinRequest
    {
        [Required]
        public string TaskSheetId { get; set; } = string.Empty;

        [Required]
        public string OrderId { get; set; } = string.Empty;

        [Required]
        [Range(-90, 90)]
        public double Latitude { get; set; }

        [Required]
        [Range(-180, 180)]
        public double Longitude { get; set; }

        [Required]
        [Range(0, 10000)]
        public double Accuracy { get; set; }

        [Required]
        public DateTime CheckinTimestamp { get; set; }
    }

    // ── Response DTOs ──

    public class VisitCheckinResponse
    {
        public bool Success { get; set; }
        public string CheckinId { get; set; } = string.Empty;
        public DateTime CheckinTimestamp { get; set; }
        public double? DistanceFromServiceAddress { get; set; }
        public bool AlreadyCheckedIn { get; set; }
    }

    /// <summary>
    /// Structured error response for check-in failures so frontend can show appropriate UI.
    /// </summary>
    public class CheckinErrorResponse
    {
        public string Error { get; set; } = string.Empty;
        public string ErrorCode { get; set; } = string.Empty;
        public double? DistanceMeters { get; set; }
        public int? MaxDistanceMeters { get; set; }
        public string? ScheduledDay { get; set; }
        public string? ScheduledStartTime { get; set; }
        public string? ScheduledEndTime { get; set; }
        public string? CurrentTimeNigeria { get; set; }
    }

    public class VisitCheckinDTO
    {
        public string CheckinId { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double Accuracy { get; set; }
        public double? DistanceFromServiceAddress { get; set; }
        public DateTime CheckinTimestamp { get; set; }
    }
}
