using System.Text.RegularExpressions;
using System.Web;
using Application.Interfaces.Common;

namespace Infrastructure.Services.Common
{
    /// <summary>
    /// Content sanitizer implementation to prevent XSS attacks in user-generated content.
    /// </summary>
    public class ContentSanitizer : IContentSanitizer
    {
        // Dangerous patterns that could indicate XSS attempts
        private static readonly Regex ScriptPattern = new(
            @"<script[^>]*>.*?</script>|javascript:|on\w+\s*=|<iframe|<object|<embed|<form|<input|<button",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex DataUriPattern = new(
            @"data:\s*[^,]*;base64",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Sanitizes text content by HTML encoding.
        /// Preserves readability while preventing HTML/script injection.
        /// </summary>
        public string SanitizeText(string? input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            // HTML encode to prevent script execution
            var sanitized = HttpUtility.HtmlEncode(input);

            return sanitized;
        }

        /// <summary>
        /// Strict sanitization that also removes potentially dangerous patterns.
        /// Use for content that will be rendered in sensitive contexts.
        /// </summary>
        public string SanitizeStrict(string? input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            // First, HTML encode
            var sanitized = HttpUtility.HtmlEncode(input);

            // Remove any remaining dangerous patterns (even if encoded)
            sanitized = ScriptPattern.Replace(sanitized, "[removed]");
            sanitized = DataUriPattern.Replace(sanitized, "[removed]");

            // Trim excessive whitespace
            sanitized = Regex.Replace(sanitized, @"\s{10,}", " ");

            // Limit message length to prevent DoS
            const int maxLength = 10000;
            if (sanitized.Length > maxLength)
            {
                sanitized = sanitized[..maxLength] + "...";
            }

            return sanitized;
        }
    }
}
