using System.Text.Json.Serialization;

namespace Application.DTOs
{
    public class ClientOnboardingStateResponse
    {
        public ClientOnboardingWalkthroughStateDto Walkthrough { get; set; } = new();
        public List<ClientOnboardingSeenTipDto> SeenTips { get; set; } = new();
    }

    public class ClientOnboardingWalkthroughStateDto
    {
        public string ContentVersion { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? CurrentStep { get; set; }
        public List<ClientOnboardingStepStateDto> StepStates { get; set; } = new();
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public DateTime? DismissedAt { get; set; }
        public long Version { get; set; }
    }

    public class ClientOnboardingStepStateDto
    {
        public string StepKey { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public DateTime UpdatedAt { get; set; }
    }

    public class ClientOnboardingSeenTipDto
    {
        public string TipKey { get; set; } = string.Empty;
        public DateTime SeenAt { get; set; }
        public Dictionary<string, string> Context { get; set; } = new();
        public string? DisplayVariant { get; set; }
        public DateTime? FirstSeenAt { get; set; }
        public DateTime? LastSeenAt { get; set; }
    }

    public class PatchClientOnboardingWalkthroughRequest
    {
        public string ContentVersion { get; set; } = string.Empty;
        public long Version { get; set; }
        public string Action { get; set; } = string.Empty;
        public string? StepKey { get; set; }
        public string? Reason { get; set; }
        public ClientOnboardingMetadataDto? Metadata { get; set; }
    }

    public class ClientOnboardingMetadataDto
    {
        [JsonPropertyName("eligibleGigCount")]
        public int? EligibleGigCount { get; set; }

        [JsonPropertyName("gateEnabledLive")]
        public bool? GateEnabledLive { get; set; }

        [JsonPropertyName("route")]
        public string? Route { get; set; }
    }

    public class ClientOnboardingConflictResponse
    {
        public string ErrorCode { get; set; } = "VERSION_CONFLICT";
        public string Message { get; set; } = "Submitted version is stale.";
        public ClientOnboardingStateResponse LatestState { get; set; } = new();
    }

    public class MarkClientOnboardingTipSeenRequest
    {
        public string TipKey { get; set; } = string.Empty;
        public Dictionary<string, string> Context { get; set; } = new();
        public string? DisplayVariant { get; set; }
    }

    public class ClientOnboardingTipSeenResponse
    {
        public string TipKey { get; set; } = string.Empty;
        public Dictionary<string, string> Context { get; set; } = new();
        public string? DisplayVariant { get; set; }
        public DateTime FirstSeenAt { get; set; }
        public DateTime LastSeenAt { get; set; }
    }

    public class ClientOnboardingPatchResult
    {
        public bool IsConflict { get; set; }
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public ClientOnboardingStateResponse State { get; set; } = new();
    }
}