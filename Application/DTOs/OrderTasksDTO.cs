using System;
using System.Collections.Generic;

namespace Application.DTOs
{
    // Request DTOs
    public class CreateOrderTasksRequestDTO
    {
        public string? ClientId { get; set; }
        public string? GigId { get; set; }
        public string? CaregiverId { get; set; }
        public PackageSelectionDTO? PackageSelection { get; set; }
        public List<CareTaskDTO> CareTasks { get; set; } = new List<CareTaskDTO>();
        public string? SpecialInstructions { get; set; }
        public List<string> PreferredTimes { get; set; } = new List<string>();
        public List<string> EmergencyContacts { get; set; } = new List<string>();
    }

    public class UpdateOrderTasksRequestDTO
    {
        public string? OrderTasksId { get; set; }
        public PackageSelectionDTO? PackageSelection { get; set; }
        public List<CareTaskDTO> CareTasks { get; set; } = new List<CareTaskDTO>();
        public string? SpecialInstructions { get; set; }
        public List<string> PreferredTimes { get; set; } = new List<string>();
        public List<string> EmergencyContacts { get; set; } = new List<string>();
    }

    public class CareTaskDTO
    {
        public string? Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = "Other"; // TaskCategory enum as string
        public string Priority { get; set; } = "Medium"; // TaskPriority enum as string
        public List<string> SpecialRequirements { get; set; } = new List<string>();
        public int? EstimatedDurationMinutes { get; set; }
        public bool IsRecurring { get; set; } = true;
        public string? Frequency { get; set; }
    }

    // Response DTOs
    public class OrderTasksResponseDTO
    {
        public string Id { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string GigId { get; set; } = string.Empty;
        public string CaregiverId { get; set; } = string.Empty;
        public PackageSelectionDTO PackageSelection { get; set; } = new PackageSelectionDTO();
        public List<CareTaskDTO> CareTasks { get; set; } = new List<CareTaskDTO>();
        public string? SpecialInstructions { get; set; }
        public List<string> PreferredTimes { get; set; } = new List<string>();
        public List<string> EmergencyContacts { get; set; } = new List<string>();
        public decimal TotalAmount { get; set; }
        public decimal EstimatedCostPerVisit { get; set; }
        public decimal EstimatedWeeklyCost { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string? ClientOrderId { get; set; }
        
        // Additional context data
        public GigSummaryDTO? GigDetails { get; set; }
        public CaregiverSummaryDTO? CaregiverDetails { get; set; }
    }

    public class CaregiverSummaryDTO
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? ProfilePicture { get; set; }
        public decimal Rating { get; set; }
        public int TotalReviews { get; set; }
        public string? Location { get; set; }
        public List<string> Specializations { get; set; } = new List<string>();
    }

    public class OrderTasksPricingDTO
    {
        public decimal BasePrice { get; set; }
        public decimal TaskComplexityMultiplier { get; set; }
        public decimal FrequencyDiscount { get; set; }
        public decimal EstimatedCostPerVisit { get; set; }
        public decimal EstimatedWeeklyCost { get; set; }
        public decimal TotalAmount { get; set; }
        public string PricingBreakdown { get; set; } = string.Empty;
    }

    public class FinalizeOrderTasksRequestDTO
    {
        public string? OrderTasksId { get; set; }
        public string? PaymentTransactionId { get; set; }
    }
}