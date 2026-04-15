using Application.DTOs;
using Application.Interfaces;
using Domain.Entities;
using Infrastructure.Content.Data;
using Infrastructure.Content.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CarePro_Api.Controllers.Content
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class GigsController : ControllerBase
    {
        private readonly IGigServices gigServices;
        private readonly ILogger<GigsController> logger;

        public GigsController(IGigServices gigServices, ILogger<GigsController> logger)
        {
            this.gigServices = gigServices;
            this.logger = logger;
        }

        /// ENDPOINT TO CREATE  Gigs Services TO THE DATABASE
        [HttpPost]
        // [Authorize(Roles = "Caregiver")]
        public async Task<IActionResult> AddGigAsync([FromForm] AddGigRequest addGigRequest)
        {
            try
            {
                // Validate the incoming request
                if (!(await ValidateAddGigAsync(addGigRequest)))
                {
                    return BadRequest(ModelState);
                }


                // Pass Domain Object to Repository, to Persisit this
                var gig = await gigServices.CreateGigAsync(addGigRequest);


                // Send DTO response back to ClientUser
                return Ok(gig);

            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                // Eligibility check failed — return 403 with structured error
                try
                {
                    var eligibilityError = System.Text.Json.JsonSerializer.Deserialize<Application.DTOs.GigEligibilityError>(ex.Message);
                    return StatusCode(403, eligibilityError);
                }
                catch
                {
                    return StatusCode(403, new { Message = ex.Message });
                }
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ApplicationException appEx)
            {
                // Handle application-specific exceptions
                return BadRequest(new { ErrorMessage = appEx.Message });
            }
            catch (HttpRequestException httpEx)
            {
                // Handle HTTP request-related exceptions
                return StatusCode(500, new { ErrorMessage = httpEx.Message });
            }
            catch (Exception ex)
            {
                // Log the full exception details for debugging
                logger.LogError(ex, "An unexpected error occurred while creating gig");

                // Return only safe error information to client
                return StatusCode(500, new { ErrorMessage = "An error occurred on the server." });
            }

        }


        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> GetAllGigsAsync(
            [FromQuery] int? page = null,
            [FromQuery] int? pageSize = null,
            [FromQuery] string? status = null,
            [FromQuery] string? search = null,
            [FromQuery] string? category = null)
        {
            try
            {
                logger.LogInformation($"Retrieving all Gigs available");

                if (page.HasValue || pageSize.HasValue)
                {
                    var paginatedGigs = await gigServices.GetAllGigsPaginatedAsync(
                        page ?? 1, pageSize ?? 20, status, search, category);
                    return Ok(new
                    {
                        success = true,
                        data = paginatedGigs.Items,
                        totalCount = paginatedGigs.TotalCount,
                        page = paginatedGigs.Page,
                        pageSize = paginatedGigs.PageSize,
                        hasMore = paginatedGigs.HasMore,
                    });
                }

                var gigs = await gigServices.GetAllGigsAsync();

                return Ok(gigs);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ApplicationException appEx)
            {
                // Handle application-specific exceptions
                return BadRequest(new { ErrorMessage = appEx.Message });
            }
            catch (HttpRequestException httpEx)
            {
                // Handle HTTP request-related exceptions
                return StatusCode(500, new { ErrorMessage = httpEx.Message });
            }
            catch (Exception ex)
            {
                // Handle other exceptions
                logger.LogError(ex, "An unexpected error occurred"); return StatusCode(500, new { ErrorMessage = "An error occurred on the server." });
            }


        }


        [HttpGet]
        [Route("caregiver/{caregiverId}")]
        // [Authorize(Roles = "Caregiver, Admin")]
        public async Task<IActionResult> GetAllCaregiverGigsAsync(string caregiverId)
        {
            try
            {
                logger.LogInformation($"Retrieving all Gigs for Caregiver with MessageId: {caregiverId}");

                var services = await gigServices.GetAllCaregiverGigsAsync(caregiverId);

                return Ok(services);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ApplicationException appEx)
            {
                // Handle application-specific exceptions
                return BadRequest(new { ErrorMessage = appEx.Message });
            }
            catch (HttpRequestException httpEx)
            {
                // Handle HTTP request-related exceptions
                return StatusCode(500, new { ErrorMessage = httpEx.Message });
            }
            catch (Exception ex)
            {
                // Handle other exceptions
                logger.LogError(ex, "An unexpected error occurred"); return StatusCode(500, new { ErrorMessage = "An error occurred on the server." });
            }

        }


        [HttpGet]
        [Route("service/{caregiverId}")]
        // [Authorize(Roles = "Caregiver, Admin")]
        public async Task<IActionResult> GetAllCaregiverGigsServicesAsync(string caregiverId)
        {
            try
            {
                logger.LogInformation($"Retrieving all Services for Caregiver with MessageId: {caregiverId}");

                var services = await gigServices.GetAllSubCategoriesForCaregiverAsync(caregiverId);

                return Ok(services);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ApplicationException appEx)
            {
                // Handle application-specific exceptions
                return BadRequest(new { ErrorMessage = appEx.Message });
            }
            catch (HttpRequestException httpEx)
            {
                // Handle HTTP request-related exceptions
                return StatusCode(500, new { ErrorMessage = httpEx.Message });
            }
            catch (Exception ex)
            {
                // Handle other exceptions
                logger.LogError(ex, "An unexpected error occurred"); return StatusCode(500, new { ErrorMessage = "An error occurred on the server." });
            }

        }




        [HttpGet]
        [Route("{caregiverId}/paused")]
        // [Authorize(Roles = "Caregiver, Admin")]
        public async Task<IActionResult> GetAllCaregiverPausedGigsAsync(string caregiverId)
        {
            try
            {
                logger.LogInformation($"Retrieving all Gigs for Caregiver with MessageId: {caregiverId}");

                var services = await gigServices.GetAllCaregiverPausedGigsAsync(caregiverId);

                return Ok(services);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ApplicationException appEx)
            {
                // Handle application-specific exceptions
                return BadRequest(new { ErrorMessage = appEx.Message });
            }
            catch (HttpRequestException httpEx)
            {
                // Handle HTTP request-related exceptions
                return StatusCode(500, new { ErrorMessage = httpEx.Message });
            }
            catch (Exception ex)
            {
                // Handle other exceptions
                logger.LogError(ex, "An unexpected error occurred"); return StatusCode(500, new { ErrorMessage = "An error occurred on the server." });
            }


        }


        [HttpGet]
        [Route("{caregiverId}/draft")]
        // [Authorize(Roles = "Caregiver, Admin")]
        public async Task<IActionResult> GetAllCaregiverDraftGigsAsync(string caregiverId)
        {
            try
            {
                logger.LogInformation($"Retrieving all Gigs for Caregiver with MessageId: {caregiverId}");

                var services = await gigServices.GetAllCaregiverDraftGigsAsync(caregiverId);

                return Ok(services);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ApplicationException appEx)
            {
                // Handle application-specific exceptions
                return BadRequest(new { ErrorMessage = appEx.Message });
            }
            catch (HttpRequestException httpEx)
            {
                // Handle HTTP request-related exceptions
                return StatusCode(500, new { ErrorMessage = httpEx.Message });
            }
            catch (Exception ex)
            {
                // Handle other exceptions
                logger.LogError(ex, "An unexpected error occurred"); return StatusCode(500, new { ErrorMessage = "An error occurred on the server." });
            }


        }

        [HttpGet("{gigId}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetGigAsync(string gigId)
        {
            try
            {
                logger.LogInformation($"Retrieving  Service with MessageId: {gigId}");

                var gig = await gigServices.GetGigAsync(gigId);

                return Ok(gig);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ApplicationException appEx)
            {
                return BadRequest(new { ErrorMessage = appEx.Message });
            }
            catch (HttpRequestException httpEx)
            {
                return StatusCode(500, new { ErrorMessage = httpEx.Message });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An unexpected error occurred"); return StatusCode(500, new { ErrorMessage = "An error occurred on the server." });
            }

        }



        [HttpPut]
        [Route("UpdateGigStatusToPause/{gigId}")]
        // [Authorize(Roles = "Caregiver, Admin")]
        public async Task<ActionResult<string>> UpdateGigStatusToPauseAsync(string gigId, UpdateGigStatusToPauseRequest updateGigStatusToPauseRequest)
        {
            try
            {
                var result = await gigServices.UpdateGigStatusToPauseAsync(gigId, updateGigStatusToPauseRequest);
                logger.LogInformation($"Gig Status with ID: {gigId} updated.");
                return Ok(new { Message = result });
            }
            catch (ArgumentNullException ex)
            {
                logger.LogWarning(ex, $"Null argument in UpdateGigStatusToPause request for gigId: {gigId}");
                return BadRequest(new { Message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                logger.LogWarning(ex, $"Invalid argument in UpdateGigStatusToPause request for gigId: {gigId}");
                return BadRequest(new { Message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                logger.LogWarning(ex, $"Resource not found in UpdateGigStatusToPause request for gigId: {gigId}");
                return NotFound(new { Message = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                logger.LogWarning(ex, $"Unauthorized access attempt in UpdateGigStatusToPause for gigId: {gigId}");
                return StatusCode(403, new { Message = ex.Message }); // 403 Forbidden
            }
            catch (InvalidOperationException ex)
            {
                logger.LogWarning(ex, $"Invalid operation in UpdateGigStatusToPause for gigId: {gigId}");
                return BadRequest(new { Message = ex.Message });
            }
            catch (ApplicationException appEx)
            {
                logger.LogError(appEx, $"Application error in UpdateGigStatusToPause for gigId: {gigId}");
                return BadRequest(new { Message = appEx.Message });
            }
            catch (HttpRequestException httpEx)
            {
                logger.LogError(httpEx, $"HTTP request error in UpdateGigStatusToPause for gigId: {gigId}");
                return StatusCode(500, new { Message = httpEx.Message });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Unexpected error in UpdateGigStatusToPause for gigId: {gigId}"); 
                return StatusCode(500, new { Message = "An unexpected error occurred on the server. Please try again later." });
            }

        }



        [HttpPut]
        [Route("UpdateGig/{gigId}")]
        // [Authorize(Roles = "Caregiver, Admin")]
        public async Task<ActionResult<string>> UpdateGigAsync(string gigId, UpdateGigRequest updateGigRequest)
        {
            try
            {
                var result = await gigServices.UpdateGigAsync(gigId, updateGigRequest);
                logger.LogInformation($"Gig Status with ID: {gigId} updated.");
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                // Eligibility check failed — return 403 with structured error
                try
                {
                    var eligibilityError = System.Text.Json.JsonSerializer.Deserialize<Application.DTOs.GigEligibilityError>(ex.Message);
                    return StatusCode(403, eligibilityError);
                }
                catch
                {
                    return StatusCode(403, new { Message = ex.Message });
                }
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ApplicationException appEx)
            {
                return BadRequest(new { ErrorMessage = appEx.Message });
            }
            catch (HttpRequestException httpEx)
            {
                return StatusCode(500, new { ErrorMessage = httpEx.Message });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An unexpected error occurred"); return StatusCode(500, new { ErrorMessage = "An error occurred on the server." });
            }

        }


        [HttpDelete]
        [Route("SoftDeleteGig/{gigId}")]
        // [Authorize(Roles = "Caregiver, Admin")]
        public async Task<IActionResult> SoftDeleteGigAsync(string gigId, [FromQuery] string caregiverId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(caregiverId))
                {
                    return BadRequest(new { message = "Caregiver ID is required." });
                }

                var result = await gigServices.SoftDeleteGigAsync(gigId, caregiverId);
                logger.LogInformation($"Gig with ID: {gigId} soft deleted by caregiver: {caregiverId}");
                return Ok(new { message = result });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An unexpected error occurred during soft delete");
                return StatusCode(500, new { message = "An error occurred on the server." });
            }
        }

        /// <summary>
        /// Admin-only endpoint to bulk soft-delete gigs.
        /// Requires SuperAdmin role for elevated security.
        /// </summary>
        [HttpDelete]
        [Route("admin/BulkSoftDelete")]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<IActionResult> AdminBulkSoftDeleteGigsAsync([FromBody] AdminBulkDeleteGigsRequest request)
        {
            try
            {
                if (request == null)
                {
                    return BadRequest(new { message = "Request body is required." });
                }

                if (!request.DeleteAll && (request.GigIds == null || !request.GigIds.Any()))
                {
                    return BadRequest(new { message = "Either provide a list of gig IDs or set deleteAll to true." });
                }

                if (string.IsNullOrWhiteSpace(request.AdminUserId))
                {
                    return BadRequest(new { message = "Admin user ID is required for audit purposes." });
                }

                var result = await gigServices.AdminBulkSoftDeleteGigsAsync(request.GigIds, request.DeleteAll, request.AdminUserId);

                logger.LogWarning(
                    "Admin bulk soft-delete executed by {AdminUserId}. Deleted: {Deleted}, Skipped: {Skipped}, Failed: {Failed}",
                    request.AdminUserId, result.DeletedCount, result.SkippedCount, result.FailedCount);

                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An unexpected error occurred during admin bulk soft-delete");
                return StatusCode(500, new { message = "An error occurred on the server." });
            }
        }

        /// <summary>
        /// Restore a soft-deleted gig within the 30-day grace period.
        /// Gig is restored to Draft status so the caregiver must review and republish.
        /// </summary>
        [HttpPut]
        [Route("RestoreGig/{gigId}")]
        public async Task<IActionResult> RestoreGigAsync(string gigId, [FromQuery] string caregiverId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(caregiverId))
                {
                    return BadRequest(new { message = "Caregiver ID is required." });
                }

                var result = await gigServices.RestoreGigAsync(gigId, caregiverId);
                logger.LogInformation("Gig {GigId} restored by caregiver {CaregiverId}", gigId, caregiverId);
                return Ok(new { message = result });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An unexpected error occurred during gig restore");
                return StatusCode(500, new { message = "An error occurred on the server." });
            }
        }


        /// <summary>
        /// Get all soft-deleted gigs for a specific caregiver.
        /// Returns deletion date and days remaining to restore.
        /// </summary>
        [HttpGet]
        [Route("deleted")]
        public async Task<IActionResult> GetDeletedGigsByCaregiverAsync([FromQuery] string caregiverId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(caregiverId))
                {
                    return BadRequest(new { message = "Caregiver ID is required." });
                }

                var deletedGigs = await gigServices.GetDeletedGigsByCaregiverAsync(caregiverId);
                return Ok(deletedGigs);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An unexpected error occurred while fetching deleted gigs");
                return StatusCode(500, new { message = "An error occurred on the server." });
            }
        }

        /// <summary>
        /// Admin endpoint to view all soft-deleted gigs across the platform.
        /// Supports pagination and optional filtering by caregiver.
        /// </summary>
        [HttpGet]
        [Route("admin/deleted")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        public async Task<IActionResult> GetAllDeletedGigsPaginatedAsync(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? caregiverId = null)
        {
            try
            {
                var result = await gigServices.GetAllDeletedGigsPaginatedAsync(page, pageSize, caregiverId);
                return Ok(new
                {
                    success = true,
                    data = result.Items,
                    totalCount = result.TotalCount,
                    page = result.Page,
                    pageSize = result.PageSize,
                    hasMore = result.HasMore,
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An unexpected error occurred while fetching admin deleted gigs");
                return StatusCode(500, new { message = "An error occurred on the server." });
            }
        }


        #region Validation

        private async Task<bool> ValidateAddGigAsync(AddGigRequest addGigRequest)
        {
            if (addGigRequest == null)
            {
                ModelState.AddModelError(nameof(addGigRequest), $" cannot be empty.");
                return false;
            }


            if (string.IsNullOrWhiteSpace(addGigRequest.Title))
            {
                ModelState.AddModelError(nameof(addGigRequest.Title),
                    $"{nameof(addGigRequest.Title)} is required.");
            }

            if (string.IsNullOrWhiteSpace(addGigRequest.Category))
            {
                ModelState.AddModelError(nameof(addGigRequest.Category),
                    $"{nameof(addGigRequest.Category)} is required");
            }

            if (string.IsNullOrWhiteSpace(addGigRequest.DeliveryTime))
            {
                ModelState.AddModelError(nameof(addGigRequest.DeliveryTime),
                    $"{nameof(addGigRequest.DeliveryTime)} is required.");
            }

            if (string.IsNullOrWhiteSpace(addGigRequest.PackageDetails))
            {
                ModelState.AddModelError(nameof(addGigRequest.PackageDetails),
                    $"{nameof(addGigRequest.PackageDetails)} is required.");
            }

            if (string.IsNullOrWhiteSpace(addGigRequest.PackageDetails))
            {
                ModelState.AddModelError(nameof(addGigRequest.PackageDetails),
                    $"{nameof(addGigRequest.PackageDetails)} is required.");
            }

            if (addGigRequest.Price <= 0)
            {
                ModelState.AddModelError(nameof(addGigRequest.Price),
                    $"{nameof(addGigRequest.Price)} cannot be 0.");
            }



            if (ModelState.ErrorCount > 0)
            {
                return false;
            }

            return true;
        }



        #endregion
    }
}
