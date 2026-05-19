using Application.DTOs;
using Application.Interfaces.Content;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace CarePro_Api.Controllers.Content
{
    [Route("api/push")]
    [ApiController]
    public class PushController : ControllerBase
    {
        private readonly IPushService _pushService;
        private readonly WebPushSettings _settings;
        private readonly ILogger<PushController> _logger;

        public PushController(
            IPushService pushService,
            IOptions<WebPushSettings> settings,
            ILogger<PushController> logger)
        {
            _pushService = pushService;
            _settings = settings.Value;
            _logger = logger;
        }

        private string? GetCurrentUserId() =>
            User.FindFirstValue(ClaimTypes.NameIdentifier) ??
            User.FindFirstValue("sub");

        // -----------------------------------------------------------------------
        // GET /api/push/vapid-public-key
        // Public — no auth required. Returns the VAPID public key for the SW.
        // -----------------------------------------------------------------------
        [HttpGet("vapid-public-key")]
        [AllowAnonymous]
        public IActionResult GetVapidPublicKey()
        {
            var publicKey = _pushService.GetVapidPublicKey();

            if (string.IsNullOrWhiteSpace(publicKey))
            {
                _logger.LogWarning("VAPID public key requested but not configured.");
                return StatusCode(503, new { message = "Push notifications are not configured on this server." });
            }

            return Ok(new VapidPublicKeyResponse { PublicKey = publicKey });
        }

        // -----------------------------------------------------------------------
        // POST /api/push/subscribe
        // JWT-gated. Upserts a push subscription for the authenticated user.
        // Body: { endpoint, p256dh, auth, userAgent?, platform? }
        // Returns 204 No Content on success.
        // -----------------------------------------------------------------------
        [HttpPost("subscribe")]
        [Authorize]
        public async Task<IActionResult> Subscribe([FromBody] SubscribePushRequest request)
        {
            var userId = GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            if (string.IsNullOrWhiteSpace(request.Endpoint) ||
                string.IsNullOrWhiteSpace(request.P256dh) ||
                string.IsNullOrWhiteSpace(request.Auth))
            {
                return BadRequest(new { message = "endpoint, p256dh, and auth are required." });
            }

            try
            {
                await _pushService.SubscribeAsync(userId, request);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving push subscription for user {UserId}", userId);
                return StatusCode(500, new { message = "Failed to save push subscription." });
            }
        }

        // -----------------------------------------------------------------------
        // DELETE /api/push/subscribe
        // JWT-gated. Removes a push subscription by endpoint for the authenticated user.
        // Body: { endpoint }
        // Returns 204 No Content on success.
        // -----------------------------------------------------------------------
        [HttpDelete("subscribe")]
        [Authorize]
        public async Task<IActionResult> Unsubscribe([FromBody] UnsubscribePushRequest request)
        {
            var userId = GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            if (string.IsNullOrWhiteSpace(request.Endpoint))
                return BadRequest(new { message = "endpoint is required." });

            try
            {
                await _pushService.UnsubscribeAsync(userId, request.Endpoint);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing push subscription for user {UserId}", userId);
                return StatusCode(500, new { message = "Failed to remove push subscription." });
            }
        }
    }
}
