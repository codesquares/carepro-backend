namespace Application.DTOs
{
    /// <summary>
    /// The result returned by the gig image moderation pipeline.
    /// Both Layer 1 (technical validation) and Layer 2 (AI content check) produce this.
    /// </summary>
    public class GigImageModerationResult
    {
        public bool IsApproved { get; init; }
        public string? RejectionReason { get; init; }
        public List<string> Suggestions { get; init; } = new();

        public static GigImageModerationResult Approved() =>
            new() { IsApproved = true };

        public static GigImageModerationResult Rejected(string reason, List<string>? suggestions = null) =>
            new()
            {
                IsApproved = false,
                RejectionReason = reason,
                Suggestions = suggestions ?? new List<string>()
            };
    }

    /// <summary>
    /// The structured error body returned to the client on HTTP 422 when an image is rejected.
    /// </summary>
    public class GigImageRejectionResponse
    {
        public string Error { get; init; } = "image_rejected";
        public string? Reason { get; init; }
        public List<string> Suggestions { get; init; } = new();
    }

    /// <summary>
    /// Internal settings bound from appsettings under "GigImageValidation".
    /// </summary>
    public class GigImageValidationSettings
    {
        public long MaxFileSizeBytes { get; set; } = 5_242_880; // 5 MB default
        public int MinWidthPx { get; set; } = 300;
        public int MinHeightPx { get; set; } = 300;
        public int MaxWidthPx { get; set; } = 5000;
        public int MaxHeightPx { get; set; } = 5000;
    }
}
