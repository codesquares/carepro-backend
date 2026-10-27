using System;
using System.Collections.Generic;

namespace Application.DTOs
{
    // ── Response DTOs ──

    public class TaskSheetDTO
    {
        public string Id { get; set; } = string.Empty;
        public string OrderId { get; set; } = string.Empty;
        public string? AssignmentId { get; set; }
        public string? PackageRequestId { get; set; }
        public string CaregiverId { get; set; } = string.Empty;
        public int SheetNumber { get; set; }
        public int BillingCycleNumber { get; set; }
        public List<TaskSheetItemDTO> Tasks { get; set; } = new List<TaskSheetItemDTO>();
        public string Status { get; set; } = string.Empty;
        public DateTime? ScheduledDate { get; set; }
        public DateTime? SubmittedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public string? ScheduledStartTime { get; set; }
        public string? ScheduledEndTime { get; set; }

        // Visit check-in data (null if not yet checked in)
        public VisitCheckinDTO? Checkin { get; set; }

        // Client signature data (null if not yet signed)
        public ClientSignatureDTO? ClientSignature { get; set; }

        // Client visit review fields
        public string? ClientReviewStatus { get; set; }
        public DateTime? ClientReviewedAt { get; set; }
        public string? ClientDisputeReason { get; set; }

        // Visit duration (minutes from check-in to submission)
        public double? VisitDurationMinutes { get; set; }

        // Report counts for UI badges
        public int ObservationReportCount { get; set; }
        public int IncidentReportCount { get; set; }
    }

    public class TaskSheetItemDTO
    {
        public string? Id { get; set; }
        public string Text { get; set; } = string.Empty;
        public bool Completed { get; set; }
        public bool AddedByCaregiver { get; set; }
        public bool AddedByClient { get; set; }
        /// <summary>
        /// "Accepted", "Pending", or "Rejected". Client-proposed tasks start as "Pending".
        /// </summary>
        public string ProposalStatus { get; set; } = "Accepted";
    }

    public class ClientSignatureDTO
    {
        public string SignatureUrl { get; set; } = string.Empty;
        public DateTime SignedAt { get; set; }
    }

    public class TaskSheetListResponse
    {
        public List<TaskSheetDTO> Sheets { get; set; } = new List<TaskSheetDTO>();
        public int MaxSheets { get; set; }
        public int CurrentSheetCount { get; set; }
    }

    /// <summary>
    /// Monthly hours aggregation for payroll (Phase 9.6) — SUM(VisitDurationMinutes)
    /// across a caregiver's submitted task sheets for a calendar month, optionally
    /// scoped to one Assignment (each package assignment gets its own Payroll row per
    /// Phase 9.7, so its hours need to be summed separately from a caregiver's other
    /// concurrent assignments).
    /// </summary>
    public class CaregiverMonthlyHoursDTO
    {
        public string CaregiverId { get; set; } = string.Empty;
        public string? AssignmentId { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }
        public double TotalMinutes { get; set; }
        public double TotalHours { get; set; }
        public int TaskSheetCount { get; set; }
    }

    // ── Request DTOs ──

    public class CreateTaskSheetRequest
    {
        public string OrderId { get; set; } = string.Empty;
    }

    public class UpdateTaskSheetRequest
    {
        public List<TaskSheetItemDTO> Tasks { get; set; } = new List<TaskSheetItemDTO>();
    }

    public class SubmitTaskSheetRequest
    {
        public string? ClientSignature { get; set; }
        public DateTime? SignedAt { get; set; }
    }

    /// <summary>
    /// Request for client to propose tasks on a task sheet during a visit.
    /// These tasks start as "Pending" and must be accepted by the caregiver.
    /// </summary>
    public class ClientProposeTasksRequest
    {
        public List<ClientProposedTaskItemDTO> Tasks { get; set; } = new List<ClientProposedTaskItemDTO>();
    }

    public class ClientProposedTaskItemDTO
    {
        public string Text { get; set; } = string.Empty;
    }

    /// <summary>
    /// Request for caregiver to accept or reject client-proposed tasks on a task sheet.
    /// </summary>
    public class RespondToProposedTasksRequest
    {
        public List<TaskProposalResponseDTO> Responses { get; set; } = new List<TaskProposalResponseDTO>();
    }

    public class TaskProposalResponseDTO
    {
        public string TaskId { get; set; } = string.Empty;
        public bool Accepted { get; set; }
    }

    /// <summary>
    /// Request to reschedule a scheduled visit's date, time, or both.
    /// At least one of NewDate, or both NewStartTime+NewEndTime, must be provided.
    /// NewStartTime and NewEndTime must always be supplied together.
    /// All times are Nigerian time (WAT, UTC+1).
    /// </summary>
    public class RescheduleTaskSheetRequest
    {
        /// <summary>
        /// The new date for the visit (date-only, Nigerian time). Null = keep current date.
        /// Must be within the contract period and must not conflict with an existing active sheet on that day.
        /// </summary>
        public DateTime? NewDate { get; set; }

        /// <summary>
        /// New start time for this visit only (e.g. "10:00", Nigerian time).
        /// Does not affect the recurring contract schedule. Must be paired with NewEndTime.
        /// </summary>
        public string? NewStartTime { get; set; }

        /// <summary>
        /// New end time for this visit only (e.g. "15:00", Nigerian time).
        /// Does not affect the recurring contract schedule. Must be paired with NewStartTime.
        /// </summary>
        public string? NewEndTime { get; set; }

        /// <summary>
        /// Optional reason for rescheduling.
        /// </summary>
        public string? Reason { get; set; }
    }

    public class RescheduleTaskSheetResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public DateTime OldDate { get; set; }
        public DateTime NewDate { get; set; }
        public string TaskSheetId { get; set; } = string.Empty;
        public string? ScheduledStartTime { get; set; }
        public string? ScheduledEndTime { get; set; }
    }
}
