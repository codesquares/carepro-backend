using System.Text;
using System.Text.Json;
using Application.DTOs;
using Application.Interfaces.Content;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Content.Services
{
    public class GigImageModerationService : IGigImageModerationService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<GigImageModerationService> _logger;
        private readonly long _maxFileSizeBytes;
        private readonly int _minWidthPx;
        private readonly int _minHeightPx;
        private readonly int _maxWidthPx;
        private readonly int _maxHeightPx;

        public GigImageModerationService(
            IConfiguration configuration,
            ILogger<GigImageModerationService> logger)
        {
            _configuration = configuration;
            _logger = logger;
            _maxFileSizeBytes = configuration.GetValue<long>("GigImageValidation:MaxFileSizeBytes", 5_242_880);
            _minWidthPx = configuration.GetValue<int>("GigImageValidation:MinWidthPx", 300);
            _minHeightPx = configuration.GetValue<int>("GigImageValidation:MinHeightPx", 300);
            _maxWidthPx = configuration.GetValue<int>("GigImageValidation:MaxWidthPx", 5000);
            _maxHeightPx = configuration.GetValue<int>("GigImageValidation:MaxHeightPx", 5000);
        }

        public async Task<GigImageModerationResult> ValidateAsync(byte[] imageBytes, string fileName, string gigTitle)
        {
            var layer1 = RunLayer1(imageBytes, fileName);
            if (!layer1.IsApproved)
                return layer1;

            return await RunLayer2Async(imageBytes, gigTitle);
        }

        // ── Layer 1: Technical checks ─────────────────────────────────────────

        private GigImageModerationResult RunLayer1(byte[] imageBytes, string fileName)
        {
            if (imageBytes.Length < 12)
            {
                return GigImageModerationResult.Rejected(
                    "The uploaded file is too small to be a valid image.",
                    new List<string> { "Please select a proper image file (JPEG, PNG, or WebP)." });
            }

            if (imageBytes.Length > _maxFileSizeBytes)
            {
                var maxMb = _maxFileSizeBytes / 1_048_576.0;
                var actualMb = imageBytes.Length / 1_048_576.0;
                return GigImageModerationResult.Rejected(
                    $"The image is too large ({actualMb:F1} MB). The maximum allowed size is {maxMb:F0} MB.",
                    new List<string>
                    {
                        $"Reduce the image size to under {maxMb:F0} MB before uploading.",
                        "Try compressing it at tinypng.com or squoosh.app.",
                        "A clear gig photo does not need to be larger than 2 MB."
                    });
            }

            var detectedFormat = DetectFormatFromMagicBytes(imageBytes);
            if (detectedFormat == null)
            {
                return GigImageModerationResult.Rejected(
                    "The uploaded file does not appear to be a valid image.",
                    new List<string>
                    {
                        "Only JPEG, PNG, and WebP images are accepted.",
                        "Make sure the file has not been renamed from another format.",
                        "Take a new photo and upload it directly from your device."
                    });
            }

            bool gotDimensions = TryReadDimensions(imageBytes, detectedFormat, out int width, out int height);
            if (!gotDimensions)
            {
                _logger.LogWarning("Could not read dimensions from {Format} image '{FileName}'. Skipping dimension check.", detectedFormat, fileName);
                return GigImageModerationResult.Approved();
            }

            if (width < _minWidthPx || height < _minHeightPx)
            {
                return GigImageModerationResult.Rejected(
                    $"The image is too small ({width}x{height}px). The minimum accepted size is {_minWidthPx}x{_minHeightPx}px.",
                    new List<string>
                    {
                        $"Use an image of at least {_minWidthPx}x{_minHeightPx} pixels.",
                        "Small or thumbnail images do not display well on gig cards.",
                        "A photo taken on any modern phone will comfortably meet this requirement."
                    });
            }

            if (width > _maxWidthPx || height > _maxHeightPx)
            {
                return GigImageModerationResult.Rejected(
                    $"The image is too large ({width}x{height}px). The maximum accepted size is {_maxWidthPx}x{_maxHeightPx}px.",
                    new List<string>
                    {
                        $"Resize the image to a maximum of {_maxWidthPx}x{_maxHeightPx} pixels.",
                        "Try resizing at squoosh.app before uploading."
                    });
            }

            return GigImageModerationResult.Approved();
        }

        // ── Magic byte detection ──────────────────────────────────────────────

        private static string? DetectFormatFromMagicBytes(byte[] b)
        {
            if (b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF)
                return "JPEG";

            if (b.Length >= 8 &&
                b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 &&
                b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A)
                return "PNG";

            if (b.Length >= 12 &&
                b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46 &&
                b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50)
                return "WebP";

            return null;
        }

        // ── Dimension extraction from raw header bytes ────────────────────────

        private static bool TryReadDimensions(byte[] b, string format, out int width, out int height)
        {
            width = 0;
            height = 0;
            try
            {
                switch (format)
                {
                    case "PNG":
                        if (b.Length < 24) return false;
                        width = ReadInt32BE(b, 16);
                        height = ReadInt32BE(b, 20);
                        return true;

                    case "WebP":
                        return TryReadWebPDimensions(b, out width, out height);

                    case "JPEG":
                        return TryReadJpegDimensions(b, out width, out height);
                }
            }
            catch { /* malformed header — skip */ }
            return false;
        }

        private static bool TryReadWebPDimensions(byte[] b, out int width, out int height)
        {
            width = 0; height = 0;
            if (b.Length < 30) return false;

            // VP8 (lossy)
            if (b[12] == 0x56 && b[13] == 0x50 && b[14] == 0x38 && b[15] == 0x20)
            {
                width = (b[26] | (b[27] << 8)) & 0x3FFF;
                height = (b[28] | (b[29] << 8)) & 0x3FFF;
                return width > 0 && height > 0;
            }
            // VP8X (extended)
            if (b[12] == 0x56 && b[13] == 0x50 && b[14] == 0x38 && b[15] == 0x58)
            {
                width = (b[24] | (b[25] << 8) | (b[26] << 16)) + 1;
                height = (b[27] | (b[28] << 8) | (b[29] << 16)) + 1;
                return width > 0 && height > 0;
            }
            // VP8L (lossless)
            if (b[12] == 0x56 && b[13] == 0x50 && b[14] == 0x38 && b[15] == 0x4C)
            {
                if (b.Length < 25 || b[20] != 0x2F) return false;
                uint bits = (uint)(b[21] | (b[22] << 8) | (b[23] << 16) | (b[24] << 24));
                width = (int)(bits & 0x3FFF) + 1;
                height = (int)((bits >> 14) & 0x3FFF) + 1;
                return width > 0 && height > 0;
            }
            return false;
        }

        private static bool TryReadJpegDimensions(byte[] b, out int width, out int height)
        {
            width = 0; height = 0;
            int i = 2;
            while (i + 4 <= b.Length)
            {
                if (b[i] != 0xFF) return false;
                byte marker = b[i + 1];
                if (marker == 0xC0 || marker == 0xC1 || marker == 0xC2 || marker == 0xC3 ||
                    marker == 0xC5 || marker == 0xC6 || marker == 0xC7 ||
                    marker == 0xC9 || marker == 0xCA || marker == 0xCB ||
                    marker == 0xCD || marker == 0xCE || marker == 0xCF)
                {
                    if (i + 9 > b.Length) return false;
                    height = ReadInt16BE(b, i + 5);
                    width = ReadInt16BE(b, i + 7);
                    return width > 0 && height > 0;
                }
                if (i + 3 >= b.Length) return false;
                int segLen = ReadInt16BE(b, i + 2);
                i += 2 + segLen;
            }
            return false;
        }

        private static int ReadInt32BE(byte[] b, int o) =>
            (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3];

        private static int ReadInt16BE(byte[] b, int o) =>
            (b[o] << 8) | b[o + 1];

        // ── Layer 2: OpenAI gpt-4o Vision check ──────────────────────────────

        private async Task<GigImageModerationResult> RunLayer2Async(byte[] imageBytes, string gigTitle)
        {
            var apiKey = _configuration["LLMSettings:OpenAI:ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _logger.LogWarning("OpenAI API key not configured. Skipping Layer 2 image moderation.");
                return GigImageModerationResult.Approved();
            }

            var model = _configuration["LLMSettings:ImageModeration:Model"] ?? "gpt-4o";
            var maxTokens = _configuration.GetValue<int>("LLMSettings:ImageModeration:MaxTokens", 400);
            var base64Image = Convert.ToBase64String(imageBytes);

            var mimeType = imageBytes[0] == 0x89 ? "image/png"
                : imageBytes[0] == 0xFF && imageBytes[1] == 0xD8 ? "image/jpeg"
                : "image/webp";

            var prompt = BuildModerationPrompt(gigTitle);

            var requestBody = new
            {
                model,
                max_tokens = maxTokens,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new
                            {
                                type = "image_url",
                                image_url = new
                                {
                                    url = $"data:{mimeType};base64,{base64Image}",
                                    detail = "low"
                                }
                            },
                            new { type = "text", text = prompt }
                        }
                    }
                }
            };

            try
            {
                using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

                var json = JsonSerializer.Serialize(requestBody);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await httpClient.PostAsync("https://api.openai.com/v1/chat/completions", content);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("OpenAI image moderation API error: {StatusCode}", response.StatusCode);
                    return GigImageModerationResult.Approved();
                }

                return ParseOpenAiResponse(responseBody);
            }
            catch (TaskCanceledException)
            {
                _logger.LogWarning("OpenAI image moderation timed out for gig '{GigTitle}'. Allowing upload.", gigTitle);
                return GigImageModerationResult.Approved();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during OpenAI image moderation. Allowing upload.");
                return GigImageModerationResult.Approved();
            }
        }

        private static string BuildModerationPrompt(string gigTitle)
        {
            return "You are a content moderator for CarePro, a professional caregiving services marketplace in Nigeria.\n" +
                   "Caregivers on this platform offer services such as elderly care, medical assistance, childcare, disability support, and home support.\n\n" +
                   "The caregiver is uploading a cover image for their gig titled: \"" + gigTitle + "\"\n\n" +
                   "Your job is to decide whether this image is appropriate to use as the cover photo for that specific gig.\n\n" +
                   "IMPORTANT CONTEXT:\n" +
                   "- A personal or professional photo of the caregiver themselves IS appropriate and should be approved.\n" +
                   "  Caregivers frequently use profile-style photos to show who they are — this builds client trust.\n" +
                   "- A photo of the caregiver with a client, in a care setting, at a hospital, or in a home environment is ideal.\n" +
                   "- The image does not need to explicitly show caregiving activity. A clear, professional photo of a person\n" +
                   "  is acceptable as long as it is reasonable in context of the gig title.\n\n" +
                   "REJECT the image ONLY if it falls clearly into one of these categories:\n" +
                   "1. Explicit, sexual, violent, or offensive content.\n" +
                   "2. Completely unrelated content with no connection to the gig title and no person visible\n" +
                   "   (e.g. a standalone photo of food, a car, a pet, a landscape, a cartoon, or a meme).\n" +
                   "3. A pure logo or text graphic with no person shown.\n" +
                   "4. An image so blurry, dark, or obscured that the subject is entirely unrecognizable.\n\n" +
                   "Do NOT reject a personal photo of a person just because it is not explicitly medical or care-related.\n" +
                   "Use the gig title as context when the connection is not immediately obvious.\n\n" +
                   "Respond with ONLY a valid JSON object — no markdown, no extra text:\n" +
                   "{\n" +
                   "  \"approved\": true or false,\n" +
                   "  \"reason\": \"one clear sentence explaining your decision\",\n" +
                   "  \"suggestions\": [\"tip 1\", \"tip 2\"]\n" +
                   "}\n\n" +
                   "If approved is true, suggestions must be an empty array [].\n" +
                   "If approved is false, suggestions must contain 2-3 actionable tips for the caregiver.";
        }

        private GigImageModerationResult ParseOpenAiResponse(string responseBody)
        {
            try
            {
                using var doc = JsonDocument.Parse(responseBody);
                var messageContent = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString() ?? string.Empty;

                var cleaned = messageContent
                    .Replace("```json", "")
                    .Replace("```", "")
                    .Trim();

                using var verdict = JsonDocument.Parse(cleaned);
                var root = verdict.RootElement;

                bool approved = root.GetProperty("approved").GetBoolean();
                string reason = root.TryGetProperty("reason", out var r) ? r.GetString() ?? "" : "";

                var suggestions = new List<string>();
                if (root.TryGetProperty("suggestions", out var sugsEl) && sugsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var sug in sugsEl.EnumerateArray())
                    {
                        var s = sug.GetString();
                        if (!string.IsNullOrWhiteSpace(s))
                            suggestions.Add(s);
                    }
                }

                return approved
                    ? GigImageModerationResult.Approved()
                    : GigImageModerationResult.Rejected(reason, suggestions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse OpenAI moderation response. Allowing upload.");
                return GigImageModerationResult.Approved();
            }
        }
    }
}
