using Application.DTOs;
using Application.Interfaces.Content;
using Domain.Entities;
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

            return Ok(new { success = true, data = ToReferrerDto(result.Value) });
        }

        [HttpPost("apply")]
        [AllowAnonymous]
        public async Task<IActionResult> ApplyForReferrer([FromBody] ApplyForReferrerRequest request)
        {
            var result = await _referralService.ApplyForReferrerAsync(request);
            if (!result.IsSuccess)
                return BadRequest(new { success = false, message = string.Join(", ", result.Errors) });

            return Ok(new { success = true, data = ToReferrerDto(result.Value) });
        }

        [HttpPost("referrers/{referrerId}/approve")]
        [Authorize(Policy = "ReferralManagementPolicy")]
        public async Task<IActionResult> ApproveReferrer(string referrerId)
        {
            var result = await _referralService.ApproveReferrerAsync(referrerId);
            if (!result.IsSuccess)
                return BadRequest(new { success = false, message = string.Join(", ", result.Errors) });

            return Ok(new { success = true, data = ToReferrerDto(result.Value) });
        }

        [HttpPost("referrers/{referrerId}/reject")]
        [Authorize(Policy = "ReferralManagementPolicy")]
        public async Task<IActionResult> RejectReferrer(string referrerId)
        {
            var result = await _referralService.RejectReferrerAsync(referrerId);
            if (!result.IsSuccess)
                return BadRequest(new { success = false, message = string.Join(", ", result.Errors) });

            return Ok(new { success = true, data = ToReferrerDto(result.Value) });
        }

        [HttpPost("codes")]
        [Authorize(Policy = "ReferralManagementPolicy")]
        public async Task<IActionResult> CreateReferralCode([FromBody] CreateReferralCodeRequest request)
        {
            var result = await _referralService.CreateReferralCodeAsync(request.ReferrerId);
            if (!result.IsSuccess)
                return BadRequest(new { success = false, message = string.Join(", ", result.Errors) });

            return Ok(new { success = true, data = ToReferralCodeDto(result.Value) });
        }

        [HttpGet("referrers")]
        [Authorize(Policy = "ReferralManagementPolicy")]
        public async Task<IActionResult> GetReferrers([FromQuery] string? status)
        {
            var data = await _referralService.GetReferrersAsync(status);
            return Ok(new { success = true, data });
        }

        [HttpGet("referrers/list")]
        [Authorize(Policy = "ReferralManagementPolicy")]
        public async Task<IActionResult> GetReferrersList([FromQuery] string? status)
        {
            var data = await _referralService.GetReferrersAsync(status);
            return Ok(new { success = true, data });
        }

        [HttpOptions("referrers")]
        [Authorize(Policy = "ReferralManagementPolicy")]
        public IActionResult ReferrersOptions()
        {
            Response.Headers.Allow = "GET,POST,OPTIONS";
            return Ok();
        }

        [HttpPost("codes/{referralCodeId}/send-email")]
        [Authorize(Policy = "ReferralManagementPolicy")]
        public async Task<IActionResult> SendReferralCodeEmail(string referralCodeId)
        {
            var result = await _referralService.SendReferralCodeEmailAsync(referralCodeId);
            if (!result.IsSuccess)
                return BadRequest(new { success = false, message = string.Join(", ", result.Errors) });

            return Ok(new { success = true, message = "Referral code email sent." });
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

        // MongoDB.Bson.ObjectId has no built-in System.Text.Json converter, so returning
        // a raw entity serializes Id as its internal struct fields (e.g. { timestamp,
        // creationTime }) instead of the hex string every client actually needs. These
        // two helpers guarantee a plain string id on every response that hands one back.
        private static object ToReferrerDto(Referrer r) => new
        {
            id = r.Id.ToString(),
            fullName = r.FullName,
            email = r.Email,
            phoneNo = r.PhoneNo,
            alias = r.Alias,
            status = r.Status,
            createdAt = r.CreatedAt
        };

        private static object ToReferralCodeDto(ReferralCode c) => new
        {
            id = c.Id.ToString(),
            code = c.Code,
            referrerId = c.ReferrerId,
            createdAt = c.CreatedAt,
            isActive = c.IsActive
        };
    }
}
