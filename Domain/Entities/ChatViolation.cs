using MongoDB.Bson;

namespace Domain.Entities
{
    public class ChatViolation
    {
        public ObjectId Id { get; set; }
        public required string UserId { get; set; }
        public required string RecipientId { get; set; }
        public required string OriginalMessage { get; set; }
        public List<string> DetectedPatterns { get; set; } = new();
        public required string ViolationType { get; set; }
        public required string Action { get; set; } // "Redacted", "Blocked", "Warned"
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
