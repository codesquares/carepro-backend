using Application.DTOs;
using Domain.Entities;
using Application.Interfaces;
using Application.Interfaces.Content;
using Infrastructure.Content.Data;
using Infrastructure.Content.Helpers;
using Infrastructure.Content.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using System.Text.Json;

namespace CarePro_Api.Controllers.Content
{
    [Route("api/[controller]")]
    [ApiController]
    // [Authorize(Roles = "Caregiver,Admin")] // was class-level: ANDed with every action's own attribute, so SuperAdmin (who is in
    // OperationsPolicy) was rejected on every route. Now: any signed-in user at class level, and EVERY action states its own rule.
    [Authorize]
    public class CertificatesController : ControllerBase
    {
        private readonly CareProDbContext careProDbContext;
        private readonly ICertificationService certificationService;
        private readonly ILogger<CertificatesController> logger;
        private readonly ICareGiverService careGiverService;
        private readonly IAuthorizationService authorizationService;

        public CertificatesController(CareProDbContext careProDbContext, ICertificationService certificationService, ILogger<CertificatesController> logger, ICareGiverService careGiverService, IAuthorizationService authorizationService)
        {
            this.authorizationService = authorizationService;
            this.careProDbContext = careProDbContext;
            this.certificationService = certificationService;
            this.logger = logger;
            this.careGiverService = careGiverService;
        }

        // ---- Ownership helpers -------------------------------------------------------------------------
        // Identity ALWAYS comes from the JWT. A caregiverId in a body/query is attacker-controlled and is only
        // ever compared against the JWT, never trusted. "Allowed admin" = the same OperationsPolicy that guards
        // every other vetting/review route (HR, ComplianceAndLegal, CareLeads, or SuperAdmin).
        private string? CurrentUserId() =>
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirst("sub")?.Value
            ?? User.FindFirst("userId")?.Value;

        private async Task<bool> IsOperationsAdminAsync() =>
            (await authorizationService.AuthorizeAsync(User, "OperationsPolicy")).Succeeded;

        private string CurrentAdminEmail() =>
            User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirst("email")?.Value ?? string.Empty;

        /// <summary>
        /// Records an admin action on a certificate in AdminAuditLogs. The acting admin always comes from the JWT.
        /// A failure to write the audit row is logged but does not undo an action that already happened.
        /// </summary>
        private async Task WriteAdminAuditAsync(string action, string certificateId, string caregiverId, string reason, string? afterJson = null)
        {
            try
            {
                careProDbContext.AdminAuditLogs.Add(new AdminAuditLog
                {
                    Id = MongoDB.Bson.ObjectId.GenerateNewId(),
                    AdminId = CurrentUserId() ?? string.Empty,
                    AdminEmail = CurrentAdminEmail(),
                    TargetEntityType = "Certificate",
                    TargetEntityId = certificateId,
                    TargetUserId = caregiverId,
                    Action = action,
                    AfterJson = afterJson,
                    Reason = reason,
                    Timestamp = DateTime.UtcNow
                });
                await careProDbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to write the admin audit log entry '{Action}' for certificate {CertificateId}", action, certificateId);
            }
        }

        private IActionResult ForbiddenResult(string message) =>
            StatusCode(StatusCodes.Status403Forbidden, new { success = false, message });

        private IActionResult CertificateNotFound(string certificateId) =>
            NotFound(new { message = $"Certificate with ID '{certificateId}' not found." });

        /// <summary>
        /// Resolves the caregiver id an upload is for. Always the JWT caregiver. A different id in the body is
        /// rejected (403) rather than silently re-targeted, so a tampered client is visible in the logs.
        /// </summary>
        private IActionResult? ResolveUploadCaregiver(string? bodyCaregiverId, out string caregiverId)
        {
            caregiverId = CurrentUserId() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(caregiverId))
                return Unauthorized(new { success = false, message = "Unable to identify the signed-in caregiver." });
            if (!string.IsNullOrWhiteSpace(bodyCaregiverId) && bodyCaregiverId != caregiverId)
            {
                logger.LogWarning("Certificate upload rejected: body CaregiverId differs from the JWT caregiver {CallerId}", caregiverId);
                return ForbiddenResult("You can only upload certificates for your own account.");
            }
            return null;
        }

        /// <summary>
        /// [DEPRECATED FALLBACK] Upload a new certificate with automatic verification
        /// using a JSON body containing a base64-encoded file.
        ///
        /// All clients should prefer the multipart/form-data variant on the same route
        /// (POST api/Certificates with Content-Type: multipart/form-data). Multipart streams
        /// the file as a binary form part instead of carrying it as a base64 string in JSON,
        /// which avoids the multi-copy in-memory blow-up that causes silent failures on
        /// low-memory mobile browsers and reduces request payload size by ~33%.
        ///
        /// This endpoint is retained only as a fallback for legacy desktop clients that
        /// cannot send multipart and will be removed in a future release.
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "Caregiver")]
        [Consumes("application/json")]
        [Obsolete("Use the multipart/form-data variant of POST api/Certificates. This base64/JSON path is kept as a fallback for legacy clients and will be removed.")]
        public async Task<IActionResult> AddCertificateAsync([FromBody] AddCertificationRequest addCertificationRequest)
        {
            try
            {
                logger.LogWarning(
                    "Deprecated certificate upload path used: caregiver POST api/Certificates (application/json, base64). CaregiverId={CaregiverId}",
                    addCertificationRequest?.CaregiverId);

                // Identity from the JWT, never the body.
                if (addCertificationRequest != null)
                {
                    var denied = ResolveUploadCaregiver(addCertificationRequest.CaregiverId, out var jwtCaregiverId);
                    if (denied != null) return denied;
                    addCertificationRequest.CaregiverId = jwtCaregiverId;
                }

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
        /// [PRIMARY] Upload a new certificate with automatic verification (multipart/form-data).
        /// This is the recommended path for all clients — the file is streamed as a binary
        /// form part instead of a base64 string in JSON, which avoids the multi-copy
        /// in-memory blow-up that causes silent XHR failures on low-memory mobile browsers
        /// and reduces request payload size by ~33%.
        ///
        /// Maximum accepted upload size: 10 MB (request body capped at ~12 MB to allow for
        /// multipart overhead and accompanying form fields).
        /// Same response shape as the legacy JSON endpoint (CertificationUploadResponse).
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

                var deniedUpload = ResolveUploadCaregiver(formRequest.CaregiverId, out var jwtCaregiverId);
                if (deniedUpload != null) return deniedUpload;

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
                byte[] certificateBytes;
                using (var ms = new MemoryStream())
                {
                    await formRequest.Certificate.CopyToAsync(ms);
                    certificateBytes = ms.ToArray();
                }

                // Type check by declared content type AND magic bytes (never the file name): PDF, JPG, PNG only.
                var fileError = CertificateFileValidator.Validate(certificateBytes, formRequest.Certificate.ContentType);
                if (fileError != null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = fileError,
                        errors = new Dictionary<string, string[]> { { nameof(formRequest.Certificate), new[] { fileError } } }
                    });
                }

                var base64Certificate = Convert.ToBase64String(certificateBytes);

                var addCertificationRequest = new AddCertificationRequest
                {
                    CertificateName = formRequest.CertificateName,
                    // CaregiverId = formRequest.CaregiverId, // was: trusted the form field (any caregiver could upload as another)
                    CaregiverId = jwtCaregiverId,
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
        /// [DEPRECATED FALLBACK] Admin upload — create a certificate on behalf of a caregiver
        /// using a JSON body containing a base64-encoded file.
        ///
        /// Prefer the multipart/form-data variant of POST api/Certificates/admin/{caregiverId}.
        /// Retained only as a fallback for legacy admin clients and will be removed.
        ///
        /// The caregiver is identified by the {caregiverId} route parameter; any CaregiverId
        /// present in the request body is overwritten with the route value to prevent the
        /// admin from accidentally targeting the wrong account.
        ///
        /// Reuses the same service pipeline as the caregiver-facing endpoint (validation,
        /// Cloudinary upload, persistence, notification to the caregiver). Emits an audit
        /// log entry capturing the acting admin's id.
        /// </summary>
        [HttpPost("admin/{caregiverId}")]
        // [Authorize(Roles = "Admin,SuperAdmin")] // was: every admin department (Finance included) could upload on a caregiver's behalf
        [Authorize(Policy = "OperationsPolicy")]
        [Consumes("application/json")]
        [Obsolete("Use the multipart/form-data variant of POST api/Certificates/admin/{caregiverId}. This base64/JSON path is kept as a fallback for legacy clients and will be removed.")]
        public async Task<IActionResult> AdminAddCertificateAsync(string caregiverId, [FromBody] AddCertificationRequest addCertificationRequest)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(caregiverId))
                {
                    return BadRequest(new { success = false, message = "CaregiverId route parameter is required." });
                }

                if (addCertificationRequest == null)
                {
                    return BadRequest(new { success = false, message = "Request body cannot be empty." });
                }

                logger.LogWarning(
                    "Deprecated certificate upload path used: admin POST api/Certificates/admin/{CaregiverId} (application/json, base64)",
                    caregiverId);

                // Route param wins — admin cannot target a different caregiver via the body.
                addCertificationRequest.CaregiverId = caregiverId;

                if (!(await ValidateAddCertificateAsync(addCertificationRequest)))
                {
                    return BadRequest(ModelState);
                }

                var result = await certificationService.CreateCertificateAsync(addCertificationRequest);

                // var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier); // was: NameIdentifier only (not what the login token carries)
                var adminId = CurrentUserId();
                logger.LogInformation(
                    "Audit Event: Admin {AdminId} uploaded certificate '{CertificateName}' on behalf of caregiver {CaregiverId} at {Timestamp}",
                    adminId, addCertificationRequest.CertificateName, caregiverId, DateTime.UtcNow);
                await WriteAdminAuditAsync("CertificateAdminUpload", result.CertificateId ?? string.Empty, caregiverId,
                    "Admin uploaded a certificate on behalf of the caregiver (json)", $"{{\"certificateName\":{JsonSerializer.Serialize(addCertificationRequest.CertificateName)}}}");

                return Ok(new
                {
                    success = true,
                    message = "Certificate uploaded successfully on behalf of caregiver",
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
                return NotFound(new { success = false, message = ex.Message });
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
                logger.LogError(ex, "An unexpected error occurred during admin certificate upload for caregiver {CaregiverId}", caregiverId);
                return StatusCode(500, new { ErrorMessage = "An error occurred on the server." });
            }
        }

        /// <summary>
        /// [PRIMARY] Admin upload — create a certificate on behalf of a caregiver
        /// (multipart/form-data). This is the recommended admin upload path; the base64/JSON
        /// variant on the same route is retained only as a fallback for legacy clients.
        /// Same semantics as <see cref="AdminAddCertificateAsync"/>.
        /// </summary>
        [HttpPost("admin/{caregiverId}")]
        // [Authorize(Roles = "Admin,SuperAdmin")] // was: every admin department (Finance included) could upload on a caregiver's behalf
        [Authorize(Policy = "OperationsPolicy")]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(12_582_912)] // 12 MB request body cap (10 MB file + multipart overhead)
        [RequestFormLimits(MultipartBodyLengthLimit = 12_582_912)]
        public async Task<IActionResult> AdminAddCertificateFormAsync(string caregiverId, [FromForm] AddCertificationFormRequest formRequest)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(caregiverId))
                {
                    return BadRequest(new { success = false, message = "CaregiverId route parameter is required." });
                }

                if (formRequest == null)
                {
                    return BadRequest(new { success = false, message = "Request cannot be empty." });
                }

                if (formRequest.Certificate == null || formRequest.Certificate.Length == 0)
                {
                    ModelState.AddModelError(nameof(formRequest.Certificate), "Certificate file is required.");
                    return BadRequest(ModelState);
                }

                const long maxFileSizeBytes = 10L * 1024 * 1024; // 10 MB
                if (formRequest.Certificate.Length > maxFileSizeBytes)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Certificate file is too large. Maximum allowed size is 10 MB.",
                        errors = new Dictionary<string, string[]>
                        {
                            { nameof(formRequest.Certificate), new[] { "File exceeds the 10 MB upload limit." } }
                        }
                    });
                }

                byte[] certificateBytes;
                using (var ms = new MemoryStream())
                {
                    await formRequest.Certificate.CopyToAsync(ms);
                    certificateBytes = ms.ToArray();
                }

                // PDF, JPG, PNG only - by declared content type AND magic bytes.
                var fileError = CertificateFileValidator.Validate(certificateBytes, formRequest.Certificate.ContentType);
                if (fileError != null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = fileError,
                        errors = new Dictionary<string, string[]> { { nameof(formRequest.Certificate), new[] { fileError } } }
                    });
                }

                var base64Certificate = Convert.ToBase64String(certificateBytes);

                var addCertificationRequest = new AddCertificationRequest
                {
                    CertificateName = formRequest.CertificateName,
                    CaregiverId = caregiverId, // route param wins
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

                // var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier); // was: NameIdentifier only (not what the login token carries)
                var adminId = CurrentUserId();
                logger.LogInformation(
                    "Audit Event: Admin {AdminId} uploaded certificate '{CertificateName}' (multipart) on behalf of caregiver {CaregiverId} at {Timestamp}",
                    adminId, addCertificationRequest.CertificateName, caregiverId, DateTime.UtcNow);
                await WriteAdminAuditAsync("CertificateAdminUpload", result.CertificateId ?? string.Empty, caregiverId,
                    "Admin uploaded a certificate on behalf of the caregiver (multipart)", $"{{\"certificateName\":{JsonSerializer.Serialize(addCertificationRequest.CertificateName)}}}");

                return Ok(new
                {
                    success = true,
                    message = "Certificate uploaded successfully on behalf of caregiver",
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
                return NotFound(new { success = false, message = ex.Message });
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
                logger.LogError(ex, "An unexpected error occurred during admin multipart certificate upload for caregiver {CaregiverId}", caregiverId);
                return StatusCode(500, new { ErrorMessage = "An error occurred on the server." });
            }
        }

        /// <summary>
        /// Returns the canonical list of certificate types accepted by the upload endpoints.
        ///
        /// The upload endpoints reject any CertificateName that is not an exact match for one
        /// of these strings (after Trim), so the frontend should use this list as the source
        /// of truth for its "Certificate type" dropdown rather than hard-coding labels. Each
        /// item also includes:
        ///   - category: educational | professional | medical | specialized
        ///   - expectedIssuer: the canonical issuer string the backend expects in CertificateIssuer
        ///   - flexibleIssuer: when true, any non-empty CertificateIssuer is accepted; when
        ///     false (educational certificates), CertificateIssuer must equal expectedIssuer
        ///     (case-insensitive). Use this to decide whether the issuer field is a free-text
        ///     input or a read-only label.
        ///   - serviceCategories: the service categories this certificate helps the caregiver
        ///     qualify for. Useful for showing the user which services unlock once verified.
        ///
        /// Anonymous endpoint — safe to call before login (e.g. during signup) for prefetching.
        /// Cached for 1 hour at the client; the list is effectively static.
        /// </summary>
        [HttpGet("types")]
        [AllowAnonymous]
        [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
        public IActionResult GetCertificateTypes()
        {
            var types = ApprovedCertificates.ValidCertificateNames
                .Select(name => new
                {
                    name,
                    category = ApprovedCertificates.GetCertificateCategory(name),
                    expectedIssuer = CertificateValidationHelper.GetExpectedIssuer(name),
                    flexibleIssuer = ApprovedCertificates.FlexibleIssuerCertificates.Contains(name),
                    serviceCategories = ApprovedCertificates.GetServiceCategories(name) ?? new List<string>()
                })
                .ToList();

            return Ok(new
            {
                success = true,
                message = "Accepted certificate types",
                data = types
            });
        }

        /// <summary>
        /// Get all certificates for a caregiver
        /// </summary>
        [HttpGet]
        [Authorize(Roles = "Caregiver,Admin,SuperAdmin")] // non-caregivers must additionally pass OperationsPolicy (checked in the action)
        public async Task<IActionResult> GetAllCertificatesAsync(string? caregiverId = null)
        {
            try
            {
                if (User.IsInRole("Caregiver"))
                {
                    var callerId = CurrentUserId();
                    if (string.IsNullOrWhiteSpace(callerId))
                        return Unauthorized(new { success = false, message = "Unable to identify the signed-in caregiver." });
                    // A caregiver only ever gets their own; omitting the id means "mine".
                    if (string.IsNullOrWhiteSpace(caregiverId)) caregiverId = callerId;
                    else if (caregiverId != callerId)
                        return ForbiddenResult("You can only view your own certificates.");
                }
                else if (!await IsOperationsAdminAsync())
                {
                    return ForbiddenResult("Your department is not permitted to view caregiver certificates.");
                }

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
        [Authorize(Roles = "Caregiver,Admin,SuperAdmin")] // non-caregivers must additionally pass OperationsPolicy (checked in the action)
        public async Task<IActionResult> GetCertificateAsync(string certificateId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(certificateId))
                {
                    return BadRequest(new { message = "CertificateId is required" });
                }

                logger.LogInformation($"Retrieving certificate: {certificateId}");

                if (!User.IsInRole("Caregiver") && !await IsOperationsAdminAsync())
                    return ForbiddenResult("Your department is not permitted to view caregiver certificates.");

                var certificate = await certificationService.GetCertificateAsync(certificateId);

                // Not the owner -> 404, identical to a missing certificate (do not reveal existence).
                if (User.IsInRole("Caregiver") && certificate.CaregiverId != CurrentUserId())
                    return CertificateNotFound(certificateId);

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
        [Authorize(Policy = "OperationsPolicy")]
        public async Task<IActionResult> DeleteAllCaregiverCertificatesAsync(string caregiverId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(caregiverId))
                {
                    return BadRequest(new { message = "CaregiverId is required" });
                }

                logger.LogInformation($"Deleting all certificates for caregiver: {caregiverId}");

                var countBefore = await careProDbContext.Certifications.CountAsync(c => c.CaregiverId == caregiverId);

                await certificationService.DeleteAllCaregiverCertificatesAsync(caregiverId);

                await WriteAdminAuditAsync("CertificateAdminBulkDelete", caregiverId, caregiverId,
                    "Admin deleted all of a caregiver's certificates", $"{{\"certificatesDeleted\":{countBefore}}}");

                return Ok(new
                {
                    success = true,
                    message = "All certificates deleted successfully"
                });
            }
            catch (CertificateFileDeletionException ex)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new { success = false, message = ex.Message });
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
        [Authorize(Roles = "Caregiver,Admin,SuperAdmin")] // non-caregivers must additionally pass OperationsPolicy (checked in the action)
        public async Task<IActionResult> DeleteCertificateAsync(string certificateId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(certificateId))
                {
                    return BadRequest(new { message = "CertificateId is required" });
                }

                logger.LogInformation($"Deleting certificate: {certificateId}");

                if (!User.IsInRole("Caregiver") && !await IsOperationsAdminAsync())
                    return ForbiddenResult("Your department is not permitted to delete caregiver certificates.");

                // Look up first: a non-owner caregiver gets the same 404 as a missing certificate, and the rule below needs the status.
                var existing = await certificationService.GetCertificateAsync(certificateId);
                var isCaregiver = User.IsInRole("Caregiver");
                if (isCaregiver && existing.CaregiverId != CurrentUserId())
                    return CertificateNotFound(certificateId);

                // RULE (owner can reverse): a caregiver cannot remove an APPROVED certificate - that would silently drop an eligibility
                // requirement. Pending, under-review and rejected ones can still be removed by their owner; Operations admins can
                // remove any, and every admin delete is audited.
                if (isCaregiver && (existing.VerificationStatus == DocumentVerificationStatus.Verified || existing.IsVerified))
                {
                    return ForbiddenResult("Approved certificates cannot be removed here. Please contact support.");
                }

                await certificationService.DeleteCertificateAsync(certificateId);

                if (!isCaregiver)
                {
                    await WriteAdminAuditAsync("CertificateAdminDelete", certificateId, existing.CaregiverId ?? string.Empty,
                        "Admin deleted a caregiver's certificate",
                        $"{{\"certificateName\":{JsonSerializer.Serialize(existing.CertificateName)},\"statusBeforeDelete\":\"{existing.VerificationStatus}\"}}");
                }

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
            catch (CertificateFileDeletionException ex)
            {
                // The file is still in storage, so the record was kept: say so instead of reporting success.
                return StatusCode(StatusCodes.Status502BadGateway, new { success = false, message = ex.Message });
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
        [Authorize(Policy = "OperationsPolicy")]
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
                    var decoded = Convert.FromBase64String(addCertificationRequest.Certificate);
                    // No content type on this path, so magic bytes only: PDF, JPG or PNG.
                    var fileError = CertificateFileValidator.Validate(decoded, declaredContentType: null);
                    if (fileError != null)
                    {
                        ModelState.AddModelError(nameof(addCertificationRequest.Certificate), fileError);
                    }
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
