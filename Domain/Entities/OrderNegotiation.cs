using MongoDB.Bson;

namespace Domain.Entities
{
    public class OrderNegotiation
    {
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

        // References
        public string OrderId { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string CaregiverId { get; set; } = string.Empty;
        public string? GigId { get; set; }

        // Status
        public NegotiationStatus Status { get; set; } = NegotiationStatus.Drafting;

        // Tasks — each party proposes, then agree
        public List<string> ClientProposedTasks { get; set; } = new List<string>();
        public List<string> CaregiverProposedTasks { get; set; } = new List<string>();
        public List<string> AgreedTasks { get; set; } = new List<string>();

        // Schedule — each party proposes, then agree
        public List<NegotiationScheduleSlot> ClientProposedSchedule { get; set; } = new List<NegotiationScheduleSlot>();
        public List<NegotiationScheduleSlot> CaregiverProposedSchedule { get; set; } = new List<NegotiationScheduleSlot>();
        public List<NegotiationScheduleSlot> AgreedSchedule { get; set; } = new List<NegotiationScheduleSlot>();

        // Service details (client fills these — only client has address info)
        public string? ServiceAddress { get; set; }
        public double? ServiceLatitude { get; set; }
        public double? ServiceLongitude { get; set; }
        public string? AccessInstructions { get; set; }
        public string? SpecialClientRequirements { get; set; }
        public string? AdditionalNotes { get; set; }

        // Agreement flags
        public bool ClientAgreed { get; set; } = false;
        public DateTime? ClientAgreedAt { get; set; }
        public bool CaregiverAgreed { get; set; } = false;
        public DateTime? CaregiverAgreedAt { get; set; }

        // Free-text notes per round
        public string? LastClientNote { get; set; }
        public string? LastCaregiverNote { get; set; }

        // Agreed start date for the contract (both parties must agree)
        public DateTime? AgreedStartDate { get; set; }

        // Meta
        public string CreatedByRole { get; set; } = string.Empty; // "Client" or "Caregiver"
        public int NegotiationRound { get; set; } = 1;
        public string? ContractId { get; set; } // Set when converted to contract
        public string? AbandonedByRole { get; set; }
        public string? AbandonReason { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class NegotiationScheduleSlot
    {
        public string DayOfWeek { get; set; } = string.Empty;
        public string StartTime { get; set; } = string.Empty; // HH:mm
        public string EndTime { get; set; } = string.Empty;   // HH:mm
    }

    public enum NegotiationStatus
    {
        Drafting,
        PendingCaregiverReview,
        PendingClientReview,
        BothAgreed,
        ConvertedToContract,
        Abandoned
    }
}
