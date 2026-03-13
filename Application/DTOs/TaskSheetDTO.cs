using System;
using System.Collections.Generic;

namespace Application.DTOs
{
    // ── Response DTOs ──

    public class TaskSheetDTO
    {
        public string Id { get; set; } = string.Empty;
        public string OrderId { get; set; } = string.Empty;
        public string CaregiverId { get; set; } = string.Empty;
        public int SheetNumber { get; set; }
        public int BillingCycleNumber { get; set; }
        public List<TaskSheetItemDTO> Tasks { get; set; } = new List<TaskSheetItemDTO>();
        public string Status { get; set; } = string.Empty;
        public DateTime? SubmittedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

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
}
