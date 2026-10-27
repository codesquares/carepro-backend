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
    /// <summary>
    /// A client's own recurring package subscriptions — created when a Recurring admin payment link is paid
    /// (see <c>PackagePaymentService.CompleteRecurringPackagePaymentAsync</c>). Replaces the client-facing use of
    /// the Gig-based <c>api/subscriptions</c> endpoints. A client can only ever see or cancel their own.
    /// </summary>
    [Route("api/client/package-subscriptions")]
    [ApiController]
    [Authorize(Roles = "Client")]
    public class ClientPackageSubscriptionsController : ControllerBase
    {
        private readonly IPackageSubscriptionService _service;
        private readonly ILogger<ClientPackageSubscriptionsController> _logger;

        public ClientPackageSubscriptionsController(IPackageSubscriptionService service, ILogger<ClientPackageSubscriptionsController> logger)
        {
            _service = service;
            _logger = logger;
        }

        private string? CurrentClientId() =>
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value
            ?? User.FindFirst("userId")?.Value;

        [HttpGet]
        public async Task<IActionResult> List()
        {
            var clientId = CurrentClientId();
            if (string.IsNullOrEmpty(clientId))
                return Unauthorized(new { success = false, message = "User not authenticated." });

            var items = await _service.GetClientSubscriptionsAsync(clientId);
            return Ok(new { success = true, data = items, count = items.Count });
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> Get(string id)
        {
            var clientId = CurrentClientId();
            if (string.IsNullOrEmpty(clientId))
                return Unauthorized(new { success = false, message = "User not authenticated." });

            var item = await _service.GetClientSubscriptionAsync(clientId, id);
            if (item == null)
                return NotFound(new { success = false, message = "Subscription not found." });

            return Ok(new { success = true, data = item });
        }

        /// <summary>Stops auto-renewal. The period already paid for continues; nothing further is charged.</summary>
        [HttpPost("{id}/cancel")]
        public async Task<IActionResult> Cancel(string id, [FromBody] CancelPackageSubscriptionRequest? request)
        {
            var clientId = CurrentClientId();
            if (string.IsNullOrEmpty(clientId))
                return Unauthorized(new { success = false, message = "User not authenticated." });

            var result = await _service.CancelClientSubscriptionAsync(clientId, id, request?.Reason);
            if (!result.IsSuccess)
                return NotFound(new { success = false, message = string.Join(", ", result.Errors) });

            return Ok(new
            {
                success = true,
                message = "Auto-renewal stopped. Your current period continues and you will not be charged again.",
                data = result.Value
            });
        }
    }

    public class CancelPackageSubscriptionRequest
    {
        [System.ComponentModel.DataAnnotations.StringLength(500)]
        public string? Reason { get; set; }
    }
}
