using Application.DTOs;
using Application.Interfaces.Content;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;

namespace CarePro_Api.Controllers.Content
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class WithdrawalRequestsController : ControllerBase
    {
        private readonly IWithdrawalRequestService _withdrawalRequestService;

        public WithdrawalRequestsController(IWithdrawalRequestService withdrawalRequestService)
        {
            _withdrawalRequestService = withdrawalRequestService;
        }

        [HttpGet("{withdrawalRequestId}")]
        public async Task<IActionResult> GetWithdrawalRequestById(string withdrawalRequestId)
        {
            try
            {
                var withdrawal = await _withdrawalRequestService.GetWithdrawalRequestByIdAsync(withdrawalRequestId);
                if (withdrawal == null)
                    return NotFound("Withdrawal request not found");

                if (!CanAccessCaregiverScope(withdrawal.CaregiverId))
                    return Forbid();

                return Ok(withdrawal);
            }
            catch (Exception ex)
            {
                return BadRequest(new { ErrorMessage = ex.Message });
            }
        }

        [HttpGet("token/{token}")]
        [Authorize(Policy = "FinancePolicy")]
        public async Task<IActionResult> GetWithdrawalRequestByToken(string token)
        {
            try
            {
                var withdrawal = await _withdrawalRequestService.GetWithdrawalRequestByTokenAsync(token);
                if (withdrawal == null)
                    return NotFound("Withdrawal request not found");

                return Ok(withdrawal);
            }
            catch (Exception ex)
            {
                return BadRequest(new { ErrorMessage = ex.Message });
            }
        }

        [HttpGet]
        [Authorize(Policy = "FinancePolicy")]
        public async Task<IActionResult> GetAllWithdrawalRequests()
        {
            try
            {
                var withdrawals = await _withdrawalRequestService.GetAllWithdrawalRequestsAsync();
                return Ok(withdrawals);
            }
            catch (Exception ex)
            {
                return BadRequest(new { ErrorMessage = ex.Message });
            }
        }

        [HttpGet("caregiver/{caregiverId}")]
        public async Task<IActionResult> GetWithdrawalRequestsByCaregiverId(string caregiverId)
        {
            try
            {
                if (!CanAccessCaregiverScope(caregiverId))
                    return Forbid();

                var withdrawals = await _withdrawalRequestService.GetWithdrawalRequestsByCaregiverIdAsync(caregiverId);
                return Ok(withdrawals);
            }
            catch (Exception ex)
            {
                return BadRequest(new { ErrorMessage = ex.Message });
            }
        }


        [HttpGet("caregiver-withdrawal-history/{caregiverId}")]
        public async Task<IActionResult> GetCaregiverWithdrawalRequestHistoryAsync(string caregiverId)
        {
            try
            {
                if (!CanAccessCaregiverScope(caregiverId))
                    return Forbid();

                var withdrawals = await _withdrawalRequestService.GetCaregiverWithdrawalRequestHistoryAsync(caregiverId);
                return Ok(withdrawals);
            }
            catch (Exception ex)
            {
                return BadRequest(new { ErrorMessage = ex.Message });
            }
        }



        [HttpGet("status/{status}")]
        [Authorize(Policy = "FinancePolicy")]
        public async Task<IActionResult> GetWithdrawalRequestsByStatus(string status)
        {
            try
            {
                var withdrawals = await _withdrawalRequestService.GetWithdrawalRequestsByStatusAsync(status);
                return Ok(withdrawals);
            }
            catch (Exception ex)
            {
                return BadRequest(new { ErrorMessage = ex.Message });
            }
        }

        [HttpPost]
        [Authorize(Roles = "Caregiver")]
        public async Task<IActionResult> CreateWithdrawalRequest([FromBody] CreateWithdrawalRequestRequest request)
        {
            try
            {
                var userId = GetCurrentUserId();
                if (string.IsNullOrEmpty(userId))
                    return Unauthorized(new { ErrorMessage = "Unable to identify caregiver from token." });

                request.CaregiverId = userId;

                var withdrawal = await _withdrawalRequestService.CreateWithdrawalRequestAsync(request);
                //return CreatedAtAction(nameof(GetWithdrawalRequestById), new { id = withdrawal.Id }, withdrawal);
                return Ok(withdrawal);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { ErrorMessage = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { ErrorMessage = ex.Message });
            }
        }


        [HttpGet("TotalAmountEarnedAndWithdrawn/{caregiverId}")]
        public async Task<IActionResult> GetTotalAmountEarnedAndWithdrawnByCaregiverIdAsync(string caregiverId)
        {
            try
            {
                if (!CanAccessCaregiverScope(caregiverId))
                    return Forbid();

                var withdrawals = await _withdrawalRequestService.GetTotalAmountEarnedAndWithdrawnByCaregiverIdAsync(caregiverId);
                return Ok(withdrawals);
            }
            catch (Exception ex)
            {
                return BadRequest(new { ErrorMessage = ex.Message });
            }
        }



        [HttpPost("verify")]
        [Authorize(Policy = "FinancePolicy")]
        public async Task<IActionResult> VerifyWithdrawalRequest([FromBody] AdminWithdrawalVerificationRequest request)
        {
            try
            {
                // Ensure admin withdrawalRequestId in the request matches the authenticated user
                string adminId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? User.FindFirst("sub")?.Value
                    ?? User.FindFirst("userId")?.Value;
                request.AdminId = adminId;

                var withdrawal = await _withdrawalRequestService.VerifyWithdrawalRequestAsync(request);
                return Ok(withdrawal);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { ErrorMessage = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { ErrorMessage = ex.Message });
            }
        }

        [HttpPost("complete/{token}")]
        [Authorize(Policy = "FinancePolicy")]
        public async Task<IActionResult> CompleteWithdrawalRequest(string token)
        {
            try
            {
                string adminId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? User.FindFirst("sub")?.Value
                    ?? User.FindFirst("userId")?.Value;
                var withdrawal = await _withdrawalRequestService.CompleteWithdrawalRequestAsync(token, adminId);
                return Ok(withdrawal);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { ErrorMessage = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { ErrorMessage = ex.Message });
            }
        }

        [HttpPost("complete")]
        [Authorize(Policy = "FinancePolicy")]
        public async Task<IActionResult> CompleteWithdrawalRequestByBody([FromBody] AdminWithdrawalVerificationRequest request)
        {
            try
            {
                string adminId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? User.FindFirst("sub")?.Value
                    ?? User.FindFirst("userId")?.Value;
                request.AdminId = adminId;
                var withdrawal = await _withdrawalRequestService.CompleteWithdrawalRequestAsync(request);
                return Ok(withdrawal);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { ErrorMessage = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { ErrorMessage = ex.Message });
            }
        }

        [HttpPost("reject")]
        [Authorize(Policy = "FinancePolicy")]
        public async Task<IActionResult> RejectWithdrawalRequest([FromBody] AdminWithdrawalVerificationRequest request)
        {
            try
            {
                string adminId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? User.FindFirst("sub")?.Value
                    ?? User.FindFirst("userId")?.Value;
                request.AdminId = adminId;

                var withdrawal = await _withdrawalRequestService.RejectWithdrawalRequestAsync(request);
                return Ok(withdrawal);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { ErrorMessage = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { ErrorMessage = ex.Message });
            }
        }

        [HttpGet("has-pending/{caregiverId}")]
        [Authorize(Roles = "Caregiver")]
        public async Task<IActionResult> HasPendingWithdrawalRequest(string caregiverId)
        {
            try
            {
                // Verify that the user is checking their own status
                string userId = GetCurrentUserId();
                if (userId != caregiverId)
                    return Forbid();

                bool hasPending = await _withdrawalRequestService.HasPendingRequest(caregiverId);
                return Ok(new { HasPendingRequest = hasPending });
            }
            catch (Exception ex)
            {
                return BadRequest(new { ErrorMessage = ex.Message });
            }
        }

        private string? GetCurrentUserId() =>
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirst("sub")?.Value
            ?? User.FindFirst("userId")?.Value;

        private bool CanAccessCaregiverScope(string caregiverId)
        {
            var userId = GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return false;
            }

            return userId == caregiverId || User.IsInRole("Admin") || User.IsInRole("SuperAdmin");
        }
    }
}
