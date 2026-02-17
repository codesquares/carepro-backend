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
        public async Task<IActionResult> GetAllGigsAsync()
        {
            try
            {
                logger.LogInformation($"Retrieving all Gigs available");

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
