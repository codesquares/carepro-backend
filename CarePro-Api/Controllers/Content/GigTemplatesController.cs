using Application.Interfaces.Content;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CarePro_Api.Controllers.Content
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class GigTemplatesController : ControllerBase
    {
        private readonly IGigTemplateService _gigTemplateService;
        private readonly ILogger<GigTemplatesController> _logger;

        public GigTemplatesController(IGigTemplateService gigTemplateService, ILogger<GigTemplatesController> logger)
        {
            _gigTemplateService = gigTemplateService;
            _logger = logger;
        }

        [HttpGet]
        [AllowAnonymous]
        [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Any)]
        public async Task<IActionResult> GetGigTemplates([FromQuery] string? category = null)
        {
            try
            {
                var result = string.IsNullOrWhiteSpace(category)
                    ? await _gigTemplateService.GetAllTemplatesAsync()
                    : await _gigTemplateService.GetTemplatesByCategoryAsync(category);

                Response.Headers["Cache-Control"] = "public, max-age=86400";

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving gig templates");
                return StatusCode(500, new { message = "Failed to retrieve gig templates", error = ex.Message });
            }
        }
    }
}
