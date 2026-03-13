namespace Application.DTOs
{
    public class DetectedPattern
    {
        public required string Category { get; set; } // "PhoneNumber", "Email", "ExternalLink", "ContactKeyword"
        public required string MatchedText { get; set; }
    }

    public class ContactDetectionResult
    {
        public bool HasViolation { get; set; }
        public List<DetectedPattern> Patterns { get; set; } = new();
        public string RedactedMessage { get; set; } = string.Empty;
    }

    public class ComplianceResult
    {
        public bool Allowed { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? Warning { get; set; }
        public bool ViolationLogged { get; set; }
    }

    public class ChatViolationDTO
    {
        public string Id { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string RecipientId { get; set; } = string.Empty;
        public string OriginalMessage { get; set; } = string.Empty;
        public List<string> DetectedPatterns { get; set; } = new();
        public string ViolationType { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}
