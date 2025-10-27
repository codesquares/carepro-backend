using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Entities
{
    public class ChatMessage
    {
        public ObjectId MessageId { get; set; }
        public required string SenderId { get; set; }
        public required string ReceiverId { get; set; }
        public required string Message { get; set; }
        public string? Status { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public bool IsDeleted { get; set; } = false;
        public DateTime? DeletedAt { get; set; }
        public bool IsRead { get; set; } = false;
        public DateTime? ReadAt { get; set; }
        public bool IsDelivered { get; set; } = false;
        public DateTime? DeliveredAt { get; set; }
    }

}
