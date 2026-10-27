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
    /// Staff review of caregivers' address bills. OperationsPolicy only (same staff as police clearances); every other role gets 403.
    /// Reviewer id/email come from the JWT. The route id is the address-history record id (the proof lives on that record).
    /// </summary>
    [Route("api/admin/address-proofs")]
    [ApiController]
    [Authorize(Policy = "OperationsPolicy")]
    public class AdminAddressProofsController : ControllerBase
    {
        private readonly IAddressProofAdminService _service;
        private readonly ILogger<AdminAddressProofsController> _logger;

        public AdminAddressProofsController(IAddressProofAdminService service, ILogger<AdminAddressProofsController> logger)
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

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            try { return Ok(await _service.ListAsync(status, search, page, pageSize)); }
            catch (Exception ex) { return Handle(ex, "ListAddressProofs"); }
        }

        /// <summary>Bills awaiting review (sidebar badge).</summary>
        [HttpGet("pending-count")]
        public async Task<IActionResult> PendingCount()
        {
            try { return Ok(new { count = await _service.PendingCountAsync() }); }
            catch (Exception ex) { return Handle(ex, "AddressProofPendingCount"); }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> Detail(string id)
        {
            try { return Ok(await _service.GetDetailAsync(id)); }
            catch (Exception ex) { return Handle(ex, "AddressProofDetail"); }
        }

        [HttpPost("{id}/approve")]
        public async Task<IActionResult> Approve(string id, [FromBody] ApproveAddressProofRequest request)
        {
            try { return Ok(await _service.ApproveAsync(id, CurrentAdmin(), request ?? new ApproveAddressProofRequest())); }
            catch (Exception ex) { return Handle(ex, "ApproveAddressProof"); }
        }

        [HttpPost("{id}/reject")]
        public async Task<IActionResult> Reject(string id, [FromBody] ClearanceReasonRequest request)
        {
            try { return Ok(await _service.RejectAsync(id, CurrentAdmin(), request?.Reason)); }
            catch (Exception ex) { return Handle(ex, "RejectAddressProof"); }
        }

        /// <summary>Streams the bill. Every view is written to the admin audit log first.</summary>
        [HttpGet("{id}/file")]
        public async Task<IActionResult> ViewFile(string id)
        {
            try { return ClearanceFileResult.From(this, await _service.GetFileForStaffAsync(id, CurrentAdmin())); }
            catch (Exception ex) { return Handle(ex, "ViewAddressProofFile"); }
        }
    }
}
