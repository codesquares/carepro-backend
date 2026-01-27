using System.Text.RegularExpressions;
using System.Web;

namespace Application.Interfaces.Common
{
    /// <summary>
    /// Service for sanitizing user-generated content to prevent XSS attacks.
    /// </summary>
    public interface IContentSanitizer
    {
        /// <summary>
        /// Sanitizes text content by encoding HTML entities.
        /// </summary>
        string SanitizeText(string? input);
        
        /// <summary>
        /// Sanitizes text and removes potentially dangerous patterns.
        /// </summary>
        string SanitizeStrict(string? input);
    }
}
