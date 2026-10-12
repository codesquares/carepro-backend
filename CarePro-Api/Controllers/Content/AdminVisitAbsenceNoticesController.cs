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
    /// Staff workflow for "caregiver can't attend" notices. OperationsPolicy only (SuperAdmin, or Admin in HR / ComplianceAndLegal /
    /// CareLeads): Finance and every non-admin get 403, no token gets 401. The acting admin id comes from the JWT; request bodies have
    /// no admin-id field. Status changes are RECORDS — they move no money and touch no payroll, task sheet or assignment.
    /// </summary>
    [Route("api/admin/visit-absence-notices")]
    [ApiController]
    [Authorize(Policy = "OperationsPolicy")]
    public class AdminVisitAbsenceNoticesController : ControllerBase
    {
        private readonly IVisitAbsenceService _service;
        private readonly ILogger<AdminVisitAbsenceNoticesController> _logger;

        public AdminVisitAbsenceNoticesController(IVisitAbsenceService service, ILogger<AdminVisitAbsenceNoticesController> logger)
        {
            _service = service; _logger = logger;
        }

        private string CurrentAdminId() =>
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value ?? User.FindFirst("userId")?.Value ?? string.Empty;

        private IActionResult Handle(Exception ex, string action)
        {
            switch (ex)
            {
                case ArgumentException a: return BadRequest(new { message = a.Message });
                case KeyNotFoundException k: return NotFound(new { message = k.Message });
                case UnauthorizedAccessException u: return StatusCode(403, new { message = u.Message });
                case InvalidOperationException io: return Conflict(new { message = io.Message });
                default:
                    _logger.LogError(ex, "Unexpected error during {Action}", action);
                    return StatusCode(500, new { message = "An unexpected error occurred." });
            }
        }

        /// <summary>Open first, then short-notice first, then soonest day. Optional filters: status, shortNotice.</summary>
        [HttpGet]
        public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] bool? shortNotice)
        {
            try
            {
                var rows = await _service.ListForAdminAsync(status, shortNotice);
                return Ok(new { success = true, data = rows, count = rows.Count });
            }
            catch (Exception ex) { return Handle(ex, "ListAbsenceNotices"); }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> Detail(string id)
        {
            try { return Ok(await _service.GetForAdminAsync(id)); }
            catch (Exception ex) { return Handle(ex, "AbsenceNoticeDetail"); }
        }

        [HttpPost("{id}/status")]
        public async Task<IActionResult> SetStatus(string id, [FromBody] SetAbsenceStatusRequest request)
        {
            try { return Ok(await _service.SetStatusAsync(id, request ?? new SetAbsenceStatusRequest(), CurrentAdminId())); }
            catch (Exception ex) { return Handle(ex, "SetAbsenceNoticeStatus"); }
        }
    }
}
