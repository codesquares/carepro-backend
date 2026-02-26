using Application.DTOs;
using Application.Interfaces.Content;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CarePro_Api.Controllers.Content
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class CaregiverBankAccountController : ControllerBase
    {
        private readonly ICaregiverBankAccountService _bankAccountService;

        public CaregiverBankAccountController(ICaregiverBankAccountService bankAccountService)
        {
            _bankAccountService = bankAccountService;
        }

        /// <summary>
        /// Get bank account info for a caregiver. Caregivers can get their own; admins can get any.
        /// </summary>
        [HttpGet("{caregiverId}")]
        public async Task<IActionResult> GetBankAccount(string caregiverId)
        {
            try
            {
                var account = await _bankAccountService.GetBankAccountAsync(caregiverId);
                if (account == null)
                    return NotFound(new { ErrorMessage = "No bank account found for this caregiver." });

                return Ok(account);
            }
            catch (Exception ex)
            {
                return BadRequest(new { ErrorMessage = ex.Message });
            }
        }

        /// <summary>
        /// Create or update bank account info. Caregiver submits their own bank details.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CreateOrUpdateBankAccount([FromBody] CaregiverBankAccountRequest request)
        {
            try
            {
                var caregiverId = User.FindFirst("userId")?.Value
                    ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? User.FindFirst("sub")?.Value;

                if (string.IsNullOrEmpty(caregiverId))
                    return Unauthorized(new { ErrorMessage = "Unable to identify caregiver from token." });

                var result = await _bankAccountService.CreateOrUpdateBankAccountAsync(caregiverId, request);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { ErrorMessage = ex.Message });
            }
        }

        /// <summary>
        /// Update bank account info (same as POST - upsert). Provided for REST semantics.
        /// </summary>
        [HttpPut]
        public async Task<IActionResult> UpdateBankAccount([FromBody] CaregiverBankAccountRequest request)
        {
            try
            {
                var caregiverId = User.FindFirst("userId")?.Value
                    ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? User.FindFirst("sub")?.Value;

                if (string.IsNullOrEmpty(caregiverId))
                    return Unauthorized(new { ErrorMessage = "Unable to identify caregiver from token." });

                var result = await _bankAccountService.CreateOrUpdateBankAccountAsync(caregiverId, request);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { ErrorMessage = ex.Message });
            }
        }

        /// <summary>
        /// Admin-only: Get full financial summary (wallet + bank account) for a caregiver.
        /// </summary>
        [HttpGet("{caregiverId}/financial-summary")]
        // [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> GetFinancialSummary(string caregiverId)
        {
            try
            {
                var summary = await _bankAccountService.GetAdminCaregiverFinancialSummaryAsync(caregiverId);
                return Ok(summary);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { ErrorMessage = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { ErrorMessage = ex.Message });
            }
        }
    }
}
