using MongoDB.Bson;

namespace Domain.Entities
{
    public class AnalyticsEvent
    {
        public ObjectId Id { get; set; } = ObjectId.GenerateNewId();
        public string EventType { get; set; } = string.Empty;
        public string Page { get; set; } = string.Empty;
        public string? Fbclid { get; set; }
        public string? UserAgent { get; set; }
        public string? IpAddress { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
