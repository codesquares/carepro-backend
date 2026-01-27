using Application.DTOs;
using Application.Interfaces;
using Application.Interfaces.Content;
using Infrastructure.Content.Data;
using Infrastructure.Content.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;

namespace CarePro_Api.Controllers.Content
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
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

        /// <summary>
        /// Upload a new certificate with automatic verification
        /// </summary>
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

                // Create certificate with verification
                var result = await certificationService.CreateCertificateAsync(addCertificationRequest);

                // Send response with upload status and verification results
                return Ok(new
                {
                    success = true,
                    message = "Certificate uploaded successfully",
                    data = result
                });

            }
            catch (ArgumentException ex)
            {
                return BadRequest(new 
                { 
                    success = false,
                    message = ex.Message,
                    errors = new Dictionary<string, string[]>
                    {
                        { ex.ParamName ?? "certificateValidation", new[] { ex.Message } }
                    }
                });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new 
                { 
                    success = false,
                    message = ex.Message,
                    errors = new Dictionary<string, string[]>
                    {
                        { "certificateName", new[] { ex.Message } }
                    }
                });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new 
                { 
                    success = false,
                    message = ex.Message 
                });
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
                logger.LogError(ex, "An unexpected error occurred");
                return StatusCode(500, new { ErrorMessage = "An error occurred on the server." });
            }
        }

        /// <summary>
        /// Get all certificates for a caregiver
        /// </summary>
        [HttpGet]
        // [Authorize(Roles = "Caregiver, Admin")]
        public async Task<IActionResult> GetAllCertificatesAsync(string caregiverId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(caregiverId))
                {
                    return BadRequest(new { message = "CaregiverId is required" });
                }

                logger.LogInformation($"Retrieving all certificates for caregiver: {caregiverId}");

                var certificates = await certificationService.GetAllCaregiverCertificateAsync(caregiverId);

                return Ok(new
                {
                    success = true,
                    data = certificates
                });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ApplicationException appEx) when (appEx.Message.Contains("legacy data schema"))
            {
                return StatusCode(422, new { 
                    ErrorMessage = "Legacy certificate data detected", 
                    Details = appEx.Message,
                    Resolution = "Please contact support to migrate your certificate data or manually clean up legacy documents."
                });
            }
            catch (ApplicationException appEx)
            {
                return BadRequest(new { ErrorMessage = appEx.Message });
            }
            catch (InvalidOperationException invOpEx) when (invOpEx.Message.Contains("Document element is missing"))
            {
                return StatusCode(422, new { 
                    ErrorMessage = "Certificate data schema incompatibility", 
                    Details = "Your certificate data was created with an older version of the system and is incompatible with the current schema.",
                    Resolution = "Please delete existing certificates and re-upload them, or contact support for data migration."
                });
            }
            catch (HttpRequestException httpEx)
            {
                return StatusCode(500, new { ErrorMessage = httpEx.Message });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An unexpected error occurred while retrieving certificates for caregiver {CaregiverId}", caregiverId);
                return StatusCode(500, new { 
                    ErrorMessage = "An error occurred on the server.",
                    Details = ex.Message,
                    RequestId = HttpContext.TraceIdentifier
                });
            }
        }

        /// <summary>
        /// Get a specific certificate by ID
        /// </summary>
        [HttpGet("{certificateId}")]
        // [Authorize(Roles = "Caregiver, Admin")]
        public async Task<IActionResult> GetCertificateAsync(string certificateId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(certificateId))
                {
                    return BadRequest(new { message = "CertificateId is required" });
                }

                logger.LogInformation($"Retrieving certificate: {certificateId}");

                var certificate = await certificationService.GetCertificateAsync(certificateId);

                return Ok(new
                {
                    success = true,
                    data = certificate
                });
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
                logger.LogError(ex, "An unexpected error occurred");
                return StatusCode(500, new { ErrorMessage = "An error occurred on the server." });
            }
        }

        /// <summary>
        /// Delete all certificates for a caregiver (admin only - useful for cleaning up legacy data)
        /// </summary>
        [HttpDelete("caregiver/{caregiverId}")]
        // [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteAllCaregiverCertificatesAsync(string caregiverId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(caregiverId))
                {
                    return BadRequest(new { message = "CaregiverId is required" });
                }

                logger.LogInformation($"Deleting all certificates for caregiver: {caregiverId}");

                await certificationService.DeleteAllCaregiverCertificatesAsync(caregiverId);

                return Ok(new
                {
                    success = true,
                    message = "All certificates deleted successfully"
                });
            }
            catch (ApplicationException appEx) when (appEx.Message.Contains("legacy data schema"))
            {
                return StatusCode(422, new { 
                    ErrorMessage = "Cannot delete legacy certificate data", 
                    Details = appEx.Message,
                    Resolution = "Manual database cleanup required. Use MongoDB tools to remove incompatible documents."
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An unexpected error occurred during bulk certificate deletion for caregiver {CaregiverId}", caregiverId);
                return StatusCode(500, new { 
                    ErrorMessage = "An error occurred on the server.",
                    Details = ex.Message,
                    RequestId = HttpContext.TraceIdentifier
                });
            }
        }

        /// <summary>
        /// Delete a specific certificate
        /// </summary>
        [HttpDelete("{certificateId}")]
        // [Authorize(Roles = "Caregiver, Admin")]
        public async Task<IActionResult> DeleteCertificateAsync(string certificateId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(certificateId))
                {
                    return BadRequest(new { message = "CertificateId is required" });
                }

                logger.LogInformation($"Deleting certificate: {certificateId}");

                await certificationService.DeleteCertificateAsync(certificateId);

                return Ok(new
                {
                    success = true,
                    message = "Certificate deleted successfully"
                });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An unexpected error occurred during certificate deletion");
                return StatusCode(500, new { ErrorMessage = "An error occurred on the server." });
            }
        }

        /// <summary>
        /// Retry verification for a specific certificate
        /// </summary>
        [HttpPost("{certificateId}/retry-verification")]
        // [Authorize(Roles = "Admin")]
        public async Task<IActionResult> RetryVerificationAsync(string certificateId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(certificateId))
                {
                    return BadRequest(new { message = "CertificateId is required" });
                }

                logger.LogInformation($"Retrying verification for certificate: {certificateId}");

                var result = await certificationService.RetryVerificationAsync(certificateId);

                return Ok(new
                {
                    success = true,
                    message = "Verification retry completed",
                    data = result
                });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ApplicationException appEx)
            {
                return BadRequest(new { ErrorMessage = appEx.Message });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An unexpected error occurred during verification retry");
                return StatusCode(500, new { ErrorMessage = "An error occurred on the server." });
            }
        }

        #region Validation Region

        private async Task<bool> ValidateAddCertificateAsync(AddCertificationRequest addCertificationRequest)
        {
            if (addCertificationRequest == null)
            {
                ModelState.AddModelError(nameof(addCertificationRequest), $"Request cannot be empty.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(addCertificationRequest.CaregiverId))
            {
                ModelState.AddModelError(nameof(addCertificationRequest.CaregiverId), "CaregiverId is required.");
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

            if (string.IsNullOrWhiteSpace(addCertificationRequest.Certificate))
            {
                ModelState.AddModelError(nameof(addCertificationRequest.Certificate),
                    "Certificate image data is required");
            }
            else
            {
                // Validate base64 format
                try
                {
                    Convert.FromBase64String(addCertificationRequest.Certificate);
                }
                catch (FormatException)
                {
                    ModelState.AddModelError(nameof(addCertificationRequest.Certificate),
                        "Certificate must be valid base64 image data");
                }
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
