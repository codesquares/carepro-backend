using MongoDB.Bson;

namespace Domain.Entities
{
    public class Contract
    {
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

        // Order Reference (contract is generated from an existing order)
        public string OrderId { get; set; } = string.Empty;

        // Gig and Payment Integration
        public string GigId { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string CaregiverId { get; set; } = string.Empty;
        public string? PaymentTransactionId { get; set; }

        // Package and Task Details (from Order/Gig - no price negotiation)
        public PackageSelection SelectedPackage { get; set; } = new PackageSelection();
        public List<ClientTask> Tasks { get; set; } = new List<ClientTask>();

        // Contract Terms (LLM generated)
        public string GeneratedTerms { get; set; } = string.Empty;
        public decimal TotalAmount { get; set; }
        public ContractStatus Status { get; set; }

        // NEW: Caregiver-initiated contract generation
        public string? SubmittedByCaregiverId { get; set; }
        public DateTime? SubmittedAt { get; set; }

        // NEW: Agreed Schedule (caregiver submits after negotiation with client)
        public List<ScheduledVisit> Schedule { get; set; } = new List<ScheduledVisit>();

        // NEW: Caregiver Notes (details from client-caregiver discussion)
        public string? ServiceAddress { get; set; }
        public string? SpecialClientRequirements { get; set; }
        public string? AccessInstructions { get; set; }
        public string? CaregiverAdditionalNotes { get; set; }

        // NEW: Client Approval (client approves caregiver's contract)
        public DateTime? ClientApprovedAt { get; set; }
        public string? ClientApprovedBy { get; set; }

        // NEW: Negotiation Round Tracking (max 2 rounds)
        public int NegotiationRound { get; set; } = 1;

        // Client Review Request (when client requests changes)
        public DateTime? ClientReviewRequestedAt { get; set; }
        public string? ClientReviewComments { get; set; }

        // LEGACY: Caregiver Response Management (keeping for backward compatibility)
        public DateTime? SentAt { get; set; }
        public DateTime? RespondedAt { get; set; }
        public DateTime? AcceptedAt { get; set; }
        public string? AcceptedBy { get; set; }
        public DateTime? RejectedAt { get; set; }
        public string? RejectedBy { get; set; }
        public string? RejectionReason { get; set; }
        public DateTime? ReviewRequestedAt { get; set; }
        public string? ReviewRequestedBy { get; set; }
        public string? ReviewComments { get; set; }
        public string CaregiverResponse { get; set; } = string.Empty;
        public List<string> Comments { get; set; } = new List<string>();

        // Contract History
        public DateTime ContractStartDate { get; set; }
        public DateTime ContractEndDate { get; set; }
        public bool IsCompleted { get; set; }
        public decimal? FinalRating { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    // NEW: Scheduled visit structure for agreed schedule
    public class ScheduledVisit
    {
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public DayOfWeek DayOfWeek { get; set; }
        public string StartTime { get; set; } = string.Empty; // e.g., "09:00"
        public string EndTime { get; set; } = string.Empty;   // e.g., "14:00" (4-6 hours from start)
    }

    public class PackageSelection
    {
        public string PackageType { get; set; } = string.Empty; // "1_visit_per_week", "3_visits_per_week", "5_visits_per_week"
        public int VisitsPerWeek { get; set; }
        public decimal PricePerVisit { get; set; }
        public decimal TotalWeeklyPrice { get; set; }
        public int DurationWeeks { get; set; }
    }

    public class ClientTask
    {
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public TaskCategory Category { get; set; }
        public TaskPriority Priority { get; set; }
        public List<string> SpecialRequirements { get; set; } = new List<string>();
        public TimeSpan? EstimatedDuration { get; set; }
    }

    public enum ContractStatus
    {
        // NEW: Caregiver-initiated flow statuses
        Draft,                    // Caregiver is preparing contract
        PendingClientApproval,    // Contract sent to client for approval
        ClientReviewRequested,    // Client requested changes (Round 1)
        Revised,                  // Caregiver revised and resubmitted (Round 2)
        Approved,                 // Client approved the contract - now active
        ClientRejected,           // Client rejected after Round 2 (requests new caregiver)

        // LEGACY: Keeping for backward compatibility with existing contracts
        Generated,      // Contract created after payment (old flow)
        Sent,          // Sent to caregiver (old flow)
        Pending,       // Awaiting caregiver response (old flow)
        Accepted,      // Caregiver accepted (old flow)
        Rejected,      // Caregiver rejected (old flow)
        ReviewRequested, // Caregiver requested review (old flow)
        UnderReview,   // Client is reviewing caregiver's requests (old flow)

        // Lifecycle statuses (used by both flows)
        Expired,       // Contract expired without response
        Completed,     // Service completed
        Terminated     // Contract terminated early
    }

    public enum TaskCategory
    {
        PersonalCare,
        MedicalCare,
        Companionship,
        HouseholdTasks,
        Mobility,
        Medication,
        Meals,
        Transportation,
        Other
    }

    public enum TaskPriority
    {
        Low,
        Medium,
        High,
        Critical
    }
}