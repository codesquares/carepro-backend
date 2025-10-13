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
        public async Task<IActionResult> AddGigAsync([FromForm] AddGigRequest  addGigRequest)
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
                return StatusCode(500, new { ex /*ErrorMessage = "An error occurred on the server."*/ });
            }

        }


        [HttpGet]
       // [Authorize(Roles = "Caregiver, Client, Admin")]
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
                return StatusCode(500, new { ex /*ErrorMessage = "An error occurred on the server."*/ });
            }
            

        }


        [HttpGet]
        [Route("caregiver/caregiverId")]
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
                return StatusCode(500, new { ex /*ErrorMessage = "An error occurred on the server."*/ });
            }          

        }


        [HttpGet]
        [Route("service/caregiverId")]
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
                return StatusCode(500, new { ex /*ErrorMessage = "An error occurred on the server."*/ });
            }

        }




        [HttpGet]
        [Route("caregiverId/paused")]
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
                return StatusCode(500, new { ex /*ErrorMessage = "An error occurred on the server."*/ });
            }
            

        }


        [HttpGet]
        [Route("caregiverId/draft")]
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
                return StatusCode(500, new { ex /*ErrorMessage = "An error occurred on the server."*/ });
            }
            

        }

        [HttpGet]
        [Route("gigId")]
       // [Authorize(Roles = "Caregiver, Admin")]
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
                return StatusCode(500, new { ex /*ErrorMessage = "An error occurred on the server."*/ });
            }

        }



        [HttpPut]
        [Route("UpdateGigStatusToPause/gigId")]
       // [Authorize(Roles = "Caregiver, Admin")]
        public async Task<ActionResult<string>> UpdateGigStatusToPauseAsync(string gigId, UpdateGigStatusToPauseRequest  updateGigStatusToPauseRequest)
        {
            try
            {
                var result = await gigServices.UpdateGigStatusToPauseAsync(gigId, updateGigStatusToPauseRequest);
                logger.LogInformation($"Gig Status with ID: {gigId} updated.");
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { Message = ex.Message }); // Returns 400 Bad Request
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
                return StatusCode(500, new { ex /*ErrorMessage = "An error occurred on the server."*/ });
            }

        }



        [HttpPut]
        [Route("UpdateGig/gigId")]
        // [Authorize(Roles = "Caregiver, Admin")]
        public async Task<ActionResult<string>> UpdateGigAsync(string gigId, UpdateGigRequest  updateGigRequest)
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
                return StatusCode(500, new { ex /*ErrorMessage = "An error occurred on the server."*/ });
            }

        }



        #region Validation

        private async Task<bool> ValidateAddGigAsync(AddGigRequest  addGigRequest)
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
