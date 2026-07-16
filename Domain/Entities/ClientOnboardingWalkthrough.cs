using MongoDB.Bson;

namespace Domain.Entities
{
    public class ClientOnboardingWalkthrough
    {
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public string ClientId { get; set; } = string.Empty;
        public string ContentVersion { get; set; } = string.Empty;
        public string Status { get; set; } = WalkthroughStatuses.NotStarted;
        public string? CurrentStep { get; set; }
        public List<ClientOnboardingStepState> StepStates { get; set; } = new();
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public DateTime? DismissedAt { get; set; }
        public long Version { get; set; }
        public List<ClientOnboardingTransitionRecord> TransitionHistory { get; set; } = new();
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class ClientOnboardingStepState
    {
        public string StepKey { get; set; } = string.Empty;
        public string State { get; set; } = StepStates.Pending;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class ClientOnboardingTransitionRecord
    {
        public string Action { get; set; } = string.Empty;
        public string? StepKey { get; set; }
        public string? Reason { get; set; }
        public string? MetadataJson { get; set; }
        public long PreviousVersion { get; set; }
        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    }

    public static class WalkthroughStatuses
    {
        public const string NotStarted = "not_started";
        public const string InProgress = "in_progress";
        public const string Paused = "paused";
        public const string Completed = "completed";
        public const string Dismissed = "dismissed";
    }

    public static class StepStates
    {
        public const string Pending = "pending";
        public const string Current = "current";
        public const string Completed = "completed";
        public const string Skipped = "skipped";
    }
}