using Application.DTOs;
using Application.Interfaces.Content;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CarePro_Api.Controllers
{
    /// <summary>
    /// Admin CRUD for pre-priced care packages (Phase 3). Structure cloned from
    /// <see cref="TrainingMaterialsController"/> — the confirmed real-CRUD template.
    /// Gated by OperationsPolicy, consistent with every other admin endpoint in the pivot.
    /// </summary>
    [ApiController]
    [Route("api/admin/[controller]")]
    [Authorize(Policy = "OperationsPolicy")]
    public class PackagesController : ControllerBase
    {
        private readonly IPackageService _packageService;

        public PackagesController(IPackageService packageService)
        {
            _packageService = packageService;
        }

        /// <summary>Create a new care package.</summary>
        [HttpPost]
        public async Task<IActionResult> CreatePackage([FromBody] AddPackageRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ModelState);

                var result = await _packageService.CreatePackageAsync(request);

                return Ok(new
                {
                    success = true,
                    message = "Package created successfully",
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
                    message = "An error occurred while creating the package",
                    error = ex.Message
                });
            }
        }

        /// <summary>Get all care packages.</summary>
        [HttpGet]
        public async Task<IActionResult> GetAllPackages()
        {
            try
            {
                var packages = await _packageService.GetAllPackagesAsync();

                return Ok(new
                {
                    success = true,
                    data = packages,
                    count = packages.Count
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "An error occurred while retrieving packages",
                    error = ex.Message
                });
            }
        }

        /// <summary>Get a care package by ID.</summary>
        [HttpGet("{id}")]
        public async Task<IActionResult> GetPackageById(string id)
        {
            try
            {
                var package = await _packageService.GetPackageByIdAsync(id);

                if (package == null)
                    return NotFound(new { success = false, message = "Package not found" });

                return Ok(new { success = true, data = package });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "An error occurred while retrieving the package",
                    error = ex.Message
                });
            }
        }

        /// <summary>Update a care package. Only fields supplied in the body are changed.</summary>
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdatePackage(string id, [FromBody] UpdatePackageRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ModelState);

                request.Id = id; // Ensure ID is set from route parameter

                var success = await _packageService.UpdatePackageAsync(request);

                if (success)
                    return Ok(new { success = true, message = "Package updated successfully" });

                return BadRequest(new { success = false, message = "Failed to update package" });
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
                    message = "An error occurred while updating the package",
                    error = ex.Message
                });
            }
        }

        /// <summary>Delete a care package.</summary>
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeletePackage(string id)
        {
            try
            {
                var success = await _packageService.DeletePackageAsync(id);

                if (success)
                    return Ok(new { success = true, message = "Package deleted successfully" });

                return BadRequest(new { success = false, message = "Failed to delete package" });
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
                    message = "An error occurred while deleting the package",
                    error = ex.Message
                });
            }
        }

        /// <summary>Toggle a package's active status.</summary>
        [HttpPatch("{id}/toggle-status")]
        public async Task<IActionResult> ToggleActiveStatus(string id, [FromBody] ToggleActiveStatusRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ModelState);

                var success = await _packageService.ToggleActiveStatusAsync(id, request.IsActive);

                if (success)
                    return Ok(new
                    {
                        success = true,
                        message = $"Package {(request.IsActive ? "activated" : "deactivated")} successfully"
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
