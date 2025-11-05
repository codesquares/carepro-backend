using Application.DTOs;
using Application.Interfaces.Content;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;

namespace CarePro_Api.Controllers
{
    [ApiController]
    [Route("api/admin/[controller]")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public class TrainingMaterialsController : ControllerBase
    {
        private readonly ITrainingMaterialService _trainingMaterialService;

        public TrainingMaterialsController(ITrainingMaterialService trainingMaterialService)
        {
            _trainingMaterialService = trainingMaterialService;
        }

        /// <summary>
        /// Upload a new training material (PDF, Document, or Video)
        /// </summary>
        [HttpPost("upload")]
        public async Task<IActionResult> UploadTrainingMaterial([FromForm] AddTrainingMaterialRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var result = await _trainingMaterialService.UploadTrainingMaterialAsync(request);

                if (result.Success)
                {
                    return Ok(new
                    {
                        success = true,
                        message = result.Message,
                        data = result
                    });
                }

                return BadRequest(new
                {
                    success = false,
                    message = result.Message
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "An error occurred while uploading the training material",
                    error = ex.Message
                });
            }
        }

        /// <summary>
        /// Get all training materials
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetAllTrainingMaterials()
        {
            try
            {
                var materials = await _trainingMaterialService.GetAllTrainingMaterialsAsync();
                
                return Ok(new
                {
                    success = true,
                    data = materials,
                    count = materials.Count
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "An error occurred while retrieving training materials",
                    error = ex.Message
                });
            }
        }

        /// <summary>
        /// Get training material by ID
        /// </summary>
        [HttpGet("{id}")]
        public async Task<IActionResult> GetTrainingMaterialById(string id)
        {
            try
            {
                var material = await _trainingMaterialService.GetTrainingMaterialByIdAsync(id);

                if (material == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Training material not found"
                    });
                }

                return Ok(new
                {
                    success = true,
                    data = material
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "An error occurred while retrieving the training material",
                    error = ex.Message
                });
            }
        }

        /// <summary>
        /// Get training materials by user type
        /// </summary>
        [HttpGet("by-user-type/{userType}")]
        public async Task<IActionResult> GetTrainingMaterialsByUserType(string userType, [FromQuery] bool activeOnly = true)
        {
            try
            {
                var result = await _trainingMaterialService.GetTrainingMaterialsByUserTypeAsync(userType, activeOnly);

                return Ok(new
                {
                    success = true,
                    data = result.Materials,
                    totalCount = result.TotalCount,
                    userType = result.UserType
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "An error occurred while retrieving training materials",
                    error = ex.Message
                });
            }
        }

        /// <summary>
        /// Update training material details and optionally replace file
        /// </summary>
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateTrainingMaterial(string id, [FromForm] UpdateTrainingMaterialRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                request.Id = id; // Ensure ID is set from route parameter

                var success = await _trainingMaterialService.UpdateTrainingMaterialAsync(request);

                if (success)
                {
                    return Ok(new
                    {
                        success = true,
                        message = "Training material updated successfully"
                    });
                }

                return BadRequest(new
                {
                    success = false,
                    message = "Failed to update training material"
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new
                {
                    success = false,
                    message = ex.Message
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "An error occurred while updating the training material",
                    error = ex.Message
                });
            }
        }

        /// <summary>
        /// Delete training material (removes from database and Cloudinary)
        /// </summary>
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteTrainingMaterial(string id)
        {
            try
            {
                var success = await _trainingMaterialService.DeleteTrainingMaterialAsync(id);

                if (success)
                {
                    return Ok(new
                    {
                        success = true,
                        message = "Training material deleted successfully"
                    });
                }

                return BadRequest(new
                {
                    success = false,
                    message = "Failed to delete training material"
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new
                {
                    success = false,
                    message = ex.Message
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "An error occurred while deleting the training material",
                    error = ex.Message
                });
            }
        }

        /// <summary>
        /// Search training materials by title, description, or filename
        /// </summary>
        [HttpGet("search")]
        public async Task<IActionResult> SearchTrainingMaterials([FromQuery] string searchTerm)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(searchTerm))
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Search term is required"
                    });
                }

                var materials = await _trainingMaterialService.SearchTrainingMaterialsAsync(searchTerm);

                return Ok(new
                {
                    success = true,
                    data = materials,
                    searchTerm = searchTerm,
                    count = materials.Count
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "An error occurred while searching training materials",
                    error = ex.Message
                });
            }
        }

        /// <summary>
        /// Toggle active status of training material
        /// </summary>
        [HttpPatch("{id}/toggle-status")]
        public async Task<IActionResult> ToggleActiveStatus(string id, [FromBody] ToggleActiveStatusRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var success = await _trainingMaterialService.ToggleActiveStatusAsync(id, request.IsActive);

                if (success)
                {
                    return Ok(new
                    {
                        success = true,
                        message = $"Training material {(request.IsActive ? "activated" : "deactivated")} successfully"
                    });
                }

                return BadRequest(new
                {
                    success = false,
                    message = "Failed to toggle active status"
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new
                {
                    success = false,
                    message = ex.Message
                });
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

        /// <summary>
        /// Get training material statistics
        /// </summary>
        [HttpGet("statistics")]
        public async Task<IActionResult> GetTrainingMaterialStatistics()
        {
            try
            {
                var allMaterials = await _trainingMaterialService.GetAllTrainingMaterialsAsync();

                var statistics = new
                {
                    totalMaterials = allMaterials.Count,
                    activeMaterials = allMaterials.Count(m => m.IsActive),
                    inactiveMaterials = allMaterials.Count(m => !m.IsActive),
                    byUserType = new
                    {
                        caregiver = allMaterials.Count(m => m.UserType == "Caregiver" || m.UserType == "Both"),
                        client = allMaterials.Count(m => m.UserType == "Client" || m.UserType == "Both"),
                        both = allMaterials.Count(m => m.UserType == "Both")
                    },
                    byFileType = new
                    {
                        pdf = allMaterials.Count(m => m.FileType == "PDF"),
                        document = allMaterials.Count(m => m.FileType == "Document"),
                        video = allMaterials.Count(m => m.FileType == "Video")
                    },
                    totalFileSize = allMaterials.Sum(m => m.FileSize),
                    averageFileSize = allMaterials.Any() ? allMaterials.Average(m => m.FileSize) : 0
                };

                return Ok(new
                {
                    success = true,
                    data = statistics
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "An error occurred while retrieving statistics",
                    error = ex.Message
                });
            }
        }
    }

    /// <summary>
    /// Request model for toggling active status
    /// </summary>
    public class ToggleActiveStatusRequest
    {
        public bool IsActive { get; set; }
    }
}