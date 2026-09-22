using Application.DTOs;
using Application.Interfaces.Content;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Threading.Tasks;

namespace CarePro_Api.Controllers.Content
{
    /// <summary>
    /// Admin-only: generates a real Flutterwave payment link for a specific client +
    /// package (Option A — no WhatsApp automation platform). Staff copy the returned
    /// link into WhatsApp manually; a successful payment creates the real
    /// PackageRequest via the webhook (see <see cref="PaymentsController"/>'s
    /// CAREPRO-PKG- route), not through this endpoint.
    /// Gated by OperationsPolicy, consistent with every other admin endpoint.
    /// </summary>
    [Route("api/admin/package-payments")]
    [ApiController]
    [Authorize(Policy = "OperationsPolicy")]
    public class AdminPackagePaymentsController : ControllerBase
    {
        private readonly IPackagePaymentService _packagePaymentService;

        public AdminPackagePaymentsController(IPackagePaymentService packagePaymentService)
        {
            _packagePaymentService = packagePaymentService;
        }

        private string? CurrentAdminId() =>
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value
            ?? User.FindFirst("userId")?.Value;

        /// <summary>Generates (or reuses a fresh, still-pending) payment link for the given client + package.</summary>
        [HttpPost("initiate")]
        public async Task<IActionResult> Initiate([FromBody] AdminInitiatePackagePaymentRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var result = await _packagePaymentService.InitiatePackagePaymentAsync(request, CurrentAdminId());

            if (!result.IsSuccess)
                return BadRequest(new { success = false, errors = result.Errors });

            return Ok(result.Value);
        }
    }
}
