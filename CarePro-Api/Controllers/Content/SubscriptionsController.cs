using Application.DTOs;
using Application.Interfaces.Content;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CarePro_Api.Controllers.Content
{
    [ApiController]
    [Route("api/subscriptions")]
    [Authorize]
    public class SubscriptionsController : ControllerBase
    {
        private readonly ISubscriptionService _subscriptionService;
        private readonly ILogger<SubscriptionsController> _logger;

        public SubscriptionsController(
            ISubscriptionService subscriptionService,
            ILogger<SubscriptionsController> logger)
        {
            _subscriptionService = subscriptionService;
            _logger = logger;
        }

        private string? GetUserId() =>
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value
            ?? User.FindFirst("userId")?.Value;

        // ══════════════════════════════════════════
        //  SUBSCRIPTION QUERIES
        // ══════════════════════════════════════════

        /// <summary>
        /// Get a specific subscription by ID
        /// </summary>
        [HttpGet("{subscriptionId}")]
        public async Task<IActionResult> GetSubscription(string subscriptionId)
        {
            var subscription = await _subscriptionService.GetSubscriptionByIdAsync(subscriptionId);
            if (subscription == null)
                return NotFound(new { success = false, message = "Subscription not found." });

            return Ok(new { success = true, data = subscription });
        }

        /// <summary>
        /// Get subscription linked to a specific order
        /// </summary>
        [HttpGet("by-order/{orderId}")]
        public async Task<IActionResult> GetSubscriptionByOrder(string orderId)
        {
            var subscription = await _subscriptionService.GetSubscriptionByOrderIdAsync(orderId);
            if (subscription == null)
                return NotFound(new { success = false, message = "No subscription found for this order." });

            return Ok(new { success = true, data = subscription });
        }

        /// <summary>
        /// Get all subscriptions for the authenticated client
        /// </summary>
        [HttpGet("client")]
        public async Task<IActionResult> GetClientSubscriptions()
        {
            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { success = false, message = "User not authenticated." });

            var subscriptions = await _subscriptionService.GetClientSubscriptionsAsync(userId);
            return Ok(new { success = true, data = subscriptions });
        }

        /// <summary>
        /// Get subscription summary/dashboard for the authenticated client
        /// </summary>
        [HttpGet("client/summary")]
        public async Task<IActionResult> GetClientSubscriptionSummary()
        {
            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { success = false, message = "User not authenticated." });

            var summary = await _subscriptionService.GetClientSubscriptionSummaryAsync(userId);
            return Ok(new { success = true, data = summary });
        }

        /// <summary>
        /// Get all subscriptions for the authenticated caregiver
        /// </summary>
        [HttpGet("caregiver")]
        public async Task<IActionResult> GetCaregiverSubscriptions()
        {
            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { success = false, message = "User not authenticated." });

            var subscriptions = await _subscriptionService.GetCaregiverSubscriptionsAsync(userId);
            return Ok(new { success = true, data = subscriptions });
        }

        // ══════════════════════════════════════════
        //  CANCELLATION & TERMINATION
        // ══════════════════════════════════════════

        /// <summary>
        /// Cancel a subscription at the end of the current billing period.
        /// Service continues until the period ends, then stops.
        /// </summary>
        [HttpPost("{subscriptionId}/cancel")]
        public async Task<IActionResult> CancelSubscription(
            string subscriptionId, [FromBody] CancelSubscriptionRequest request)
        {
            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { success = false, message = "User not authenticated." });

            var result = await _subscriptionService.CancelSubscriptionAsync(subscriptionId, userId, request);

            if (!result.IsSuccess)
                return BadRequest(new { success = false, message = string.Join(", ", result.Errors) });

            return Ok(new { success = true, message = "Subscription will be cancelled at the end of the current billing period.", data = result.Value });
        }

        /// <summary>
        /// Reverse a pending cancellation and re-enable auto-renewal.
        /// Only works if the current billing period hasn't ended yet.
        /// </summary>
        [HttpPost("{subscriptionId}/reactivate")]
        public async Task<IActionResult> ReactivateSubscription(string subscriptionId)
        {
            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { success = false, message = "User not authenticated." });

            var result = await _subscriptionService.ReactivateSubscriptionAsync(subscriptionId, userId);

            if (!result.IsSuccess)
                return BadRequest(new { success = false, message = string.Join(", ", result.Errors) });

            return Ok(new { success = true, message = "Subscription reactivated successfully.", data = result.Value });
        }

        /// <summary>
        /// Immediately terminate a subscription with optional pro-rated refund.
        /// Service stops immediately. Use for disputes or urgent cancellations.
        /// </summary>
        [HttpPost("{subscriptionId}/terminate")]
        public async Task<IActionResult> TerminateSubscription(
            string subscriptionId, [FromBody] TerminateSubscriptionRequest request)
        {
            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { success = false, message = "User not authenticated." });

            var result = await _subscriptionService.TerminateSubscriptionAsync(subscriptionId, userId, request);

            if (!result.IsSuccess)
                return BadRequest(new { success = false, message = string.Join(", ", result.Errors) });

            return Ok(new { success = true, message = "Subscription terminated.", data = result.Value });
        }

        // ══════════════════════════════════════════
        //  PLAN CHANGES
        // ══════════════════════════════════════════

        /// <summary>
        /// Change the subscription plan (upgrade/downgrade).
        /// Changes take effect at the start of the next billing cycle.
        /// </summary>
        [HttpPut("{subscriptionId}/plan")]
        public async Task<IActionResult> ChangePlan(
            string subscriptionId, [FromBody] ChangePlanRequest request)
        {
            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { success = false, message = "User not authenticated." });

            var result = await _subscriptionService.ChangePlanAsync(subscriptionId, userId, request);

            if (!result.IsSuccess)
                return BadRequest(new { success = false, message = string.Join(", ", result.Errors) });

            return Ok(new { success = true, data = result.Value });
        }

        /// <summary>
        /// Get plan change history for a subscription
        /// </summary>
        [HttpGet("{subscriptionId}/plan-history")]
        public async Task<IActionResult> GetPlanChangeHistory(string subscriptionId)
        {
            var history = await _subscriptionService.GetPlanChangeHistoryAsync(subscriptionId);
            return Ok(new { success = true, data = history });
        }

        // ══════════════════════════════════════════
        //  PAYMENT METHOD
        // ══════════════════════════════════════════

        /// <summary>
        /// Initiate payment method update. Returns a Flutterwave link for card authorization.
        /// </summary>
        [HttpPost("{subscriptionId}/payment-method")]
        public async Task<IActionResult> UpdatePaymentMethod(
            string subscriptionId, [FromBody] UpdatePaymentMethodRequest request)
        {
            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { success = false, message = "User not authenticated." });

            var result = await _subscriptionService.InitiatePaymentMethodUpdateAsync(subscriptionId, userId, request);

            if (!result.IsSuccess)
                return BadRequest(new { success = false, message = string.Join(", ", result.Errors) });

            return Ok(new { success = true, data = result.Value });
        }

        // ══════════════════════════════════════════
        //  PAUSE / RESUME
        // ══════════════════════════════════════════

        /// <summary>
        /// Pause a subscription. No charges during pause.
        /// </summary>
        [HttpPost("{subscriptionId}/pause")]
        public async Task<IActionResult> PauseSubscription(
            string subscriptionId, [FromBody] PauseSubscriptionRequest request)
        {
            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { success = false, message = "User not authenticated." });

            var result = await _subscriptionService.PauseSubscriptionAsync(subscriptionId, userId, request);

            if (!result.IsSuccess)
                return BadRequest(new { success = false, message = string.Join(", ", result.Errors) });

            return Ok(new { success = true, message = "Subscription paused.", data = result.Value });
        }

        /// <summary>
        /// Resume a paused subscription. A new billing period starts immediately.
        /// </summary>
        [HttpPost("{subscriptionId}/resume")]
        public async Task<IActionResult> ResumeSubscription(string subscriptionId)
        {
            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { success = false, message = "User not authenticated." });

            var result = await _subscriptionService.ResumeSubscriptionAsync(subscriptionId, userId);

            if (!result.IsSuccess)
                return BadRequest(new { success = false, message = string.Join(", ", result.Errors) });

            return Ok(new { success = true, message = "Subscription resumed.", data = result.Value });
        }

        // ══════════════════════════════════════════
        //  BILLING HISTORY
        // ══════════════════════════════════════════

        /// <summary>
        /// Get all payment records for a subscription
        /// </summary>
        [HttpGet("{subscriptionId}/payments")]
        public async Task<IActionResult> GetPaymentHistory(string subscriptionId)
        {
            var history = await _subscriptionService.GetPaymentHistoryAsync(subscriptionId);
            return Ok(new { success = true, data = history });
        }

        // ══════════════════════════════════════════
        //  ADMIN ENDPOINTS
        // ══════════════════════════════════════════

        /// <summary>
        /// Admin: Get subscription analytics/metrics
        /// </summary>
        [HttpGet("admin/analytics")]
        public async Task<IActionResult> GetAnalytics()
        {
            var analytics = await _subscriptionService.GetSubscriptionAnalyticsAsync();
            return Ok(new { success = true, data = analytics });
        }

        /// <summary>
        /// Admin: Get all subscriptions with optional status filter
        /// </summary>
        [HttpGet("admin/all")]
        public async Task<IActionResult> GetAllSubscriptions([FromQuery] string? status = null)
        {
            var subscriptions = await _subscriptionService.GetAllSubscriptionsAsync(status);
            return Ok(new { success = true, data = subscriptions });
        }

        /// <summary>
        /// Admin: Terminate a subscription (can terminate any subscription)
        /// </summary>
        [HttpPost("admin/{subscriptionId}/terminate")]
        public async Task<IActionResult> AdminTerminateSubscription(
            string subscriptionId, [FromBody] TerminateSubscriptionRequest request)
        {
            // For admin termination, we pass a special admin context
            // In production, verify admin role from JWT claims
            var result = await _subscriptionService.TerminateSubscriptionAsync(subscriptionId, "admin", request);

            if (!result.IsSuccess)
                return BadRequest(new { success = false, message = string.Join(", ", result.Errors) });

            return Ok(new { success = true, message = "Subscription terminated by admin.", data = result.Value });
        }
    }
}
