using MongoDB.Bson;
using System;
using System.Collections.Generic;

namespace Domain.Entities
{
    public class OrderTasks
    {
        public ObjectId Id { get; set; } = ObjectId.GenerateNewId();
        public string ClientId { get; set; } = string.Empty;
        public string GigId { get; set; } = string.Empty;
        public string CaregiverId { get; set; } = string.Empty;

        // Package Selection
        public PackageSelection PackageSelection { get; set; } = new PackageSelection();

        // Detailed Care Tasks
        public List<CareTask> CareTasks { get; set; } = new List<CareTask>();

        // Additional Requirements
        public string? SpecialInstructions { get; set; }
        public List<string> PreferredTimes { get; set; } = new List<string>();
        public List<string> EmergencyContacts { get; set; } = new List<string>();

        // Pricing
        public decimal TotalAmount { get; set; }
        public decimal EstimatedCostPerVisit { get; set; }
        public decimal EstimatedWeeklyCost { get; set; }

        // Status Tracking
        public OrderTasksStatus Status { get; set; } = OrderTasksStatus.Draft;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }
        public DateTime? PaidAt { get; set; }

        // Order Reference (after payment)
        public string? ClientOrderId { get; set; }
    }

    public class CareTask
    {
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public TaskCategory Category { get; set; } = TaskCategory.Other;
        public TaskPriority Priority { get; set; } = TaskPriority.Medium;
        public List<string> SpecialRequirements { get; set; } = new List<string>();
        public TimeSpan? EstimatedDuration { get; set; }
        public bool IsRecurring { get; set; } = true;
        public string? Frequency { get; set; } // "daily", "weekly", "as needed"
    }

    public enum OrderTasksStatus
    {
        Draft,              // Being created by client
        PendingPayment,     // Ready for payment
        Paid,              // Payment completed, order created
        ContractGenerated, // Contract created and sent
        Completed,         // Process fully completed
        Cancelled,         // Cancelled before payment
        Expired           // Expired without payment
    }
}