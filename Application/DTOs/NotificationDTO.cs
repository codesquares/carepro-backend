using Domain.Entities;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.DTOs
{
    public class NotificationDTO
    {
        public string Id { get; set; }
        public string UserId { get; set; }
        public string Type { get; set; }
        public string Content { get; set; }
        public string Title { get; set; }
        public bool IsRead { get; set; }
        public string? RelatedEntityId { get; set; }
      //  public string? ReferenceType { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class CreateNotificationRequest
    {
        public string UserId { get; set; }
        public string Title { get; set; }
        public string Message { get; set; }
        public string Type { get; set; }
        public string? ReferenceId { get; set; }
        public string? ReferenceType { get; set; }
    }

    public class AddNotificationRequest
    {
      //  public ObjectId Id { get; set; }
        public string RecipientId { get; set; } // User receiving the notification
        public string SenderId { get; set; } // User who triggered the notification (optional)
        public string Type { get; set; } // Message, Payment, etc.
        public string Content { get; set; } // Notification text
        public string? Title { get; set; } // Notification title
       // public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
       // public bool IsRead { get; set; } = false;
        public string RelatedEntityId { get; set; } // ID of message/payment/gig



        
    }

    public class NotificationResponse
    {     
        public string Id { get; set; }
        public string UserId { get; set; }
        public string UserFullName { get; set; }
        public string Type { get; set; }
        public string Content { get; set; }
        public string Title { get; set; }
        public bool IsRead { get; set; }
        public string? RelatedEntityId { get; set; }
        public string? RelatedEntityType { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class NotificationTypes
    {
        public const string WithdrawalRequest = "WithdrawalRequest";
        public const string SystemAlert = "SystemAlert";
        public const string OrderNotification = "OrderNotification";
        public const string MessageNotification = "MessageNotification";
    }
}
