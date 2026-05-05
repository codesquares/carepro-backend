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
    [Authorize(Roles = "Caregiver,Admin")]
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
        /// Upload a new certificate with automatic verification (JSON / base64 body).
        /// Kept for backward compatibility with existing desktop clients.
        /// Mobile clients should prefer the multipart/form-data variant on the same route
        /// to avoid base64-in-JSON memory pressure on low-memory devices.
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "Caregiver")]
        [Consumes("application/json")]
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
        /// Upload a new certificate with automatic verification (multipart/form-data variant).
        /// Preferred path for mobile clients: the file is streamed as a binary form part
        /// instead of a base64 string in JSON, which avoids the multi-copy in-memory blow-up
        /// that causes silent XHR failures on low-memory mobile browsers.
        ///
        /// Maximum accepted upload size: 10 MB (request body capped at ~12 MB to allow for
        /// multipart overhead and accompanying form fields).
        /// Same response shape as the JSON endpoint (CertificationUploadResponse).
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "Caregiver")]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(12_582_912)] // 12 MB request body cap (10 MB file + multipart overhead)
        [RequestFormLimits(MultipartBodyLengthLimit = 12_582_912)]
        public async Task<IActionResult> AddCertificateFormAsync([FromForm] AddCertificationFormRequest formRequest)
        {
            try
            {
                if (formRequest == null)
                {
                    return BadRequest(new { success = false, message = "Request cannot be empty." });
                }

                if (formRequest.Certificate == null || formRequest.Certificate.Length == 0)
                {
                    ModelState.AddModelError(nameof(formRequest.Certificate), "Certificate file is required.");
                    return BadRequest(ModelState);
                }

                // Hard cap on the actual file size (defense in depth — RequestSizeLimit covers
                // the whole request including overhead; this enforces the documented 10 MB file limit).
                const long maxFileSizeBytes = 10L * 1024 * 1024; // 10 MB
                if (formRequest.Certificate.Length > maxFileSizeBytes)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = $"Certificate file is too large. Maximum allowed size is 10 MB.",
                        errors = new Dictionary<string, string[]>
                        {
                            { nameof(formRequest.Certificate), new[] { "File exceeds the 10 MB upload limit." } }
                        }
                    });
                }

                // Read the file into memory and convert to base64 so we can reuse the existing
                // service pipeline (Cloudinary upload, validation, persistence, notifications).
                // The service contract expects base64; rather than fork the pipeline we adapt here.
                string base64Certificate;
                using (var ms = new MemoryStream())
                {
                    await formRequest.Certificate.CopyToAsync(ms);
                    base64Certificate = Convert.ToBase64String(ms.ToArray());
                }

                var addCertificationRequest = new AddCertificationRequest
                {
                    CertificateName = formRequest.CertificateName,
                    CaregiverId = formRequest.CaregiverId,
                    CertificateIssuer = formRequest.CertificateIssuer,
                    CertificateCategory = formRequest.CertificateCategory,
                    Certificate = base64Certificate,
                    YearObtained = formRequest.YearObtained,
                    ExpiryDate = formRequest.ExpiryDate,
                    VerifyImmediately = formRequest.VerifyImmediately
                };

                if (!(await ValidateAddCertificateAsync(addCertificationRequest)))
                {
                    return BadRequest(ModelState);
                }

                var result = await certificationService.CreateCertificateAsync(addCertificationRequest);

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
                logger.LogError(ex, "An unexpected error occurred during multipart certificate upload");
                return StatusCode(500, new { ErrorMessage = "An error occurred on the server." });
            }
        }

        /// <summary>
        /// Get all certificates for a caregiver
        /// </summary>
        [HttpGet]
        [Authorize(Roles = "Caregiver,Admin")]
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
        [Authorize(Roles = "Caregiver,Admin")]
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
        [Authorize(Roles = "Admin")]
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
        [Authorize(Roles = "Caregiver,Admin")]
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
        [Authorize(Roles = "Admin")]
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
