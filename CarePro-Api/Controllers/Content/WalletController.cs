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
    public class WalletController : ControllerBase
    {
        private readonly ICaregiverWalletService _walletService;
        private readonly IEarningsLedgerService _ledgerService;

        public WalletController(
            ICaregiverWalletService walletService,
            IEarningsLedgerService ledgerService)
        {
            _walletService = walletService;
            _ledgerService = ledgerService;
        }

        /// <summary>
        /// Verify the authenticated user owns the requested resource or is an admin.
        /// Prevents IDOR attacks where one user accesses another's wallet.
        /// </summary>
        private bool IsAuthorizedForCaregiver(string caregiverId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User.FindFirst("sub")?.Value
                ?? User.FindFirst("userId")?.Value;
            var role = User.FindFirstValue(ClaimTypes.Role);
            return userId == caregiverId || role == "Admin" || role == "SuperAdmin";
        }

        /// <summary>
        /// Get wallet summary for a caregiver (balances + last updated).
        /// </summary>
        [HttpGet("summary/{caregiverId}")]
        public async Task<IActionResult> GetWalletSummary(string caregiverId)
        {
            if (!IsAuthorizedForCaregiver(caregiverId))
                return Forbid();

            try
            {
                var summary = await _walletService.GetWalletSummaryAsync(caregiverId);
                return Ok(summary);
            }
            catch (Exception ex)
            {
                return BadRequest(new { ErrorMessage = ex.Message });
            }
        }

        /// <summary>
        /// Get full ledger / transaction history for a caregiver.
        /// Returns all financial events (earnings, releases, withdrawals, refunds, disputes).
        /// </summary>
        [HttpGet("ledger/{caregiverId}")]
        public async Task<IActionResult> GetLedgerHistory(string caregiverId, [FromQuery] int? limit = null)
        {
            if (!IsAuthorizedForCaregiver(caregiverId))
                return Forbid();

            try
            {
                var history = await _ledgerService.GetLedgerHistoryAsync(caregiverId, limit);
                return Ok(history);
            }
            catch (Exception ex)
            {
                return BadRequest(new { ErrorMessage = ex.Message });
            }
        }

        /// <summary>
        /// Get transaction history formatted for display (compatible with old transaction history).
        /// </summary>
        [HttpGet("transactions/{caregiverId}")]
        public async Task<IActionResult> GetTransactionHistory(string caregiverId)
        {
            if (!IsAuthorizedForCaregiver(caregiverId))
                return Forbid();

            try
            {
                var transactions = await _ledgerService.GetTransactionHistoryAsync(caregiverId);
                return Ok(transactions);
            }
            catch (Exception ex)
            {
                return BadRequest(new { ErrorMessage = ex.Message });
            }
        }
    }
}
