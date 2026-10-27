using Application.DTOs;
using Application.Interfaces.Content;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CarePro_Api.Controllers.Content
{
    /// <summary>
    /// Admin review of package-assignment visits and their GPS check-in flags. Check-ins are never blocked for
    /// location; humans review the flagged ones here. Admin identity always comes from the JWT.
    /// </summary>
    [Route("api/admin/package-visits")]
    [ApiController]
    [Authorize(Policy = "FinanceOrOperationsPolicy")]
    public class AdminPackageVisitsController : ControllerBase
    {
        private readonly IPackageVisitAdminService _service;
        private readonly ILogger<AdminPackageVisitsController> _logger;

        public AdminPackageVisitsController(IPackageVisitAdminService service, ILogger<AdminPackageVisitsController> logger)
        {
            _service = service;
            _logger = logger;
        }

        private (string? id, string email) CurrentAdmin() => (
            User.FindFirst("userId")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value,
            User.FindFirst(ClaimTypes.Email)?.Value ?? User.FindFirst("email")?.Value ?? string.Empty);

        /// <summary>GET api/admin/package-visits?flagged=hard|soft|any&amp;unclearedOnly=true&amp;from=&amp;to=&amp;caregiverId=</summary>
        [HttpGet]
        public async Task<IActionResult> List([FromQuery] string? flagged, [FromQuery] bool unclearedOnly = false,
            [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null, [FromQuery] string? caregiverId = null, [FromQuery] int limit = 200)
        {
            try
            {
                var rows = await _service.ListAsync(new PackageVisitAdminQuery
                {
                    Flagged = flagged, UnclearedOnly = unclearedOnly, From = from, To = to, CaregiverId = caregiverId, Limit = limit
                });
                return Ok(new { success = true, data = rows });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Listing package visits failed");
                return StatusCode(500, new { message = "An unexpected error occurred." });
            }
        }

        [HttpPost("{checkinId}/clear-flag")]
        public async Task<IActionResult> ClearFlag(string checkinId, [FromBody] ClearVisitFlagRequest request)
        {
            if (request == null || !ModelState.IsValid) return BadRequest(new { message = "A note (at least 3 characters) is required to clear a flag." });
            var (adminId, email) = CurrentAdmin();
            if (string.IsNullOrEmpty(adminId)) return Unauthorized(new { message = "Unable to identify admin user." });
            try
            {
                var row = await _service.ClearFlagAsync(checkinId, adminId, email, request.Note);
                return Ok(new { success = true, data = row });
            }
            catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
            catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Clearing check-in flag failed");
                return StatusCode(500, new { message = "An unexpected error occurred." });
            }
        }
    }
}
