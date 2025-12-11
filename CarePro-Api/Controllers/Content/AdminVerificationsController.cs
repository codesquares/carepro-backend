using Application.DTOs;
using Application.Interfaces.Content;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CarePro_Api.Controllers.Content
{
    [Route("api/Admin/Verifications")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class AdminVerificationsController : ControllerBase
    {
        private readonly IWebhookLogService _webhookLogService;
        private readonly IVerificationService _verificationService;
        private readonly ILogger<AdminVerificationsController> _logger;

        public AdminVerificationsController(
            IWebhookLogService webhookLogService,
            IVerificationService verificationService,
            ILogger<AdminVerificationsController> logger)
        {
            _webhookLogService = webhookLogService;
            _verificationService = verificationService;
            _logger = logger;
        }

        [HttpGet("PendingReviews")]
        public async Task<IActionResult> GetPendingReviews()
        {
            try
            {
                _logger.LogInformation("Admin requesting pending verifications for review");

                var pendingVerifications = await _webhookLogService.GetPendingVerificationsForReviewAsync();

                return Ok(pendingVerifications);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving pending verifications for review");
                return StatusCode(500, new { error = "Failed to retrieve pending verifications" });
            }
        }

        [HttpGet("WebhookDetails/{webhookLogId}")]
        public async Task<IActionResult> GetWebhookDetails(string webhookLogId)
        {
            try
            {
                _logger.LogInformation("Admin requesting webhook details for: {WebhookLogId}", webhookLogId);

                var webhookData = await _webhookLogService.GetParsedWebhookDataAsync(webhookLogId);

                if (webhookData == null)
                {
                    return NotFound(new { error = "Webhook log not found" });
                }

                return Ok(webhookData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving webhook details for: {WebhookLogId}", webhookLogId);
                return StatusCode(500, new { error = "Failed to retrieve webhook details" });
            }
        }

        [HttpPut("Review")]
        public async Task<IActionResult> ReviewVerification([FromBody] AdminVerificationReviewRequest request)
        {
            try
            {
                _logger.LogInformation("Admin {AdminId} reviewing verification {VerificationId} with decision: {Decision}",
                    request.AdminId, request.VerificationId, request.Decision);

                // Validate request
                if (string.IsNullOrEmpty(request.VerificationId))
                {
                    return BadRequest(new { error = "VerificationId is required" });
                }

                if (string.IsNullOrEmpty(request.AdminId))
                {
                    return BadRequest(new { error = "AdminId is required" });
                }

                if (string.IsNullOrEmpty(request.Decision) || 
                    (request.Decision != "Approve" && request.Decision != "Reject"))
                {
                    return BadRequest(new { error = "Decision must be 'Approve' or 'Reject'" });
                }

                // Get verification details
                var verification = await _verificationService.GetVerificationAsync(
                    request.VerificationId.Replace("verificationId=", "")
                );

                if (verification == null)
                {
                    return NotFound(new { error = "Verification not found" });
                }

                // Check if already verified
                if (verification.IsVerified)
                {
                    return BadRequest(new { error = "This verification has already been approved" });
                }

                // Prepare update request
                var updateRequest = new UpdateVerificationRequest
                {
                    VerificationMode = verification.VerificationMethod,
                    VerificationStatus = request.Decision == "Approve" ? "Verified" : "Failed"
                };

                // Update verification
                var result = await _verificationService.UpdateVerificationAsync(
                    verification.VerificationId,
                    updateRequest
                );

                // Update webhook log if provided
                if (!string.IsNullOrEmpty(request.ReviewedWebhookLogId))
                {
                    await _webhookLogService.UpdateWebhookLogStatusAsync(
                        request.ReviewedWebhookLogId,
                        "admin_reviewed",
                        verification.VerificationId,
                        $"Admin decision: {request.Decision}. Notes: {request.AdminNotes ?? "None"}"
                    );
                }

                var response = new AdminVerificationReviewResponse
                {
                    Success = true,
                    NewStatus = request.Decision == "Approve" ? "Verified" : "Failed",
                    Message = $"Verification {request.Decision.ToLower()}d successfully by admin",
                    VerificationId = request.VerificationId
                };

                _logger.LogInformation("Admin {AdminId} {Decision} verification {VerificationId}",
                    request.AdminId, request.Decision, request.VerificationId);

                return Ok(response);
            }
            catch (KeyNotFoundException ex)
            {
                _logger.LogWarning(ex, "Verification not found: {VerificationId}", request.VerificationId);
                return NotFound(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reviewing verification {VerificationId}", request.VerificationId);
                return StatusCode(500, new { error = "Failed to process verification review" });
            }
        }

        [HttpGet("WebhookLogs/User/{userId}")]
        public async Task<IActionResult> GetUserWebhookLogs(string userId)
        {
            try
            {
                _logger.LogInformation("Admin requesting webhook logs for user: {UserId}", userId);

                var webhookLogs = await _webhookLogService.GetWebhookLogsByUserIdAsync(userId);

                return Ok(webhookLogs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving webhook logs for user: {UserId}", userId);
                return StatusCode(500, new { error = "Failed to retrieve webhook logs" });
            }
        }
    }
}
