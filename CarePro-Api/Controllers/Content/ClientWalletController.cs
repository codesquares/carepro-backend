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
    public class ClientWalletController : ControllerBase
    {
        private readonly IClientWalletService _walletService;
        private readonly ILogger<ClientWalletController> _logger;

        public ClientWalletController(IClientWalletService walletService, ILogger<ClientWalletController> logger)
        {
            _walletService = walletService;
            _logger = logger;
        }

        /// <summary>
        /// Get the current client's wallet (credit balance, totals).
        /// </summary>
        [HttpGet]
        [Authorize(Roles = "Client, Admin, SuperAdmin")]
        public async Task<IActionResult> GetWallet()
        {
            try
            {
                var clientId = GetCurrentUserId();
                if (string.IsNullOrEmpty(clientId))
                    return Unauthorized(new { error = "Client authorization required." });

                var wallet = await _walletService.GetOrCreateWalletAsync(clientId);
                return Ok(wallet);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving client wallet");
                return StatusCode(500, new { error = "An error occurred while retrieving the wallet." });
            }
        }

        private string? GetCurrentUserId() =>
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirst("sub")?.Value
            ?? User.FindFirst("userId")?.Value;
    }
}
