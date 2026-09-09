using Application.DTOs;
using Application.Interfaces.Content;
using Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace CarePro_Api.Controllers.Content
{
    /// <summary>
    /// Phase 4 — staff-facing internal assignment. Includes the "Pending Acceptance"
    /// view: every assignment currently awaiting a caregiver response, longest-pending
    /// first, so staff can notice and act on a stalled assignment (there is no
    /// automatic reassignment or timeout).
    /// Gated by OperationsPolicy, like the rest of the admin matching tooling.
    /// </summary>
    [Route("api/admin/assignments")]
    [ApiController]
    [Authorize(Policy = "OperationsPolicy")]
    public class AdminAssignmentsController : ControllerBase
    {
        private readonly IAssignmentService _assignmentService;
        private readonly ICareRequestMatchingService _matchingService;
        private readonly IPackageRequestService _packageRequestService;
        private readonly Infrastructure.Content.Data.CareProDbContext _db;
        private readonly ILogger<AdminAssignmentsController> _logger;

        public AdminAssignmentsController(
            IAssignmentService assignmentService,
            ICareRequestMatchingService matchingService,
            IPackageRequestService packageRequestService,
            Infrastructure.Content.Data.CareProDbContext db,
            ILogger<AdminAssignmentsController> logger)
        {
            _assignmentService = assignmentService;
            _matchingService = matchingService;
            _packageRequestService = packageRequestService;
            _db = db;
            _logger = logger;
        }

        private (string? adminId, string adminEmail) CurrentAdmin()
        {
            var id = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? User.FindFirst("sub")?.Value
                     ?? User.FindFirst("userId")?.Value;
            var email = User.FindFirst(ClaimTypes.Email)?.Value
                        ?? User.FindFirst("email")?.Value
                        ?? string.Empty;
            return (id, email);
        }

        private IActionResult HandleException(Exception ex, string action)
        {
            switch (ex)
            {
                case CaregiverNotReadyException cnr:
                    return Conflict(new { message = cnr.Message, errorCode = cnr.ErrorCode, reasons = cnr.Reasons });
                case ArgumentException ae:
                    return BadRequest(new { message = ae.Message });
                case InvalidOperationException ioe:
                    return Conflict(new { message = ioe.Message });
                case KeyNotFoundException knf:
                    return NotFound(new { message = knf.Message });
                default:
                    _logger.LogError(ex, "Unexpected error during {Action}", action);
                    return StatusCode(500, new { message = "An unexpected error occurred." });
            }
        }

        /// <summary>Ranked candidate caregivers for a package request (reuses the scoring engine + a type/specialty hard filter).</summary>
        [HttpGet("candidates")]
        public async Task<IActionResult> GetCandidates([FromQuery] string packageRequestId)
        {
            try
            {
                if (!MongoDB.Bson.ObjectId.TryParse(packageRequestId, out var oid))
                    return BadRequest(new { message = "Invalid package request id." });

                var req = await _db.PackageRequests.FindAsync(oid);
                if (req == null) return NotFound(new { message = "Package request not found." });

                var matches = await _matchingService.FindCandidatesForPackageAsync(new PackageAssignmentMatchQuery
                {
                    ServiceCategory = req.ServiceCategory,
                    RequiredCaregiverType = req.RequiredCaregiverType.ToString(),
                    RequiredSpecialty = req.RequiredSpecialty,
                    Location = req.Location,
                    Latitude = req.Latitude,
                    Longitude = req.Longitude,
                    ClientId = req.ClientId,
                    Budget = req.Budget,
                });

                return Ok(new { success = true, data = matches, count = matches.Count });
            }
            catch (Exception ex) { return HandleException(ex, "GetCandidates"); }
        }

        /// <summary>Assign a specific caregiver to a package request (creates a PendingAcceptance assignment).</summary>
        [HttpPost]
        public async Task<IActionResult> Assign([FromBody] AssignCaregiverRequest request)
        {
            if (request == null) return BadRequest(new { message = "Request body is required." });
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var (adminId, adminEmail) = CurrentAdmin();
            if (string.IsNullOrEmpty(adminId))
                return Unauthorized(new { message = "Unable to identify admin user." });

            try
            {
                var dto = await _assignmentService.AssignAsync(
                    request.PackageRequestId, request.CaregiverId,
                    adminId, adminEmail, request.AssignedBy ?? "staff", request.MatchScore);
                return Ok(new { success = true, data = dto });
            }
            catch (Exception ex) { return HandleException(ex, "Assign"); }
        }

        /// <summary>The Pending Acceptance view — assignments awaiting a caregiver response, longest-pending first.</summary>
        [HttpGet("pending-acceptance")]
        public async Task<IActionResult> GetPendingAcceptance()
        {
            try
            {
                var rows = await _assignmentService.GetPendingAcceptanceAsync();
                return Ok(new { success = true, data = rows, count = rows.Count });
            }
            catch (Exception ex) { return HandleException(ex, "GetPendingAcceptance"); }
        }

        /// <summary>Withdraw a pending assignment (e.g. to reassign a stalled one).</summary>
        [HttpPost("{id}/cancel")]
        public async Task<IActionResult> Cancel(string id, [FromBody] CancelAssignmentRequest request)
        {
            if (request == null) return BadRequest(new { message = "Request body is required." });
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var (adminId, adminEmail) = CurrentAdmin();
            if (string.IsNullOrEmpty(adminId))
                return Unauthorized(new { message = "Unable to identify admin user." });

            try
            {
                var result = await _assignmentService.CancelAsync(id, adminId, adminEmail, request.Reason);
                return Ok(result);
            }
            catch (Exception ex) { return HandleException(ex, "Cancel"); }
        }
    }
}
