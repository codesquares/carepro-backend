using MongoDB.Bson.Serialization.Attributes;
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
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public ObjectId MessageId { get; set; }

        [BsonElement("senderId")]
        public string SenderId { get; set; }

        [BsonElement("receiverId")]
        public string ReceiverId { get; set; }

        [BsonElement("message")]
        public string Message { get; set; }
       // public string? MessageId { get; set; }
        public string? Status { get; set; }

        [BsonElement("timestamp")]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        
        [BsonElement("isDeleted")]
        public bool IsDeleted { get; set; } = false;
        
        [BsonElement("deletedAt")]
        public DateTime? DeletedAt { get; set; }
        
        [BsonElement("isRead")]
        public bool IsRead { get; set; } = false;
        
        [BsonElement("readAt")]
        public DateTime? ReadAt { get; set; }
        
        [BsonElement("isDelivered")]
        public bool IsDelivered { get; set; } = false;
        
        [BsonElement("deliveredAt")]
        public DateTime? DeliveredAt { get; set; }
    }

}
