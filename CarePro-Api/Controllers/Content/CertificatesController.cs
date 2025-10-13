using Application.DTOs;
using Application.Interfaces;
using Application.Interfaces.Content;
using Infrastructure.Content.Data;
using Infrastructure.Content.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CarePro_Api.Controllers.Content
{
    [Route("api/[controller]")]
    [ApiController]
    public class CertificatesController : ControllerBase
    {
        private readonly CareProDbContext careProDbContext;
        private readonly ICertificationService certificationService;
        private readonly ILogger<CertificatesController> logger;
        private readonly ICareGiverService careGiverService;

        public CertificatesController(CareProDbContext careProDbContext, ICertificationService certificationService, ILogger<CertificatesController> logger, ICareGiverService careGiverService)
        {
            this.careProDbContext = careProDbContext;
            this.certificationService = certificationService;
            this.logger = logger;
            this.careGiverService = careGiverService;
        }

        /// ENDPOINT TO CREATE  Certificate Services TO THE DATABASE
        [HttpPost]
        // [Authorize(Roles = "Caregiver")]
        public async Task<IActionResult> AddCertificateAsync([FromBody] AddCertificationRequest addCertificationRequest)
        {
            try
            {
                // Validate the incoming request
                if (!(await ValidateAddCertificateAsync(addCertificationRequest)))
                {
                    return BadRequest(ModelState);
                }


                // Pass Domain Object to Repository, to Persisit this
                var certificate = await certificationService.CreateCertificateAsync(addCertificationRequest);


                // Send DTO response back to ClientUser
                return Ok(certificate);

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
        // [Authorize(Roles = "Caregiver, Admin")]
        public async Task<IActionResult> GetAllCertificatesAsync(string caregiverId)
        {
            try
            {
                logger.LogInformation($"Retrieving all Certification for Caregiver with MessageId: {caregiverId}");

                var certificates = await certificationService.GetAllCaregiverCertificateAsync(caregiverId);

                return Ok(certificates);
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
        [Route("certificateId")]
        // [Authorize(Roles = "Caregiver, Admin")]
        public async Task<IActionResult> GetCertificateAsync(string certificateId)
        {
            try
            {
                logger.LogInformation($"Retrieving Certificate for Caregiver with MessageId: {certificateId}");

                var certificate = await certificationService.GetCertificateAsync(certificateId);

                return Ok(certificate);
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



        #region Validation Region

        private async Task<bool> ValidateAddCertificateAsync(AddCertificationRequest  addCertificationRequest)
        {
            if (addCertificationRequest == null)
            {
                ModelState.AddModelError(nameof(addCertificationRequest), $" cannot be empty.");
                return false;
            }

            
            var user = await careProDbContext.CareGivers.FirstOrDefaultAsync(x => x.Id.ToString() == addCertificationRequest.CaregiverId);
            if (user == null)
            {
                ModelState.AddModelError(nameof(addCertificationRequest.CaregiverId),
                    "UserId entered is Invalid or does not exist");
                return false;
            }


            if (string.IsNullOrWhiteSpace(addCertificationRequest.CertificateName))
            {
                ModelState.AddModelError(nameof(addCertificationRequest.CertificateName),
                    $"{nameof(addCertificationRequest.CertificateName)} is required.");
            }

            if (string.IsNullOrWhiteSpace(addCertificationRequest.CertificateIssuer))
            {
                ModelState.AddModelError(nameof(addCertificationRequest.CertificateIssuer),
                    $"{nameof(addCertificationRequest.CertificateIssuer)} is required");
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
