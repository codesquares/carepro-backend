using MongoDB.Bson;
using System;
using System.Collections.Generic;

namespace Domain.Entities
{
    public class EmailNotificationLog
    {
        public ObjectId Id { get; set; }
        public required string UserId { get; set; } // Recipient of the email
        public string? NotificationId { get; set; } // For individual notifications (null for batch)
        public required string NotificationType { get; set; } // "NewGig", "Message", "Contract", "System", etc.
        public EmailType EmailType { get; set; } // Initial, Reminder1, Reminder2, Final, Batch
        public DateTime SentAt { get; set; } = DateTime.UtcNow;
        public required string EmailSubject { get; set; }
        public List<string> NotificationIds { get; set; } = new List<string>(); // For batched notifications
        public EmailStatus Status { get; set; } = EmailStatus.Sent;
        public string? RelatedEntityId { get; set; } // Contract ID, Gig ID, etc.
        public string? ErrorMessage { get; set; } // If failed
        public int RetryCount { get; set; } = 0;
    }

    public enum EmailType
    {
        Initial,        // First email for the notification
        Reminder1,      // 24 hour reminder
        Reminder2,      // 72 hour reminder
        Final,          // 7 day final reminder
        Batch,          // Batched notifications (like daily message digest)
        Immediate       // Send once notifications (NewGig, System updates)
    }

    public enum EmailStatus
    {
        Scheduled,      // Queued to be sent
        Sent,           // Successfully sent
        Failed,         // Failed to send
        Skipped         // Skipped due to user preferences
    }
}