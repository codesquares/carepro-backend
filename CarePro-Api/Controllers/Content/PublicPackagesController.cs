using Application.Interfaces.Content;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace CarePro_Api.Controllers.Content
{
    /// <summary>
    /// Anonymous, price-free view of the care-package catalog for the public homepage.
    ///
    /// Deliberately a separate controller and DTO from <see cref="ClientPackagesController"/>: that
    /// endpoint stays <c>[Authorize]</c> because pricing is a commercial catalog not to be exposed to
    /// unauthenticated scrapers. This one returns only category / tier / description / caregiver type —
    /// never a price or any payroll field — so exposing it anonymously leaks nothing that endpoint protects.
    /// </summary>
    [Route("api/public/packages")]
    [ApiController]
    [AllowAnonymous]
    public class PublicPackagesController : ControllerBase
    {
        private readonly IPackageService _packageService;
        private readonly ILogger<PublicPackagesController> _logger;

        public PublicPackagesController(IPackageService packageService, ILogger<PublicPackagesController> logger)
        {
            _packageService = packageService;
            _logger = logger;
        }

        /// <summary>List every active package as a price-free summary.</summary>
        [HttpGet]
        public async Task<IActionResult> GetPublicPackages()
        {
            try
            {
                var packages = await _packageService.GetActivePackagesPublicAsync();
                Response.Headers["Cache-Control"] = "public, max-age=60";
                return Ok(new { success = true, data = packages, count = packages.Count });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving public package summaries");
                return StatusCode(500, new { success = false, message = "An error occurred while retrieving packages" });
            }
        }
    }
}
