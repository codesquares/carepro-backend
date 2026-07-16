using Application.DTOs;
using Application.Interfaces.Content;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using System.Security.Claims;

namespace CarePro_Api.Controllers.Content
{
    [ApiController]
    [Route("api/client-onboarding")]
    [Authorize(Roles = "Client")]
    public class ClientOnboardingController : ControllerBase
    {
        private readonly IClientOnboardingService _clientOnboardingService;
        private readonly ILogger<ClientOnboardingController> _logger;
        private readonly IHostEnvironment _hostEnvironment;

        public ClientOnboardingController(
            IClientOnboardingService clientOnboardingService,
            ILogger<ClientOnboardingController> logger,
            IHostEnvironment hostEnvironment)
        {
            _clientOnboardingService = clientOnboardingService;
            _logger = logger;
            _hostEnvironment = hostEnvironment;
        }

        [HttpGet("state")]
        public async Task<IActionResult> GetState()
        {
            try
            {
                var clientId = GetCurrentUserId();
                if (string.IsNullOrWhiteSpace(clientId))
                {
                    return Unauthorized();
                }

                var response = await _clientOnboardingService.GetStateAsync(clientId);
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve onboarding state.");
                return StatusCode(500, new { message = "An error occurred while retrieving onboarding state." });
            }
        }

        [HttpPatch("walkthrough")]
        public async Task<IActionResult> PatchWalkthrough([FromBody] PatchClientOnboardingWalkthroughRequest request)
        {
            try
            {
                var clientId = GetCurrentUserId();
                if (string.IsNullOrWhiteSpace(clientId))
                {
                    return Unauthorized();
                }

                var result = await _clientOnboardingService.PatchWalkthroughAsync(clientId, request);
                if (result.IsConflict)
                {
                    return Conflict(new ClientOnboardingConflictResponse
                    {
                        ErrorCode = result.ErrorCode ?? "VERSION_CONFLICT",
                        Message = result.ErrorMessage ?? "Submitted version is stale.",
                        LatestState = result.State
                    });
                }

                return Ok(result.State);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update onboarding walkthrough.");
                return StatusCode(500, new { message = "An error occurred while updating onboarding walkthrough." });
            }
        }

        [HttpPost("tips/seen")]
        public async Task<IActionResult> MarkTipSeen([FromBody] MarkClientOnboardingTipSeenRequest request)
        {
            try
            {
                var clientId = GetCurrentUserId();
                if (string.IsNullOrWhiteSpace(clientId))
                {
                    return Unauthorized();
                }

                var response = await _clientOnboardingService.MarkTipSeenAsync(clientId, request);
                return Ok(response);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to upsert seen tip.");
                return StatusCode(500, new { message = "An error occurred while recording seen tip." });
            }
        }

        [HttpPost("state/reset")]
        [ApiExplorerSettings(IgnoreApi = true)]
        [Authorize]
        public async Task<IActionResult> ResetState()
        {
            try
            {
                if (_hostEnvironment.IsProduction())
                {
                    return NotFound();
                }

                var clientId = GetCurrentUserId();
                if (string.IsNullOrWhiteSpace(clientId))
                {
                    return Unauthorized();
                }

                if (!User.IsInRole("Client"))
                {
                    return Forbid();
                }

                var hasQaRole = User.IsInRole("QA") || User.IsInRole("TestAutomation");
                var hasQaFlag = string.Equals(User.FindFirst("qa_access")?.Value, "true", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(User.FindFirst("onboarding_reset_access")?.Value, "true", StringComparison.OrdinalIgnoreCase);

                if (!hasQaRole && !hasQaFlag)
                {
                    return Forbid();
                }

                var state = await _clientOnboardingService.ResetStateAsync(clientId);
                return Ok(state);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reset onboarding state.");
                return StatusCode(500, new { message = "An error occurred while resetting onboarding state." });
            }
        }

        private string? GetCurrentUserId()
        {
            return User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User.FindFirst("sub")?.Value
                ?? User.FindFirst("userId")?.Value;
        }
    }
}