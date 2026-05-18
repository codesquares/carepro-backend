using Application.DTOs;
using Application.Interfaces.Content;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CarePro_Api.Controllers
{
    [ApiController]
    [Route("api/analytics")]
    public class AnalyticsController : ControllerBase
    {
        private readonly IAnalyticsService _analyticsService;
        private readonly ILogger<AnalyticsController> _logger;

        public AnalyticsController(IAnalyticsService analyticsService, ILogger<AnalyticsController> logger)
        {
            _analyticsService = analyticsService;
            _logger = logger;
        }

        /// <summary>
        /// Track an ad campaign analytics event. Public — no authentication required.
        /// </summary>
        [HttpPost("event")]
        [AllowAnonymous]
        public async Task<IActionResult> TrackEvent([FromBody] TrackAnalyticsEventRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.EventType) || string.IsNullOrWhiteSpace(request.Page))
                return BadRequest(new { success = false, message = "eventType and page are required." });

            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

            // Prefer X-Forwarded-For when behind a reverse proxy / load balancer
            if (HttpContext.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor))
            {
                var firstIp = forwardedFor.ToString().Split(',')[0].Trim();
                if (!string.IsNullOrWhiteSpace(firstIp))
                    ipAddress = firstIp;
            }

            await _analyticsService.TrackEventAsync(request, ipAddress);

            return Ok(new { success = true });
        }

        /// <summary>
        /// Retrieve paginated analytics events with summary counts. Admin only.
        /// </summary>
        [HttpGet("events")]
        [Authorize(Policy = "AnalyticsPolicy")]
        public async Task<IActionResult> GetEvents([FromQuery] AnalyticsEventsQuery query)
        {
            var result = await _analyticsService.GetEventsAsync(query);
            return Ok(new { success = true, data = result });
        }
    }
}
