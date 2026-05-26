using Application.DTOs;

namespace Application.Interfaces.Content
{
    /// <summary>
    /// Validates a gig image through two layers:
    ///   Layer 1 — Technical rules (file type, size, dimensions). Free, synchronous.
    ///   Layer 2 — AI content check via OpenAI gpt-4o Vision. Checks relevance and safety
    ///             in context of the gig title so personal/professional caregiver photos
    ///             are handled correctly.
    /// </summary>
    public interface IGigImageModerationService
    {
        /// <summary>
        /// Validates the image bytes against both layers.
        /// </summary>
        /// <param name="imageBytes">Raw bytes of the uploaded file.</param>
        /// <param name="fileName">Original file name (used for extension hint only).</param>
        /// <param name="gigTitle">
        ///     The title of the gig being created or updated.
        ///     Passed to the AI prompt so a personal caregiver photo can be assessed
        ///     in context (e.g. a selfie may be appropriate for "Elderly Companion Care").
        /// </param>
        Task<GigImageModerationResult> ValidateAsync(byte[] imageBytes, string fileName, string gigTitle);
    }
}
