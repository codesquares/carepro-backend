using MongoDB.Bson;

namespace Domain.Entities
{
    public class ClientOnboardingTipSeen
    {
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public string ClientId { get; set; } = string.Empty;
        public string TipKey { get; set; } = string.Empty;
        public List<ClientOnboardingContextEntry> ContextEntries { get; set; } = new();
        public string? DisplayVariant { get; set; }
        public DateTime FirstSeenAt { get; set; } = DateTime.UtcNow;
        public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class ClientOnboardingContextEntry
    {
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }
}