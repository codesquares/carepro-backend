using Application.Interfaces.Content;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CarePro_Api.Controllers.Content
{
    [ApiController]
    [Route("api/admin/analytics/gig-views")]
    [Authorize(Policy = "AnalyticsPolicy")]
    public class AdminGigViewsAnalyticsController : ControllerBase
    {
        private readonly IAnalyticsService _analyticsService;

        public AdminGigViewsAnalyticsController(IAnalyticsService analyticsService)
        {
            _analyticsService = analyticsService;
        }

        [HttpGet("overview")]
        public async Task<IActionResult> GetOverview([FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            var result = await _analyticsService.GetGigViewsOverviewAsync(from, to);
            return Ok(new { success = true, data = result });
        }

        [HttpGet("top")]
        public async Task<IActionResult> GetTopGigs([FromQuery] int limit = 20, [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null)
        {
            var result = await _analyticsService.GetTopGigViewsAsync(limit, from, to);
            return Ok(new { success = true, data = result });
        }

        [HttpGet("{gigId}/timeseries")]
        public async Task<IActionResult> GetTimeseries(string gigId, [FromQuery] string bucket = "day", [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null)
        {
            var result = await _analyticsService.GetGigViewsTimeseriesAsync(gigId, bucket, from, to);
            return Ok(new { success = true, data = result });
        }
    }
}
