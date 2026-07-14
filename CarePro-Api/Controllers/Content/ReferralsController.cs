using Application.DTOs;
using Application.Interfaces.Content;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CarePro_Api.Controllers.Content
{
    [ApiController]
    [Route("api/referrals")]
    [Authorize]
    public class ReferralsController : ControllerBase
    {
        private readonly IReferralService _referralService;

        public ReferralsController(IReferralService referralService)
        {
            _referralService = referralService;
        }

        [HttpPost("referrers")]
        [Authorize(Policy = "ReferralManagementPolicy")]
        public async Task<IActionResult> CreateReferrer([FromBody] CreateReferrerRequest request)
        {
            var result = await _referralService.CreateReferrerAsync(request);
            if (!result.IsSuccess)
                return BadRequest(new { success = false, message = string.Join(", ", result.Errors) });

            return Ok(new { success = true, data = result.Value });
        }

        [HttpPost("codes")]
        [Authorize(Policy = "ReferralManagementPolicy")]
        public async Task<IActionResult> CreateReferralCode([FromBody] CreateReferralCodeRequest request)
        {
            var result = await _referralService.CreateReferralCodeAsync(request.ReferrerId);
            if (!result.IsSuccess)
                return BadRequest(new { success = false, message = string.Join(", ", result.Errors) });

            return Ok(new { success = true, data = result.Value });
        }

        [HttpGet("referrers")]
        [Authorize(Policy = "ReferralManagementPolicy")]
        public async Task<IActionResult> GetReferrers()
        {
            var data = await _referralService.GetReferrersAsync();
            return Ok(new { success = true, data });
        }

        [HttpGet("referrers/list")]
        [Authorize(Policy = "ReferralManagementPolicy")]
        public async Task<IActionResult> GetReferrersList()
        {
            var data = await _referralService.GetReferrersAsync();
            return Ok(new { success = true, data });
        }

        [HttpOptions("referrers")]
        [Authorize(Policy = "ReferralManagementPolicy")]
        public IActionResult ReferrersOptions()
        {
            Response.Headers.Allow = "GET,POST,OPTIONS";
            return Ok();
        }

        [HttpGet("redemptions")]
        [Authorize(Policy = "ReferralManagementPolicy")]
        public async Task<IActionResult> GetRedemptions([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate)
        {
            var data = await _referralService.GetRedemptionsAsync(startDate, endDate);
            return Ok(new { success = true, data });
        }

        [HttpPost("redemptions/{redemptionId}/mark-paid")]
        [Authorize(Policy = "ReferralManagementPolicy")]
        public async Task<IActionResult> MarkRedemptionPaid(string redemptionId)
        {
            var result = await _referralService.MarkRedemptionPaidAsync(redemptionId);
            if (!result.IsSuccess)
                return BadRequest(new { success = false, message = string.Join(", ", result.Errors) });

            return Ok(new { success = true, message = "Redemption marked as paid." });
        }
    }
}
