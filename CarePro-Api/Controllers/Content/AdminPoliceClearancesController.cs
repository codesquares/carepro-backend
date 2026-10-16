using Application.DTOs;
using Application.Interfaces.Content;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;

namespace CarePro_Api.Controllers.Content
{
    /// <summary>
    /// Staff review of police clearances. OperationsPolicy only (SuperAdmin, or Admin in HR / ComplianceAndLegal / CareLeads):
    /// every other department — and every non-admin — gets 403 on every route here. The reviewer id/email come from the JWT; request
    /// bodies have no admin-id field.
    /// </summary>
    [Route("api/admin/police-clearances")]
    [ApiController]
    [Authorize(Policy = "OperationsPolicy")]
    public class AdminPoliceClearancesController : ControllerBase
    {
        private readonly IPoliceClearanceAdminService _service;
        private readonly ILogger<AdminPoliceClearancesController> _logger;

        public AdminPoliceClearancesController(IPoliceClearanceAdminService service, ILogger<AdminPoliceClearancesController> logger)
        {
            _service = service; _logger = logger;
        }

        private AdminIdentity CurrentAdmin() => new()
        {
            Id = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value ?? User.FindFirst("userId")?.Value ?? string.Empty,
            Email = User.FindFirst(ClaimTypes.Email)?.Value ?? User.FindFirst("email")?.Value ?? string.Empty
        };

        private IActionResult Handle(Exception ex, string action)
        {
            switch (ex)
            {
                case PoliceClearanceValidationException v: return BadRequest(new { message = v.Message });
                case PoliceClearanceConflictException c: return Conflict(new { message = c.Message });
                case KeyNotFoundException k: return NotFound(new { message = k.Message });
                case UnauthorizedAccessException u: return StatusCode(403, new { message = u.Message });
                case InvalidOperationException io: return StatusCode(503, new { message = io.Message });
                default:
                    _logger.LogError(ex, "Unexpected error during {Action}", action);
                    return StatusCode(500, new { message = "An unexpected error occurred." });
            }
        }

        /// <summary>Latest submission per caregiver. Filter by status, search by caregiver name/email; oldest first.</summary>
        [HttpGet]
        public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            try { return Ok(await _service.ListAsync(status, search, page, pageSize)); }
            catch (Exception ex) { return Handle(ex, "ListPoliceClearances"); }
        }

        /// <summary>Number of caregivers whose latest submission is awaiting review (sidebar badge).</summary>
        [HttpGet("pending-count")]
        public async Task<IActionResult> PendingCount()
        {
            try { return Ok(new { count = await _service.PendingCountAsync() }); }
            catch (Exception ex) { return Handle(ex, "PoliceClearancePendingCount"); }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> Detail(string id)
        {
            try { return Ok(await _service.GetDetailAsync(id)); }
            catch (Exception ex) { return Handle(ex, "PoliceClearanceDetail"); }
        }

        [HttpPost("{id}/approve")]
        public async Task<IActionResult> Approve(string id, [FromBody] ApprovePoliceClearanceRequest request)
        {
            try { return Ok(await _service.ApproveAsync(id, CurrentAdmin(), request ?? new ApprovePoliceClearanceRequest())); }
            catch (Exception ex) { return Handle(ex, "ApprovePoliceClearance"); }
        }

        [HttpPost("{id}/reject")]
        public async Task<IActionResult> Reject(string id, [FromBody] ClearanceReasonRequest request)
        {
            try { return Ok(await _service.RejectAsync(id, CurrentAdmin(), request?.Reason)); }
            catch (Exception ex) { return Handle(ex, "RejectPoliceClearance"); }
        }

        [HttpPost("{id}/revoke")]
        public async Task<IActionResult> Revoke(string id, [FromBody] ClearanceReasonRequest request)
        {
            try { return Ok(await _service.RevokeAsync(id, CurrentAdmin(), request?.Reason)); }
            catch (Exception ex) { return Handle(ex, "RevokePoliceClearance"); }
        }

        /// <summary>Streams one file (any index — every file of a multi-file submission is viewable). Every view is written to the admin audit log first.</summary>
        [HttpGet("{id}/files/{index:int}")]
        public async Task<IActionResult> ViewFile(string id, int index)
        {
            try { return ClearanceFileResult.From(this, await _service.GetFileForStaffAsync(id, index, CurrentAdmin())); }
            catch (Exception ex) { return Handle(ex, "ViewPoliceClearanceFile"); }
        }
    }
}
