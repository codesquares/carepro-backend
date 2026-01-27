using Domain.Entities;

namespace Application.DTOs
{
    // ========================================
    // NEW FLOW: Caregiver-Initiated Contract DTOs
    // ========================================

    /// <summary>
    /// DTO for caregiver to generate a contract after agreeing with client on schedule.
    /// Caregiver submits the agreed schedule and service details.
    /// </summary>
    public class CaregiverContractGenerationDTO
    {
        public string OrderId { get; set; } = string.Empty;
        
        // Agreed schedule (must match VisitsPerWeek from order)
        public List<ScheduledVisitDTO> Schedule { get; set; } = new List<ScheduledVisitDTO>();
        
        // Service details from client-caregiver discussion
        public string ServiceAddress { get; set; } = string.Empty;
        public string? SpecialClientRequirements { get; set; }
        public string? AccessInstructions { get; set; }
        public string? AdditionalNotes { get; set; }
    }

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
    /// DTO for client to request review/changes (only allowed in Round 1)
    /// </summary>
    public class ClientContractReviewRequestDTO
    {
        public string Comments { get; set; } = string.Empty;
        public string? PreferredScheduleNotes { get; set; } // Optional: suggest alternative times
    }

    /// <summary>
    /// DTO for caregiver to revise contract after client review request (Round 2)
    /// </summary>
    public class CaregiverContractRevisionDTO
    {
        public string ContractId { get; set; } = string.Empty;
        
        // Revised schedule
        public List<ScheduledVisitDTO> RevisedSchedule { get; set; } = new List<ScheduledVisitDTO>();
        
        // Updated service details (can be modified)
        public string? ServiceAddress { get; set; }
        public string? SpecialClientRequirements { get; set; }
        public string? AccessInstructions { get; set; }
        public string? AdditionalNotes { get; set; }
        
        // Explanation of changes
        public string RevisionNotes { get; set; } = string.Empty;
    }

    /// <summary>
    /// DTO for client rejection (only allowed in Round 2)
    /// </summary>
    public class ClientContractRejectionDTO
    {
        public string? Reason { get; set; }
    }

    /// <summary>
    /// Contract negotiation history entry for audit/safety
    /// </summary>
    public class ContractNegotiationHistoryDTO
    {
        public string Id { get; set; } = string.Empty;
        public string ContractId { get; set; } = string.Empty;
        public string OrderId { get; set; } = string.Empty;
        public string ActorId { get; set; } = string.Empty;
        public string ActorType { get; set; } = string.Empty; // "Client", "Caregiver", "System"
        public string Action { get; set; } = string.Empty;
        public int Round { get; set; }
        public List<ScheduledVisitDTO> ScheduleSnapshot { get; set; } = new List<ScheduledVisitDTO>();
        public string? ServiceAddressSnapshot { get; set; }
        public string? SpecialRequirementsSnapshot { get; set; }
        public string? Comments { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    // ========================================
    // LEGACY: Original Contract DTOs (kept for backward compatibility)
    // ========================================

    // Request DTOs
    public class ContractGenerationRequestDTO
    {
        public string? GigId { get; set; }
        public string? ClientId { get; set; }
        public string? CaregiverId { get; set; }
        public string? PaymentTransactionId { get; set; }
        public PackageSelectionDTO? SelectedPackage { get; set; }
        public List<ClientTaskDTO> Tasks { get; set; } = new List<ClientTaskDTO>();
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

    public class CaregiverContractResponseDTO
    {
        public string ContractId { get; set; }
        public string CaregiverId { get; set; }
        public string Response { get; set; } // "accept", "reject", "review"
        public List<string> Comments { get; set; } = new List<string>();
        public List<string> RequestedChanges { get; set; } = new List<string>();
    }

    // New DTOs for specific caregiver actions
    public class ContractRejectRequestDTO
    {
        public string? Reason { get; set; }
    }

    public class ContractReviewRequestDTO
    {
        public string? Comments { get; set; }
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
        public string GeneratedTerms { get; set; }
        public decimal TotalAmount { get; set; }
        public string Status { get; set; }
        public string? PaymentTransactionId { get; set; }
        
        // NEW: Caregiver-initiated fields
        public string? SubmittedByCaregiverId { get; set; }
        public DateTime? SubmittedAt { get; set; }
        public List<ScheduledVisitDTO> Schedule { get; set; } = new List<ScheduledVisitDTO>();
        public string? ServiceAddress { get; set; }
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

    public class CaregiverContractNotificationDTO
    {
        public string ContractId { get; set; }
        public string ClientName { get; set; }
        public string GigTitle { get; set; }
        public PackageSelectionDTO Package { get; set; }
        public List<ClientTaskDTO> Tasks { get; set; }
        public decimal TotalAmount { get; set; }
        public DateTime ContractStartDate { get; set; }
        public DateTime ExpiryDate { get; set; }
        public string ContractTerms { get; set; }
    }

    /// <summary>
    /// Client contract notification for the new flow (client receives contract to approve)
    /// </summary>
    public class ClientContractNotificationDTO
    {
        public string ContractId { get; set; } = string.Empty;
        public string CaregiverName { get; set; } = string.Empty;
        public string GigTitle { get; set; } = string.Empty;
        public PackageSelectionDTO Package { get; set; } = new PackageSelectionDTO();
        public List<ClientTaskDTO> Tasks { get; set; } = new List<ClientTaskDTO>();
        public List<ScheduledVisitDTO> Schedule { get; set; } = new List<ScheduledVisitDTO>();
        public string? ServiceAddress { get; set; }
        public string? SpecialClientRequirements { get; set; }
        public decimal TotalAmount { get; set; }
        public DateTime ContractStartDate { get; set; }
        public DateTime ContractEndDate { get; set; }
        public string ContractTerms { get; set; } = string.Empty;
        public int NegotiationRound { get; set; }
    }

    public class AlternativeCaregiverDTO
    {
        public string CaregiverId { get; set; }
        public string Name { get; set; }
        public string ProfilePicture { get; set; }
        public double ProximityScore { get; set; }
        public decimal Rating { get; set; }
        public int TotalReviews { get; set; }
        public string Location { get; set; }
        public decimal Distance { get; set; }
        public List<string> Specializations { get; set; }
        public PackagePricingDTO Pricing { get; set; }
    }

    public class PackagePricingDTO
    {
        public decimal OneVisitPerWeek { get; set; }
        public decimal ThreeVisitsPerWeek { get; set; }
        public decimal FiveVisitsPerWeek { get; set; }
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

    public class ContractRevisionRequestDTO
    {
        public string ContractId { get; set; }
        public List<ClientTaskDTO> UpdatedTasks { get; set; }
        public string RevisionNotes { get; set; }
        public PackageSelectionDTO UpdatedPackage { get; set; }
    }

    // Webhook Integration DTOs
    public class ContractPaymentWebhookDTO
    {
        public string Status { get; set; }
        public string TransactionId { get; set; }
        public string GigId { get; set; }
        public string ClientId { get; set; }
        public string CaregiverId { get; set; }
        public PackageSelectionDTO SelectedPackage { get; set; }
        public List<ClientTaskDTO> Tasks { get; set; } = new List<ClientTaskDTO>();
        public decimal Amount { get; set; }
    }
}