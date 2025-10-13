using Application.DTOs;
using Application.Interfaces.Content;
using Microsoft.AspNetCore.Mvc;

namespace CarePro_Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class VerificationsController : ControllerBase
    {
        private readonly IVerificationService verificationService;
        private readonly ICareGiverService careGiverService;
        private readonly ILogger<VerificationsController> logger;

        public VerificationsController(IVerificationService verificationService, ICareGiverService careGiverService, ILogger<VerificationsController> logger)
        {
            this.verificationService = verificationService;
            this.careGiverService = careGiverService;
            this.logger = logger;
        }

        [HttpPost]
        // [Authorize(Roles = "Caregiver")]
        public async Task<IActionResult> AddVerificationAsync([FromBody] AddVerificationRequest addVerificationRequest )
        {
            try
            {
                // Pass Domain Object to Repository, to Persisit this
                var verification = await verificationService.CreateVerificationAsync(addVerificationRequest);


                // Send DTO response back to ClientUser
                return Ok(verification);

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
        [Route("userId")]
        // [Authorize(Roles = "Caregiver, Client, Admin")]
        public async Task<IActionResult> GetCaregiverVerificationAsync(string userId)
        {

            try
            {
                logger.LogInformation($"Retrieving Verification for caregiver with ID '{userId}'.");

                var verification  = await verificationService.GetVerificationAsync(userId);

                return Ok(verification);

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


        [HttpPut]
        [Route("verificationId")]
        // [Authorize(Roles = "Caregiver, Admin")]
        public async Task<ActionResult<string>> UpdateVerificationAsync(string verificationId, UpdateVerificationRequest updateVerificationRequest )
        {
            try
            {
                var result = await verificationService.UpdateVerificationAsync(verificationId, updateVerificationRequest);
                logger.LogInformation($"Verification  with ID: {verificationId} updated.");
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
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }

        }


    }
}
