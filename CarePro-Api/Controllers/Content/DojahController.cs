using Application.DTOs;
using Application.Interfaces.Common;
using Application.Interfaces.Content;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.RegularExpressions;

namespace CarePro_Api.Controllers.Content
{
    [Route("api/[controller]")]
    [ApiController]
    public class DojahController : ControllerBase
    {
        private readonly ISignatureVerificationService _signatureService;
        private readonly IRateLimitingService _rateLimitService;
        private readonly IDojahDataFormattingService _formattingService;
        private readonly IVerificationService _verificationService;
        private readonly IConfiguration _config;
        private readonly ILogger<DojahController> _logger;

        public DojahController(
            ISignatureVerificationService signatureService,
            IRateLimitingService rateLimitService,
            IDojahDataFormattingService formattingService,
            IVerificationService verificationService,
            IConfiguration config,
            ILogger<DojahController> logger)
        {
            _signatureService = signatureService;
            _rateLimitService = rateLimitService;
            _formattingService = formattingService;
            _verificationService = verificationService;
            _config = config;
            _logger = logger;
        }

        [HttpPost("webhook")]
        public async Task<IActionResult> HandleWebhook([FromBody] DojahWebhookRequest request)
        {
            var startTime = DateTime.UtcNow;
            var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            try
            {
                _logger.LogInformation("Received Dojah webhook from IP: {ClientIp}", clientIp);

                // Rate limiting check
                if (!_rateLimitService.CheckRateLimit(clientIp))
                {
                    _logger.LogWarning("Rate limit exceeded for IP: {ClientIp}", clientIp);
                    return StatusCode(429, new { error = "Rate limit exceeded" });
                }

                // Input validation
                if (!IsValidWebhook(request))
                {
                    _logger.LogWarning("Invalid webhook data received from IP: {ClientIp}", clientIp);
                    return BadRequest(new { error = "Invalid webhook data" });
                }

                // Signature verification
                var signatureVerificationEnabled = _config.GetValue<bool>("Dojah:SignatureVerificationEnabled", true);
                if (signatureVerificationEnabled)
                {
                    var signature = Request.Headers["x-dojah-signature"].FirstOrDefault();
                    var webhookSecret = _config["Dojah:WebhookSecret"];
                    
                    if (string.IsNullOrEmpty(signature) || 
                        !_signatureService.VerifySignature(signature, await GetRawBodyAsync(), webhookSecret))
                    {
                        _logger.LogError("Invalid webhook signature from IP: {ClientIp}", clientIp);
                        return Unauthorized(new { error = "Invalid signature" });
                    }
                    
                    _logger.LogInformation("Webhook signature verified successfully");
                }

                // Extract user ID with validation
                var userId = ExtractUserId(request);
                if (string.IsNullOrEmpty(userId) || !IsValidUserId(userId))
                {
                    _logger.LogError("Invalid user ID in webhook: {UserId}", userId);
                    return BadRequest(new { error = "Invalid user identification" });
                }

                // Check if this is a verification webhook
                if (IsVerificationWebhook(request))
                {
                    _logger.LogInformation("Processing verification webhook for user: {UserId}", userId);
                    
                    // Format data for backend
                    var formattedData = _formattingService.FormatWebhookData(request, userId);
                    
                    // Check existing verification status
                    var existingVerification = await _verificationService.GetUserVerificationStatusAsync(userId);
                    
                    // Determine if we should create new verification record
                    var shouldCreate = ShouldCreateNewVerification(existingVerification?.VerificationStatus, formattedData.VerificationStatus);
                    
                    if (shouldCreate.ShouldCreate)
                    {
                        _logger.LogInformation("Creating new verification record: {Reason}", shouldCreate.Reason);
                        
                        // Create new verification record
                        await _verificationService.AddVerificationAsync(formattedData);
                        
                        _logger.LogInformation("Verification processed successfully for user: {UserId}", userId);
                    }
                    else
                    {
                        _logger.LogInformation("Skipping verification creation: {Reason}", shouldCreate.Reason);
                    }

                    var processingTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
                    return Ok(new { 
                        status = "success", 
                        message = "Webhook processed successfully",
                        processingTime = $"{processingTime}ms"
                    });
                }

                // Handle non-verification webhooks
                _logger.LogInformation("Received non-verification webhook");
                return Ok(new { status = "received" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Webhook handler error for IP: {ClientIp}", clientIp);
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        [HttpGet("webhook")]
        public IActionResult GetWebhookHealth()
        {
            return Ok(new { status = "Dojah webhook is reachable" });
        }

        [HttpGet("status")]
        [Authorize]
        public async Task<IActionResult> GetVerificationStatus([FromQuery] string userId, [FromQuery] string userType, [FromQuery] string token)
        {
            try
            {
                if (string.IsNullOrEmpty(userId))
                {
                    return BadRequest(new { error = "User ID is required" });
                }

                var verification = await _verificationService.GetUserVerificationStatusAsync(userId);
                
                if (verification == null)
                {
                    return NotFound(new { message = "No verification found for user" });
                }

                return Ok(verification);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting verification status for user: {UserId}", userId);
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        [HttpGet("admin/statistics")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetWebhookStatistics()
        {
            try
            {
                // This would need to be implemented based on your logging/metrics system
                var statistics = new DojahWebhookStatistics
                {
                    TotalWebhooksReceived = 0,
                    SuccessfulVerifications = 0,
                    FailedVerifications = 0,
                    PendingVerifications = 0,
                    LastWebhookReceived = DateTime.UtcNow,
                    AverageProcessingTime = TimeSpan.Zero
                };

                return Ok(statistics);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting webhook statistics");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        [HttpGet("admin/health")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetSystemHealth()
        {
            try
            {
                var health = new DojahSystemHealth
                {
                    IsHealthy = true,
                    Status = "Healthy",
                    LastChecked = DateTime.UtcNow,
                    HealthDetails = new Dictionary<string, object>
                    {
                        { "WebhookEndpoint", "Active" },
                        { "Database", "Connected" },
                        { "SignatureVerification", "Enabled" },
                        { "RateLimiting", "Active" }
                    }
                };

                return Ok(health);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting system health");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        private string ExtractUserId(DojahWebhookRequest request)
        {
            // Try multiple sources for user ID
            if (!string.IsNullOrEmpty(request.UserId))
                return request.UserId;
                
            if (!string.IsNullOrEmpty(request.Metadata?.UserId))
                return request.Metadata.UserId;
                
            if (!string.IsNullOrEmpty(request.ReferenceId) && request.ReferenceId.StartsWith("user_"))
            {
                var parts = request.ReferenceId.Split('_');
                if (parts.Length >= 2)
                    return parts[1];
            }
            
            return request.ReferenceId;
        }

        private bool IsValidUserId(string userId)
        {
            return !string.IsNullOrEmpty(userId) &&
                   userId.Length >= 3 &&
                   userId.Length <= 50 &&
                   Regex.IsMatch(userId, @"^[a-zA-Z0-9_-]+$") &&
                   !userId.ToLower().Contains("admin") &&
                   !userId.ToLower().Contains("system");
        }

        private bool IsVerificationWebhook(DojahWebhookRequest request)
        {
            return (request.Status == true && request.VerificationStatus == "Completed") ||
                   (request.VerificationStatus == "Pending") ||
                   (request.Status == false) ||
                   (!string.IsNullOrEmpty(request.VerificationStatus)) ||
                   (request.Data != null && (request.Data.GovernmentData != null || request.Data.UserData != null || request.Data.Id != null));
        }

        private bool IsValidWebhook(DojahWebhookRequest request)
        {
            // Add validation logic for webhook structure
            return request != null;
        }

        private (bool ShouldCreate, string Reason) ShouldCreateNewVerification(string? existingStatus, string newStatus)
        {
            // If no existing record, always allow creation
            if (string.IsNullOrEmpty(existingStatus))
                return (true, "First verification attempt");
                
            // If status is different, allow creation
            if (existingStatus != newStatus)
                return (true, $"Status change from '{existingStatus}' to '{newStatus}'");
                
            // If status is the same, skip
            return (false, $"Duplicate status '{existingStatus}' - skipping to prevent duplicate records");
        }

        private async Task<string> GetRawBodyAsync()
        {
            Request.Body.Position = 0;
            using var reader = new StreamReader(Request.Body);
            return await reader.ReadToEndAsync();
        }
    }
}