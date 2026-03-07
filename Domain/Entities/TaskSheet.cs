using MongoDB.Bson;
using System;
using System.Collections.Generic;

namespace Domain.Entities
{
    public class TaskSheet
    {
        public ObjectId Id { get; set; } = ObjectId.GenerateNewId();
        public string OrderId { get; set; } = string.Empty;
        public string CaregiverId { get; set; } = string.Empty;
        public int SheetNumber { get; set; }
        public int BillingCycleNumber { get; set; } = 1;
        public List<TaskSheetItem> Tasks { get; set; } = new List<TaskSheetItem>();
        public string Status { get; set; } = "in-progress";
        public DateTime? SubmittedAt { get; set; }
        public string? ClientSignatureUrl { get; set; }
        public DateTime? ClientSignatureSignedAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class TaskSheetItem
    {
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public string Text { get; set; } = string.Empty;
        public bool Completed { get; set; } = false;
        public bool AddedByCaregiver { get; set; } = false;
    }
}
