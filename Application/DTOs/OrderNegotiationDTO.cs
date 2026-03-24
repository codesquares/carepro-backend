using System.ComponentModel.DataAnnotations;

namespace Application.DTOs
{
    // ========================================
    // CREATE NEGOTIATION
    // ========================================
    public class CreateNegotiationDTO
    {
        [Required]
        public string OrderId { get; set; } = string.Empty;

        [Required]
        public string CaregiverId { get; set; } = string.Empty;

        public string? GigId { get; set; }

        [Required]
        public string CreatedByRole { get; set; } = string.Empty; // "Client" or "Caregiver"

        public List<string>? ClientProposedTasks { get; set; }
        public List<string>? CaregiverProposedTasks { get; set; }
        public List<NegotiationScheduleSlotDTO>? ClientProposedSchedule { get; set; }
        public List<NegotiationScheduleSlotDTO>? CaregiverProposedSchedule { get; set; }

        // Client-only fields
        public string? ServiceAddress { get; set; }
        public string? AccessInstructions { get; set; }
        public string? SpecialClientRequirements { get; set; }
        public string? AdditionalNotes { get; set; }

        /// <summary>
        /// If true, client is currently at the service address and device GPS should be used.
        /// </summary>
        public bool ConfirmAtServiceAddress { get; set; }
        public double? ServiceLatitude { get; set; }
        public double? ServiceLongitude { get; set; }

        public string? OpeningNote { get; set; }
    }

    // ========================================
    // CLIENT UPDATE
    // ========================================
    public class ClientNegotiationUpdateDTO
    {
        public List<string>? ClientProposedTasks { get; set; }
        public List<NegotiationScheduleSlotDTO>? ClientProposedSchedule { get; set; }
        public string? SpecialClientRequirements { get; set; }
        public string? ServiceAddress { get; set; }
        public string? AccessInstructions { get; set; }
        public string? Note { get; set; }
        public bool SubmitForCaregiverReview { get; set; } = false;

        /// <summary>
        /// If true, client is currently at the service address and device GPS should be used.
        /// </summary>
        public bool ConfirmAtServiceAddress { get; set; }
        public double? ServiceLatitude { get; set; }
        public double? ServiceLongitude { get; set; }
    }

    // ========================================
    // CAREGIVER UPDATE
    // ========================================
    public class CaregiverNegotiationUpdateDTO
    {
        public List<string>? CaregiverProposedTasks { get; set; }
        public List<NegotiationScheduleSlotDTO>? CaregiverProposedSchedule { get; set; }
        public string? AdditionalNotes { get; set; }
        public string? Note { get; set; }
        public bool SubmitForClientReview { get; set; } = false;
    }

    // ========================================
    // AGREE
    // ========================================
    public class NegotiationAgreeDTO
    {
        [Required]
        public bool ConfirmAgreed { get; set; }
    }

    // ========================================
    // ABANDON
    // ========================================
    public class NegotiationAbandonDTO
    {
        public string? Reason { get; set; }
    }

    // ========================================
    // SCHEDULE SLOT (shared)
    // ========================================
    public class NegotiationScheduleSlotDTO
    {
        [Required]
        public string DayOfWeek { get; set; } = string.Empty;

        [Required]
        public string StartTime { get; set; } = string.Empty; // HH:mm

        [Required]
        public string EndTime { get; set; } = string.Empty;   // HH:mm
    }

    // ========================================
    // RESPONSE DTO
    // ========================================
    public class OrderNegotiationDTO
    {
        public string Id { get; set; } = string.Empty;
        public string OrderId { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string CaregiverId { get; set; } = string.Empty;
        public string? GigId { get; set; }
        public string Status { get; set; } = string.Empty;

        // Tasks
        public List<string> ClientProposedTasks { get; set; } = new();
        public List<string> CaregiverProposedTasks { get; set; } = new();
        public List<string> AgreedTasks { get; set; } = new();

        // Schedule
        public List<NegotiationScheduleSlotDTO> ClientProposedSchedule { get; set; } = new();
        public List<NegotiationScheduleSlotDTO> CaregiverProposedSchedule { get; set; } = new();
        public List<NegotiationScheduleSlotDTO> AgreedSchedule { get; set; } = new();

        // Service details
        public string? ServiceAddress { get; set; }
        public double? ServiceLatitude { get; set; }
        public double? ServiceLongitude { get; set; }
        public string? AccessInstructions { get; set; }
        public string? SpecialClientRequirements { get; set; }
        public string? AdditionalNotes { get; set; }

        // Agreement
        public bool ClientAgreed { get; set; }
        public DateTime? ClientAgreedAt { get; set; }
        public bool CaregiverAgreed { get; set; }
        public DateTime? CaregiverAgreedAt { get; set; }

        // Notes
        public string? LastClientNote { get; set; }
        public string? LastCaregiverNote { get; set; }

        // Meta
        public string CreatedByRole { get; set; } = string.Empty;
        public int NegotiationRound { get; set; }
        public string? ContractId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
