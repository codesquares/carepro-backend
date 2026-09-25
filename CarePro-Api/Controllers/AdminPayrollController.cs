using Application.DTOs;
using Application.Interfaces.Content;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;

namespace CarePro_Api.Controllers
{
    /// <summary>
    /// Admin CRUD + approval workflow for caregiver payroll (Phase 9.7). Structure cloned
    /// from <see cref="PackagesController"/> — response envelope and exception-to-status-code
    /// mapping match. Gated by OperationsPolicy, consistent with every other admin endpoint
    /// in the pivot. The acting admin id/email is always taken from the JWT, never the
    /// request body.
    /// </summary>
    [ApiController]
    [Route("api/admin/[controller]")]
    [Authorize(Policy = "OperationsPolicy")]
    public class PayrollController : ControllerBase
    {
        private readonly IPayrollService _payrollService;

        public PayrollController(IPayrollService payrollService)
        {
            _payrollService = payrollService;
        }

        private (string adminId, string adminEmail) CurrentAdmin()
        {
            var adminId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                          ?? User.FindFirst("sub")?.Value
                          ?? User.FindFirst("userId")?.Value
                          ?? string.Empty;
            var adminEmail = User.FindFirst(ClaimTypes.Email)?.Value
                             ?? User.FindFirst("email")?.Value
                             ?? string.Empty;
            return (adminId, adminEmail);
        }

        /// <summary>Create a Draft payroll record for an assignment's pay-period month.</summary>
        [HttpPost]
        public async Task<IActionResult> CreatePayroll([FromBody] CreatePayrollRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ModelState);

                var result = await _payrollService.CreatePayrollAsync(request);

                return Ok(new { success = true, message = "Payroll record created successfully", data = result });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { success = false, message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "An error occurred while creating the payroll record", error = ex.Message });
            }
        }

        /// <summary>Get all payroll records, optionally filtered by caregiver.</summary>
        [HttpGet]
        public async Task<IActionResult> GetAllPayrolls([FromQuery] string? caregiverId)
        {
            try
            {
                var payrolls = await _payrollService.GetAllPayrollsAsync(caregiverId);
                return Ok(new { success = true, data = payrolls, count = payrolls.Count });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "An error occurred while retrieving payroll records", error = ex.Message });
            }
        }

        /// <summary>Get a payroll record by ID.</summary>
        [HttpGet("{id}")]
        public async Task<IActionResult> GetPayrollById(string id)
        {
            try
            {
                var payroll = await _payrollService.GetPayrollByIdAsync(id);
                if (payroll == null)
                    return NotFound(new { success = false, message = "Payroll record not found" });

                return Ok(new { success = true, data = payroll });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "An error occurred while retrieving the payroll record", error = ex.Message });
            }
        }

        /// <summary>
        /// Approves a Draft payroll record, crediting the caregiver's wallet. Omit
        /// FinalAmount to approve at the calculated amount; supply it with a reason to override.
        /// </summary>
        [HttpPost("{id}/approve")]
        public async Task<IActionResult> ApprovePayroll(string id, [FromBody] ApprovePayrollRequest? request)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ModelState);

                var (adminId, adminEmail) = CurrentAdmin();
                if (string.IsNullOrEmpty(adminId))
                    return Unauthorized(new { success = false, message = "Unable to identify admin user." });

                var result = await _payrollService.ApprovePayrollAsync(id, request ?? new ApprovePayrollRequest(), adminId, adminEmail);

                return Ok(new { success = true, message = "Payroll record approved and wallet credited", data = result });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { success = false, message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "An error occurred while approving the payroll record", error = ex.Message });
            }
        }

        /// <summary>Marks an Approved payroll record Paid (reconciliation status only).</summary>
        [HttpPost("{id}/mark-paid")]
        public async Task<IActionResult> MarkPayrollPaid(string id)
        {
            try
            {
                var success = await _payrollService.MarkPayrollPaidAsync(id);
                if (success)
                    return Ok(new { success = true, message = "Payroll record marked as paid" });

                return BadRequest(new { success = false, message = "Failed to mark payroll record as paid" });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { success = false, message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "An error occurred while marking the payroll record as paid", error = ex.Message });
            }
        }

        /// <summary>Deletes a Draft payroll record. Approved/Paid records cannot be deleted.</summary>
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeletePayroll(string id)
        {
            try
            {
                var success = await _payrollService.DeletePayrollAsync(id);
                if (success)
                    return Ok(new { success = true, message = "Payroll record deleted successfully" });

                return BadRequest(new { success = false, message = "Failed to delete payroll record" });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { success = false, message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "An error occurred while deleting the payroll record", error = ex.Message });
            }
        }
    }
}
