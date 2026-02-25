using Application.Interfaces.Content;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CarePro_Api.Controllers.Content
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class BillingRecordsController : ControllerBase
    {
        private readonly IBillingRecordService _billingRecordService;

        public BillingRecordsController(IBillingRecordService billingRecordService)
        {
            _billingRecordService = billingRecordService;
        }

        /// <summary>
        /// Verify the authenticated user is the given user or is an admin.
        /// Prevents IDOR attacks where one user accesses another's billing records.
        /// </summary>
        private bool IsAuthorizedForUser(string userId)
        {
            var authenticatedUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var role = User.FindFirstValue(ClaimTypes.Role);
            return authenticatedUserId == userId || role == "Admin" || role == "SuperAdmin";
        }

        /// <summary>
        /// Get a specific billing record by ID. Admin only.
        /// </summary>
        [HttpGet("{id}")]
        public async Task<IActionResult> GetBillingRecordById(string id)
        {
            try
            {
                var record = await _billingRecordService.GetBillingRecordByIdAsync(id);
                if (record == null)
                    return NotFound("Billing record not found");

                // IDOR: verify caller is either the client or the caregiver on this record
                var authenticatedUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                var role = User.FindFirstValue(ClaimTypes.Role);
                if (authenticatedUserId != record.CaregiverId && authenticatedUserId != record.ClientId
                    && role != "Admin" && role != "SuperAdmin")
                    return Forbid();

                return Ok(record);
            }
            catch (Exception ex)
            {
                return BadRequest(new { ErrorMessage = ex.Message });
            }
        }

        /// <summary>
        /// Get the billing record for a specific order.
        /// </summary>
        [HttpGet("order/{orderId}")]
        public async Task<IActionResult> GetBillingRecordByOrderId(string orderId)
        {
            try
            {
                var record = await _billingRecordService.GetBillingRecordByOrderIdAsync(orderId);
                if (record == null)
                    return NotFound("Billing record not found for this order");

                // IDOR check
                var authenticatedUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                var role = User.FindFirstValue(ClaimTypes.Role);
                if (authenticatedUserId != record.CaregiverId && authenticatedUserId != record.ClientId
                    && role != "Admin" && role != "SuperAdmin")
                    return Forbid();

                return Ok(record);
            }
            catch (Exception ex)
            {
                return BadRequest(new { ErrorMessage = ex.Message });
            }
        }

        /// <summary>
        /// Get all billing records for a subscription (shows all billing cycles).
        /// </summary>
        [HttpGet("subscription/{subscriptionId}")]
        public async Task<IActionResult> GetBillingRecordsBySubscription(string subscriptionId)
        {
            try
            {
                var records = await _billingRecordService.GetBillingRecordsBySubscriptionIdAsync(subscriptionId);

                // IDOR: verify caller is participant on at least one record
                if (records.Any())
                {
                    var first = records.First();
                    var authenticatedUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                    var role = User.FindFirstValue(ClaimTypes.Role);
                    if (authenticatedUserId != first.CaregiverId && authenticatedUserId != first.ClientId
                        && role != "Admin" && role != "SuperAdmin")
                        return Forbid();
                }

                return Ok(records);
            }
            catch (Exception ex)
            {
                return BadRequest(new { ErrorMessage = ex.Message });
            }
        }

        /// <summary>
        /// Get all billing records for a client (payment receipts).
        /// </summary>
        [HttpGet("client/{clientId}")]
        public async Task<IActionResult> GetClientBillingRecords(string clientId)
        {
            if (!IsAuthorizedForUser(clientId))
                return Forbid();

            try
            {
                var records = await _billingRecordService.GetClientBillingRecordsAsync(clientId);
                return Ok(records);
            }
            catch (Exception ex)
            {
                return BadRequest(new { ErrorMessage = ex.Message });
            }
        }

        /// <summary>
        /// Get all billing records for a caregiver (income receipts).
        /// </summary>
        [HttpGet("caregiver/{caregiverId}")]
        public async Task<IActionResult> GetCaregiverBillingRecords(string caregiverId)
        {
            if (!IsAuthorizedForUser(caregiverId))
                return Forbid();

            try
            {
                var records = await _billingRecordService.GetCaregiverBillingRecordsAsync(caregiverId);
                return Ok(records);
            }
            catch (Exception ex)
            {
                return BadRequest(new { ErrorMessage = ex.Message });
            }
        }
    }
}
