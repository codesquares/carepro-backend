using MongoDB.Bson;

namespace Domain.Entities
{
    public class Referrer
    {
        public ObjectId Id { get; set; } = ObjectId.GenerateNewId();
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? PhoneNo { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
