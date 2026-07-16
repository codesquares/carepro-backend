namespace Domain.Settings
{
    public class ClientOnboardingSettings
    {
        public string CurrentContentVersion { get; set; } = "2026-07-15";
        public List<string> SupportedContentVersions { get; set; } = new() { "2026-07-15" };
    }
}