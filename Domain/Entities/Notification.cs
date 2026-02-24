using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Entities
{
    public class Notification
    {
        public ObjectId Id { get; set; }
        public string RecipientId { get; set; } = string.Empty; // User receiving the notification
        public string? SenderId { get; set; } // User who triggered the notification (optional)
        public string Type { get; set; } = string.Empty; // Message, Payment, etc.
        public string Content { get; set; } = string.Empty; // Notification text
        public string? Title { get; set; } // Notification title        
        public bool IsRead { get; set; } = false;
        public string RelatedEntityId { get; set; } = string.Empty; // ID of message/payment/gig
        public string? OrderId { get; set; } // Associated order ID for contract notifications
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public enum NotificationType
    {
        Message,
        Payment,
        SystemNotice,
        NewGig,
        WithdrawalRequest,
        WithdrawalVerified,
        WithdrawalCompleted,
        WithdrawalRejected,
        ContractSent,
        ContractApproved,
        ContractRejected,
        ContractRevisionRequested,
        OrderCompleted,
        OrderDisputed,
        NewReview,
        BookingConfirmed
    }
}
