using MongoDB.Bson;

namespace Domain.Entities
{
    public class Contract
    {
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

        // Gig and Payment Integration
        public string GigId { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string CaregiverId { get; set; } = string.Empty;
        public string? PaymentTransactionId { get; set; }
        
        // Package and Task Details
        public PackageSelection SelectedPackage { get; set; } = new PackageSelection();
        public List<ClientTask> Tasks { get; set; } = new List<ClientTask>();
        
        // Contract Terms
        public string GeneratedTerms { get; set; } = string.Empty;
        public decimal TotalAmount { get; set; }
        public ContractStatus Status { get; set; }
        
        // Response Management
        public DateTime? SentAt { get; set; }
        public DateTime? RespondedAt { get; set; }
        public string CaregiverResponse { get; set; } = string.Empty;
        public List<string> ReviewComments { get; set; } = new List<string>();
        
        // Contract History
        public DateTime ContractStartDate { get; set; }
        public DateTime ContractEndDate { get; set; }
        public bool IsCompleted { get; set; }
        public decimal? FinalRating { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
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
        Generated,      // Contract created after payment
        Sent,          // Sent to caregiver
        Pending,       // Awaiting caregiver response
        Accepted,      // Caregiver accepted
        Rejected,      // Caregiver rejected
        ReviewRequested, // Caregiver requested review
        Revised,       // Client made revisions
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