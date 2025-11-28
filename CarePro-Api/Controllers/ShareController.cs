using Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Text;

namespace CarePro_Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ShareController : ControllerBase
    {
        private readonly IGigServices _gigServices;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ShareController> _logger;

        public ShareController(
            IGigServices gigServices, 
            IConfiguration configuration,
            ILogger<ShareController> logger)
        {
            _gigServices = gigServices;
            _configuration = configuration;
            _logger = logger;
        }

        /// <summary>
        /// Endpoint for social media sharing that returns HTML with Open Graph meta tags
        /// </summary>
        /// <param name="gigId">The ID of the gig to share</param>
        /// <returns>HTML page with meta tags for social media crawlers</returns>
        [HttpGet("gig/{gigId}")]
        public async Task<IActionResult> ShareGig(string gigId)
        {
            try
            {
                // Fetch gig data from database
                var gig = await _gigServices.GetGigAsync(gigId);

                if (gig == null)
                {
                    _logger.LogWarning($"Gig with ID {gigId} not found");
                    return NotFound("Gig not found");
                }

                // Get frontend URL from configuration
                var frontendUrl = _configuration["FrontendUrl"] ?? "https://oncarepro.com";

                // Build description from available fields
                var description = BuildDescription(gig);

                // Generate HTML with Open Graph meta tags
                var html = GenerateShareHtml(
                    title: gig.Title,
                    description: description,
                    imageUrl: gig.Image1,
                    gigUrl: $"{frontendUrl}/service/{gigId}",
                    frontendUrl: frontendUrl
                );

                return Content(html, "text/html", Encoding.UTF8);
            }
            catch (KeyNotFoundException ex)
            {
                _logger.LogError(ex, $"Gig not found: {gigId}");
                return NotFound(new { Message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error generating share page for gig {gigId}");
                return StatusCode(500, new { Message = "An error occurred while generating the share page" });
            }
        }

        /// <summary>
        /// Builds a description from gig fields
        /// </summary>
        private string BuildDescription(Application.DTOs.GigDTO gig)
        {
            var descriptionParts = new List<string>();

            // Add category and subcategories
            if (!string.IsNullOrEmpty(gig.Category))
            {
                descriptionParts.Add(gig.Category);
            }

            if (gig.SubCategory != null && gig.SubCategory.Any())
            {
                descriptionParts.Add(string.Join(", ", gig.SubCategory));
            }

            // Add package name
            if (!string.IsNullOrEmpty(gig.PackageName))
            {
                descriptionParts.Add($"Package: {gig.PackageName}");
            }

            // Add package details (first few items)
            if (gig.PackageDetails != null && gig.PackageDetails.Any())
            {
                var details = string.Join(", ", gig.PackageDetails.Take(3));
                descriptionParts.Add(details);
            }

            // Add price and delivery time
            if (gig.Price > 0)
            {
                descriptionParts.Add($"₦{gig.Price:N0}");
            }

            if (!string.IsNullOrEmpty(gig.DeliveryTime))
            {
                descriptionParts.Add($"Delivery: {gig.DeliveryTime}");
            }

            var fullDescription = string.Join(" • ", descriptionParts);

            // Limit to 200 characters for social media
            return fullDescription.Length > 200 
                ? fullDescription.Substring(0, 197) + "..." 
                : fullDescription;
        }

        /// <summary>
        /// Generates HTML with Open Graph and Twitter Card meta tags
        /// </summary>
        private string GenerateShareHtml(
            string title, 
            string description, 
            string imageUrl, 
            string gigUrl,
            string frontendUrl)
        {
            // Use a default image if none is provided
            var ogImage = string.IsNullOrEmpty(imageUrl) 
                ? $"{frontendUrl}/default-gig-image.png" 
                : imageUrl;

            return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>{EscapeHtml(title)}</title>
    
    <!-- Open Graph Meta Tags for Facebook, LinkedIn, WhatsApp -->
    <meta property=""og:title"" content=""{EscapeHtml(title)}"" />
    <meta property=""og:description"" content=""{EscapeHtml(description)}"" />
    <meta property=""og:image"" content=""{EscapeHtml(ogImage)}"" />
    <meta property=""og:image:secure_url"" content=""{EscapeHtml(ogImage)}"" />
    <meta property=""og:image:width"" content=""1200"" />
    <meta property=""og:image:height"" content=""630"" />
    <meta property=""og:url"" content=""{EscapeHtml(gigUrl)}"" />
    <meta property=""og:type"" content=""website"" />
    <meta property=""og:site_name"" content=""CarePro"" />
    
    <!-- Twitter Card Meta Tags -->
    <meta name=""twitter:card"" content=""summary_large_image"" />
    <meta name=""twitter:title"" content=""{EscapeHtml(title)}"" />
    <meta name=""twitter:description"" content=""{EscapeHtml(description)}"" />
    <meta name=""twitter:image"" content=""{EscapeHtml(ogImage)}"" />
    
    <!-- Auto-redirect to frontend after crawlers read meta tags -->
    <meta http-equiv=""refresh"" content=""0;url={EscapeHtml(gigUrl)}"" />
    
    <style>
        body {{
            font-family: Arial, sans-serif;
            display: flex;
            justify-content: center;
            align-items: center;
            height: 100vh;
            margin: 0;
            background-color: #f5f5f5;
        }}
        .message {{
            text-align: center;
            padding: 20px;
        }}
        .spinner {{
            border: 4px solid #f3f3f3;
            border-top: 4px solid #3498db;
            border-radius: 50%;
            width: 40px;
            height: 40px;
            animation: spin 1s linear infinite;
            margin: 0 auto 20px;
        }}
        @keyframes spin {{
            0% {{ transform: rotate(0deg); }}
            100% {{ transform: rotate(360deg); }}
        }}
    </style>
</head>
<body>
    <div class=""message"">
        <div class=""spinner""></div>
        <p>Redirecting to CarePro...</p>
        <p><a href=""{EscapeHtml(gigUrl)}"">Click here if you are not redirected automatically</a></p>
    </div>
</body>
</html>";
        }

        /// <summary>
        /// Escapes HTML special characters to prevent XSS
        /// </summary>
        private string EscapeHtml(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            return text
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&#39;");
        }
    }
}
