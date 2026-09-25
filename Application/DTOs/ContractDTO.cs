using Domain.Entities;

namespace Application.DTOs
{
    /// <summary>
    /// Scheduled visit details - each visit must be 4-6 hours
    /// </summary>
    public class ScheduledVisitDTO
    {
        public string DayOfWeek { get; set; } = string.Empty; // "Monday", "Tuesday", etc.
        public string StartTime { get; set; } = string.Empty; // "09:00"
        public string EndTime { get; set; } = string.Empty;   // "14:00" (4-6 hours from start)
    }

    /// <summary>
    /// Enriched contract data for LLM generation - includes all real data instead of placeholders
    /// </summary>
    public class ContractGenerationDataDTO
    {
        // Party Information
        public string ClientId { get; set; } = string.Empty;
        public string ClientFullName { get; set; } = string.Empty;
        public string? ClientEmail { get; set; }
        public string? ClientPhone { get; set; }
        
        public string CaregiverId { get; set; } = string.Empty;
        public string CaregiverFullName { get; set; } = string.Empty;
        public string? CaregiverEmail { get; set; }
        public string? CaregiverPhone { get; set; }
        public string? CaregiverQualifications { get; set; }
        
        // Service Details
        public string GigTitle { get; set; } = string.Empty;
        public string? GigDescription { get; set; }
        public string? GigCategory { get; set; }
        
        // Package & Pricing (already paid)
        public PackageSelection Package { get; set; } = new PackageSelection();
        public decimal TotalAmountPaid { get; set; }
        public string? TransactionReference { get; set; }
        
        // Schedule
        public List<ScheduledVisit> Schedule { get; set; } = new List<ScheduledVisit>();
        
        // Location & Requirements
        public string ServiceAddress { get; set; } = string.Empty;
        public string? City { get; set; }
        public string? State { get; set; }
        public string? SpecialClientRequirements { get; set; }
        public string? AccessInstructions { get; set; }
        public string? CaregiverNotes { get; set; }
        
        // Care Tasks
        public List<ClientTask> Tasks { get; set; } = new List<ClientTask>();
        
        // Contract Period
        public DateTime ContractStartDate { get; set; }
        public DateTime ContractEndDate { get; set; }
        
        // Generated identifiers
        public string ContractId { get; set; } = string.Empty;
        public string OrderId { get; set; } = string.Empty;
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Response DTO for a proposed task on a contract
    /// </summary>
    public class ContractProposedTaskDTO
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Priority { get; set; } = string.Empty;
        public string ProposedBy { get; set; } = string.Empty;
        public string ProposedByRole { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? ResponseNote { get; set; }
        public DateTime ProposedAt { get; set; }
        public DateTime? RespondedAt { get; set; }
    }

    public class PackageSelectionDTO
    {
        public string? PackageType { get; set; }
        public int VisitsPerWeek { get; set; }
        public decimal PricePerVisit { get; set; }
        public decimal TotalWeeklyPrice { get; set; }
        public int DurationWeeks { get; set; }
    }

    public class ClientTaskDTO
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public string Category { get; set; }
        public string? Priority { get; set; }
        public List<string> SpecialRequirements { get; set; } = new List<string>();
        public int? EstimatedDurationMinutes { get; set; }
    }

    // Response DTOs
    public class ContractDTO
    {
        public string Id { get; set; }
        public string? OrderId { get; set; }
        public string GigId { get; set; }
        public GigSummaryDTO GigDetails { get; set; }
        public string ClientId { get; set; }
        public string CaregiverId { get; set; }
        public PackageSelectionDTO SelectedPackage { get; set; }
        public List<ClientTaskDTO> Tasks { get; set; }
        public List<ContractProposedTaskDTO> ProposedTasks { get; set; } = new List<ContractProposedTaskDTO>();
        public string GeneratedTerms { get; set; }
        public decimal TotalAmount { get; set; }
        public string Status { get; set; }
        public string? PaymentTransactionId { get; set; }
        
        // NEW: Caregiver-initiated fields
        public string? SubmittedByCaregiverId { get; set; }
        public string? SubmittedByClientId { get; set; }
        public string? InitiatedByRole { get; set; } = "Caregiver";
        public DateTime? SubmittedAt { get; set; }
        public List<ScheduledVisitDTO> Schedule { get; set; } = new List<ScheduledVisitDTO>();
        public string? ServiceAddress { get; set; }
        public bool? ServiceLocationSetByClient { get; set; }
        public DateTime? ServiceLocationSetAt { get; set; }
        public string? SpecialClientRequirements { get; set; }
        public string? AccessInstructions { get; set; }
        public string? CaregiverAdditionalNotes { get; set; }
        public DateTime? ClientApprovedAt { get; set; }
        public string? ClientApprovedBy { get; set; }
        public int NegotiationRound { get; set; }
        public DateTime? ClientReviewRequestedAt { get; set; }
        public string? ClientReviewComments { get; set; }
        
        // LEGACY: Caregiver response fields
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
        public string CaregiverResponse { get; set; }
        public List<string> Comments { get; set; } = new List<string>();
        public DateTime ContractStartDate { get; set; }
        public DateTime ContractEndDate { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class GigSummaryDTO
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string CaregiverName { get; set; }
        public string Location { get; set; }
    }

    public class ContractHistoryDTO
    {
        public List<ContractDTO> ActiveContracts { get; set; }
        public List<ContractDTO> CompletedContracts { get; set; }
        public List<ContractDTO> PendingContracts { get; set; }
        public ContractStatsDTO Stats { get; set; }
    }

    public class ContractStatsDTO
    {
        public int TotalContracts { get; set; }
        public int AcceptedContracts { get; set; }
        public int RejectedContracts { get; set; }
        public double AcceptanceRate { get; set; }
        public decimal TotalEarnings { get; set; }
        public double AverageRating { get; set; }
    }

    public class ContractAnalyticsDTO
    {
        public ContractStatsDTO Stats { get; set; }
        public List<ContractDTO> RecentContracts { get; set; }
        public decimal MonthlyEarnings { get; set; }
        public int ContractsThisMonth { get; set; }
        public List<ContractTrendDTO> MonthlyTrends { get; set; }
    }

    public class ContractTrendDTO
    {
        public string Month { get; set; }
        public int ContractCount { get; set; }
        public decimal Earnings { get; set; }
    }

    /// <summary>
    /// Request body for the dedicated client GPS capture endpoint.
    /// The client confirms they are physically at the service address
    /// and the frontend sends the device GPS fix here.
    /// </summary>
    public class SetServiceLocationRequest
    {
        /// <summary>Device GPS latitude.</summary>
        public double Latitude { get; set; }

        /// <summary>Device GPS longitude.</summary>
        public double Longitude { get; set; }

        /// <summary>
        /// Horizontal accuracy in metres reported by the device.
        /// Requests with accuracy worse than the configured threshold (default 50m) are rejected.
        /// </summary>
        public double Accuracy { get; set; }
    }

    public class SetServiceLocationResponse
    {
        public bool Success { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double Accuracy { get; set; }
        public DateTime SetAt { get; set; }
    }
}