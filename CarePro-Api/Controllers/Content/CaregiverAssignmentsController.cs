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
    /// Phase 4 — the caregiver side of internal assignment: view assignments,
    /// accept (finalizes to the client) or decline (back to staff, no auto-reassign).
    /// </summary>
    [Route("api/caregiver/assignments")]
    [ApiController]
    [Authorize(Roles = "Caregiver")]
    public class CaregiverAssignmentsController : ControllerBase
    {
        private readonly IAssignmentService _assignmentService;
        private readonly IPackageContractService _contractService;
        private readonly ITaskSheetService _taskSheetService;
        private readonly ILogger<CaregiverAssignmentsController> _logger;

        public CaregiverAssignmentsController(
            IAssignmentService assignmentService,
            IPackageContractService contractService,
            ITaskSheetService taskSheetService,
            ILogger<CaregiverAssignmentsController> logger)
        {
            _assignmentService = assignmentService;
            _contractService = contractService;
            _taskSheetService = taskSheetService;
            _logger = logger;
        }

        private string? CurrentCaregiverId() =>
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirst("sub")?.Value
            ?? User.FindFirst("userId")?.Value;

        private IActionResult HandleException(Exception ex, string action)
        {
            switch (ex)
            {
                case ArgumentException ae:
                    return BadRequest(new { message = ae.Message });
                case InvalidOperationException ioe:
                    return Conflict(new { message = ioe.Message });
                case KeyNotFoundException knf:
                    return NotFound(new { message = knf.Message });
                case UnauthorizedAccessException ua:
                    _logger.LogWarning("IDOR attempt during {Action}: {Message}", action, ua.Message);
                    return StatusCode(403, new { message = ua.Message });
                default:
                    _logger.LogError(ex, "Unexpected error during {Action}", action);
                    return StatusCode(500, new { message = "An unexpected error occurred." });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetMine()
        {
            var caregiverId = CurrentCaregiverId();
            if (string.IsNullOrWhiteSpace(caregiverId))
                return Unauthorized(new { message = "Caregiver identity not found in token." });
            try
            {
                var items = await _assignmentService.GetMyAssignmentsAsync(caregiverId);
                return Ok(new { success = true, data = items, count = items.Count });
            }
            catch (Exception ex) { return HandleException(ex, "GetMine"); }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetDetail(string id)
        {
            var caregiverId = CurrentCaregiverId();
            if (string.IsNullOrWhiteSpace(caregiverId))
                return Unauthorized(new { message = "Caregiver identity not found in token." });
            try
            {
                var dto = await _assignmentService.GetMyAssignmentDetailAsync(id, caregiverId);
                return Ok(new { success = true, data = dto });
            }
            catch (Exception ex) { return HandleException(ex, "GetAssignmentDetail"); }
        }

        /// <summary>
        /// The care agreement generated for this assignment's package request. Reuses the
        /// exact same lookup (<see cref="IPackageContractService.GetByPackageRequestAsync"/>)
        /// the client-facing <c>PackageRequestsController</c> route already uses — no
        /// duplicated contract logic. Ownership is enforced by loading the assignment through
        /// <see cref="IAssignmentService.GetMyAssignmentDetailAsync"/> first, which throws
        /// unless the caller is the caregiver assigned to it.
        /// </summary>
        [HttpGet("{id}/contract")]
        public async Task<IActionResult> GetContract(string id)
        {
            var caregiverId = CurrentCaregiverId();
            if (string.IsNullOrWhiteSpace(caregiverId))
                return Unauthorized(new { message = "Caregiver identity not found in token." });
            try
            {
                var assignment = await _assignmentService.GetMyAssignmentDetailAsync(id, caregiverId);

                var contract = await _contractService.GetByPackageRequestAsync(assignment.PackageRequestId);
                if (contract == null)
                    return NotFound(new { message = "No contract has been generated for this assignment yet." });

                return Ok(new { success = true, data = contract });
            }
            catch (Exception ex) { return HandleException(ex, "GetAssignmentContract"); }
        }

        [HttpGet("{id}/contract/pdf")]
        public async Task<IActionResult> GetContractPdf(string id)
        {
            var caregiverId = CurrentCaregiverId();
            if (string.IsNullOrWhiteSpace(caregiverId))
                return Unauthorized(new { message = "Caregiver identity not found in token." });
            try
            {
                var assignment = await _assignmentService.GetMyAssignmentDetailAsync(id, caregiverId);

                var contract = await _contractService.GetByPackageRequestAsync(assignment.PackageRequestId);
                if (contract == null)
                    return NotFound(new { message = "No contract has been generated for this assignment yet." });

                var pdf = await _contractService.GeneratePdfAsync(contract.Id);
                return File(pdf, "application/pdf", $"CarePro-Agreement-{contract.Id}.pdf");
            }
            catch (Exception ex) { return HandleException(ex, "GetAssignmentContractPdf"); }
        }

        /// <summary>
        /// TaskSheets for this assignment, most recent first — includes each visit's
        /// ScheduledDate/StartTime/EndTime alongside its current status. Note: the package
        /// model has no advance-scheduling mechanism today (see CaregiverAssignmentDTO's
        /// doc comment) — a caregiver creates each day's TaskSheet themselves via
        /// POST .../task-sheets/for-assignment/{id}, so this reflects today's-and-past
        /// visits, not a forward-looking calendar, until that changes.
        /// </summary>
        [HttpGet("{id}/visits")]
        public async Task<IActionResult> GetVisits(string id)
        {
            var caregiverId = CurrentCaregiverId();
            if (string.IsNullOrWhiteSpace(caregiverId))
                return Unauthorized(new { message = "Caregiver identity not found in token." });
            try
            {
                var visits = await _taskSheetService.GetVisitsForAssignmentAsync(id, caregiverId);
                return Ok(new { success = true, data = visits, count = visits.Count });
            }
            catch (Exception ex) { return HandleException(ex, "GetAssignmentVisits"); }
        }

        [HttpPost("{id}/accept")]
        public async Task<IActionResult> Accept(string id)
        {
            var caregiverId = CurrentCaregiverId();
            if (string.IsNullOrWhiteSpace(caregiverId))
                return Unauthorized(new { message = "Caregiver identity not found in token." });
            try
            {
                return Ok(await _assignmentService.AcceptAsync(id, caregiverId));
            }
            catch (Exception ex) { return HandleException(ex, "AcceptAssignment"); }
        }

        [HttpPost("{id}/decline")]
        public async Task<IActionResult> Decline(string id, [FromBody] DeclineAssignmentRequest? request)
        {
            var caregiverId = CurrentCaregiverId();
            if (string.IsNullOrWhiteSpace(caregiverId))
                return Unauthorized(new { message = "Caregiver identity not found in token." });
            try
            {
                return Ok(await _assignmentService.DeclineAsync(id, caregiverId, request?.Reason));
            }
            catch (Exception ex) { return HandleException(ex, "DeclineAssignment"); }
        }
    }
}
