using MongoDB.Bson;
using System;
using System.Collections.Generic;

namespace Domain.Entities
{
    public class WebhookLog
    {
        public ObjectId Id { get; set; }
        
        public string UserId { get; set; } = string.Empty;
        
        public string RawPayload { get; set; } = string.Empty;
        
        public string WebhookType { get; set; } = string.Empty; // "verification", "email", "phone", etc.
        
        public DateTime ReceivedAt { get; set; }
        
        public DateTime? ProcessedAt { get; set; }
        
        public string Status { get; set; } = "received"; // "received", "processed", "failed"
        
        public string? VerificationId { get; set; }
        
        public string ClientIp { get; set; } = string.Empty;
        
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();
        
        public string Signature { get; set; } = string.Empty;
        
        public string? ProcessingNotes { get; set; }
    }
}
