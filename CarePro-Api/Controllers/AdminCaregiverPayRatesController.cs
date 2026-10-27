using Application.DTOs;
using Application.Interfaces.Content;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CarePro_Api.Controllers
{
    /// <summary>
    /// Admin CRUD for the caregiver payroll rate table (Phase 9.3). Structure cloned
    /// from <see cref="PackagesController"/> — response envelope, exception-to-status-code
    /// mapping, and route shape all match. Gated by OperationsPolicy, consistent with
    /// every other admin endpoint in the pivot.
    /// </summary>
    [ApiController]
    [Route("api/admin/[controller]")]
    [Authorize(Policy = "OperationsPolicy")]
    public class CaregiverPayRatesController : ControllerBase
    {
        private readonly ICaregiverPayRateService _payRateService;

        public CaregiverPayRatesController(ICaregiverPayRateService payRateService)
        {
            _payRateService = payRateService;
        }

        /// <summary>Create a new caregiver pay rate.</summary>
        [HttpPost]
        public async Task<IActionResult> CreatePayRate([FromBody] AddCaregiverPayRateRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ModelState);

                var result = await _payRateService.CreatePayRateAsync(request);

                return Ok(new
                {
                    success = true,
                    message = "Pay rate created successfully",
                    data = result
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "An error occurred while creating the pay rate",
                    error = ex.Message
                });
            }
        }

        /// <summary>Get all caregiver pay rates.</summary>
        [HttpGet]
        public async Task<IActionResult> GetAllPayRates()
        {
            try
            {
                var rates = await _payRateService.GetAllPayRatesAsync();

                return Ok(new
                {
                    success = true,
                    data = rates,
                    count = rates.Count
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "An error occurred while retrieving pay rates",
                    error = ex.Message
                });
            }
        }

        /// <summary>Get a caregiver pay rate by ID.</summary>
        [HttpGet("{id}")]
        public async Task<IActionResult> GetPayRateById(string id)
        {
            try
            {
                var rate = await _payRateService.GetPayRateByIdAsync(id);

                if (rate == null)
                    return NotFound(new { success = false, message = "Pay rate not found" });

                return Ok(new { success = true, data = rate });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "An error occurred while retrieving the pay rate",
                    error = ex.Message
                });
            }
        }

        /// <summary>Update a caregiver pay rate. Only fields supplied in the body are changed.</summary>
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdatePayRate(string id, [FromBody] UpdateCaregiverPayRateRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ModelState);

                request.Id = id; // Ensure ID is set from route parameter

                var success = await _payRateService.UpdatePayRateAsync(request);

                if (success)
                    return Ok(new { success = true, message = "Pay rate updated successfully" });

                return BadRequest(new { success = false, message = "Failed to update pay rate" });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "An error occurred while updating the pay rate",
                    error = ex.Message
                });
            }
        }

        /// <summary>Delete a caregiver pay rate.</summary>
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeletePayRate(string id)
        {
            try
            {
                var success = await _payRateService.DeletePayRateAsync(id);

                if (success)
                    return Ok(new { success = true, message = "Pay rate deleted successfully" });

                return BadRequest(new { success = false, message = "Failed to delete pay rate" });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "An error occurred while deleting the pay rate",
                    error = ex.Message
                });
            }
        }

        /// <summary>Toggle a pay rate's active status.</summary>
        [HttpPatch("{id}/toggle-status")]
        public async Task<IActionResult> ToggleActiveStatus(string id, [FromBody] ToggleActiveStatusRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ModelState);

                var success = await _payRateService.ToggleActiveStatusAsync(id, request.IsActive);

                if (success)
                    return Ok(new
                    {
                        success = true,
                        message = $"Pay rate {(request.IsActive ? "activated" : "deactivated")} successfully"
                    });

                return BadRequest(new { success = false, message = "Failed to toggle active status" });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "An error occurred while toggling active status",
                    error = ex.Message
                });
            }
        }
    }
}
