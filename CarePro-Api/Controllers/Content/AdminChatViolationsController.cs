using Application.Interfaces.Content;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CarePro_Api.Controllers.Content
{
    [Route("api/admin/chat-violations")]
    [ApiController]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public class AdminChatViolationsController : ControllerBase
    {
        private readonly IChatComplianceService _chatComplianceService;

        public AdminChatViolationsController(IChatComplianceService chatComplianceService)
        {
            _chatComplianceService = chatComplianceService;
        }

        [HttpGet]
        public async Task<IActionResult> GetViolations(
            [FromQuery] int skip = 0,
            [FromQuery] int take = 20,
            [FromQuery] string? userId = null,
            [FromQuery] string? violationType = null)
        {
            var violations = await _chatComplianceService.GetViolationsAsync(skip, take, userId, violationType);
            return Ok(violations);
        }

        [HttpGet("repeat-offenders")]
        public async Task<IActionResult> GetRepeatOffenders(
            [FromQuery] int minViolations = 3,
            [FromQuery] int days = 30)
        {
            var offenders = await _chatComplianceService.GetRepeatOffendersAsync(minViolations, days);
            return Ok(offenders);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetViolation(string id)
        {
            var violation = await _chatComplianceService.GetViolationByIdAsync(id);
            if (violation == null)
                return NotFound(new { error = "Violation not found" });

            return Ok(violation);
        }
    }
}
