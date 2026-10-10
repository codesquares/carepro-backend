using Application.DTOs;
using Application.Interfaces.Content;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace CarePro_Api.Controllers.Content
{
    public class VettingNoticePreviewRequest
    {
        public string? Type { get; set; }
        public bool RetryFailed { get; set; }
    }

    public class VettingNoticeSendRequest
    {
        public string? Type { get; set; }
        /// <summary>Must equal the preview's recipientCount at the moment of sending, or the send is refused with 409.</summary>
        public int? ConfirmRecipientCount { get; set; }
        public bool RetryFailed { get; set; }
    }

    /// <summary>
    /// Admin-triggered vetting enforcement notice. OperationsPolicy only (same rule as the police clearance review): every other
    /// department and every non-admin gets 403. The admin identity comes from the JWT — there is no admin-id field in any request.
    /// Never invoked automatically: not on startup, not on a timer, not on a config change.
    /// </summary>
    [Route("api/admin/vetting-notice")]
    [ApiController]
    [Authorize(Policy = "OperationsPolicy")]
    public class AdminVettingNoticeController : ControllerBase
    {
        private readonly IVettingNoticeService _service;
        private readonly ILogger<AdminVettingNoticeController> _logger;

        public AdminVettingNoticeController(IVettingNoticeService service, ILogger<AdminVettingNoticeController> logger)
        {
            _service = service; _logger = logger;
        }

        private AdminIdentity CurrentAdmin() => new()
        {
            Id = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value ?? User.FindFirst("userId")?.Value ?? string.Empty,
            Email = User.FindFirst(ClaimTypes.Email)?.Value ?? User.FindFirst("email")?.Value ?? string.Empty
        };

        private IActionResult Handle(Exception ex, string action)
        {
            switch (ex)
            {
                case VettingNoticeValidationException v: return BadRequest(new { message = v.Message });
                case VettingNoticeConflictException c: return Conflict(new { message = c.Message });
                case UnauthorizedAccessException u: return StatusCode(403, new { message = u.Message });
                default:
                    _logger.LogError(ex, "Unexpected error during {Action}", action);
                    return StatusCode(500, new { message = "An unexpected error occurred." });
            }
        }

        /// <summary>Counts and a sample (first 5: first name + missing steps, no email addresses). Sends nothing.</summary>
        [HttpPost("preview")]
        public async Task<IActionResult> Preview([FromBody] VettingNoticePreviewRequest request)
        {
            try { return Ok(await _service.PreviewAsync(request?.Type ?? string.Empty, request?.RetryFailed ?? false)); }
            catch (Exception ex) { return Handle(ex, "VettingNoticePreview"); }
        }

        /// <summary>Sends the notice (email + in-app) to everyone the preview counted. Requires confirmRecipientCount == current count.</summary>
        [HttpPost("send")]
        public async Task<IActionResult> Send([FromBody] VettingNoticeSendRequest request)
        {
            if (request?.ConfirmRecipientCount == null)
                return BadRequest(new { message = "confirmRecipientCount is required (use the recipientCount from a preview)." });
            try { return Ok(await _service.SendAsync(request.Type ?? string.Empty, request.ConfirmRecipientCount.Value, request.RetryFailed, CurrentAdmin())); }
            catch (Exception ex) { return Handle(ex, "VettingNoticeSend"); }
        }
    }
}
