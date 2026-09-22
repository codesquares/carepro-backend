using Application.Interfaces.Content;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace CarePro_Api.Controllers.Content
{
    /// <summary>
    /// Client-facing, read-only catalog of the pre-priced care packages (Phase 3).
    ///
    /// Every other Package endpoint lives on <c>api/admin/Packages</c> behind
    /// <c>OperationsPolicy</c>; this is the one surface a client can read so they can
    /// browse tiers and prices before raising a <c>PackageRequest</c>.
    ///
    /// Auth decision: <see cref="AuthorizeAttribute"/> with no role — any authenticated
    /// user (client, caregiver, admin) may read it. Not anonymous: package pricing is a
    /// commercial catalog and there is no product reason to expose it to unauthenticated
    /// scrapers (unlike the legacy gig marketplace). Not role-gated: a caregiver
    /// legitimately needs to see what packages exist, and gating adds no protection over
    /// "must be logged in".
    /// </summary>
    [Route("api/client/packages")]
    [ApiController]
    [Authorize]
    public class ClientPackagesController : ControllerBase
    {
        private readonly IPackageService _packageService;
        private readonly ILogger<ClientPackagesController> _logger;

        public ClientPackagesController(IPackageService packageService, ILogger<ClientPackagesController> logger)
        {
            _packageService = packageService;
            _logger = logger;
        }

        /// <summary>List every active care package (client projection — no operational internals).</summary>
        [HttpGet]
        public async Task<IActionResult> GetActivePackages()
        {
            try
            {
                var packages = await _packageService.GetActivePackagesForClientAsync();
                return Ok(new
                {
                    success = true,
                    data = packages,
                    count = packages.Count,
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving client package catalog");
                return StatusCode(500, new
                {
                    success = false,
                    message = "An error occurred while retrieving packages",
                });
            }
        }
    }
}
