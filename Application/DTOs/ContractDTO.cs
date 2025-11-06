namespace Application.DTOs
{
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