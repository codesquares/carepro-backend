using Application.DTOs;
using Application.Interfaces.Content;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CarePro_Api.Controllers.Content
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class ServicesController : ControllerBase
    {
        private readonly IEligibilityService eligibilityService;
        private readonly ICertificationService certificationService;
        private readonly ILogger<ServicesController> logger;

        public ServicesController(
            IEligibilityService eligibilityService,
            ICertificationService certificationService,
            ILogger<ServicesController> logger)
        {
            this.eligibilityService = eligibilityService;
            this.certificationService = certificationService;
            this.logger = logger;
        }

        #region Service Requirements CRUD

        /// <summary>
        /// Returns service requirements for all active categories.
        /// </summary>
        [HttpGet("requirements")]
        [AllowAnonymous]
        public async Task<IActionResult> GetServiceRequirements()
        {
            try
            {
                var requirements = await eligibilityService.GetAllServiceRequirementsAsync();
                return Ok(requirements);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting service requirements");
                return StatusCode(500, new { ErrorMessage = "An error occurred retrieving service requirements." });
            }
        }

        /// <summary>
        /// Returns a single service requirement by ID.
        /// </summary>
        [HttpGet("requirements/{id}")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> GetServiceRequirementById(string id)
        {
            try
            {
                var requirement = await eligibilityService.GetServiceRequirementByIdAsync(id);
                if (requirement == null)
                    return NotFound(new { Message = $"Service requirement with ID '{id}' not found." });

                return Ok(requirement);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting service requirement: {Id}", id);
                return StatusCode(500, new { ErrorMessage = "An error occurred retrieving the service requirement." });
            }
        }

        /// <summary>
        /// Creates a new service requirement. Admin only.
        /// </summary>
        [HttpPost("requirements")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> CreateServiceRequirement([FromBody] AddServiceRequirementRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.ServiceCategory))
                    return BadRequest(new { Message = "ServiceCategory is required." });

                if (string.IsNullOrWhiteSpace(request.DisplayName))
                    return BadRequest(new { Message = "DisplayName is required." });

                var created = await eligibilityService.CreateServiceRequirementAsync(request);
                return CreatedAtAction(nameof(GetServiceRequirementById), new { id = created.Id }, created);
            }
            catch (ArgumentException ex)
            {
                return Conflict(new { Message = ex.Message });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error creating service requirement");
                return StatusCode(500, new { ErrorMessage = "An error occurred creating the service requirement." });
            }
        }

        /// <summary>
        /// Updates an existing service requirement. Admin only.
        /// Only provided fields are updated (partial update).
        /// </summary>
        [HttpPut("requirements/{id}")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> UpdateServiceRequirement(string id, [FromBody] UpdateServiceRequirementRequest request)
        {
            try
            {
                var updated = await eligibilityService.UpdateServiceRequirementAsync(id, request);
                if (updated == null)
                    return NotFound(new { Message = $"Service requirement with ID '{id}' not found." });

                return Ok(updated);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error updating service requirement: {Id}", id);
                return StatusCode(500, new { ErrorMessage = "An error occurred updating the service requirement." });
            }
        }

        /// <summary>
        /// Deletes a service requirement. Admin only.
        /// </summary>
        [HttpDelete("requirements/{id}")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> DeleteServiceRequirement(string id)
        {
            try
            {
                var deleted = await eligibilityService.DeleteServiceRequirementAsync(id);
                if (!deleted)
                    return NotFound(new { Message = $"Service requirement with ID '{id}' not found." });

                return Ok(new { Message = "Service requirement deleted successfully." });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error deleting service requirement: {Id}", id);
                return StatusCode(500, new { ErrorMessage = "An error occurred deleting the service requirement." });
            }
        }

        #endregion

        /// <summary>
        /// Returns certificate status for a caregiver — all uploaded certificates with verification status.
        /// </summary>
        [HttpGet("certificates/status")]
        public async Task<IActionResult> GetCertificateStatus([FromQuery] string caregiverId)
        {
            try
            {
                if (string.IsNullOrEmpty(caregiverId))
                    return BadRequest(new { Message = "caregiverId is required" });

                var certificates = await certificationService.GetCertificateStatusAsync(caregiverId);
                return Ok(certificates);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting certificate status for caregiver: {CaregiverId}", caregiverId);
                return StatusCode(500, new { ErrorMessage = "An error occurred retrieving certificate status." });
            }
        }

        /// <summary>
        /// Returns full eligibility map for a caregiver across all service categories.
        /// Alternative to the eligibility endpoint on the Assessments controller.
        /// </summary>
        [HttpGet("eligibility/{caregiverId}")]
        public async Task<IActionResult> GetCaregiverEligibility(string caregiverId)
        {
            try
            {
                var eligibility = await eligibilityService.GetEligibilityAsync(caregiverId);
                return Ok(eligibility);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting eligibility for caregiver: {CaregiverId}", caregiverId);
                return StatusCode(500, new { ErrorMessage = "An error occurred retrieving eligibility." });
            }
        }
    }
}
