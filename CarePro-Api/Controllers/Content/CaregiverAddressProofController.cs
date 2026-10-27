using Application.DTOs;
using Application.Interfaces.Content;
using Infrastructure.Content.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Threading.Tasks;

namespace CarePro_Api.Controllers.Content
{
    /// <summary>
    /// Caregiver uploads a power-holding (NEPA) bill for their CURRENT address. The caregiver id comes from the JWT only;
    /// ownership of the address record is re-checked in the service. Admins get 403 here; staff use /api/admin/address-proofs.
    /// </summary>
    [Route("api/caregiver/vetting/address-history/{addressId}/proof")]
    [ApiController]
    [Authorize(Roles = "Caregiver")]
    public class CaregiverAddressProofController : ControllerBase
    {
        private const long RequestCap = 15_728_640; // 15 MB hard cap on the whole request; larger -> 413, not 500.

        private readonly IAddressProofService _service;
        private readonly IConfiguration _configuration;
        private readonly ILogger<CaregiverAddressProofController> _logger;

        public CaregiverAddressProofController(IAddressProofService service, IConfiguration configuration,
            ILogger<CaregiverAddressProofController> logger)
        {
            _service = service; _configuration = configuration; _logger = logger;
        }

        private string? CurrentCaregiverId() =>
            User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub")?.Value ?? User.FindFirst("userId")?.Value;

        private IActionResult Handle(Exception ex, string action)
        {
            switch (ex)
            {
                case PoliceClearanceValidationException v: return BadRequest(new { message = v.Message });
                case PoliceClearanceConflictException c: return Conflict(new { message = c.Message });
                case KeyNotFoundException k: return NotFound(new { message = k.Message });
                case UnauthorizedAccessException u:
                    _logger.LogWarning("Address proof access refused during {Action}: {Message}", action, u.Message);
                    return StatusCode(403, new { message = u.Message });
                case InvalidOperationException io: return StatusCode(503, new { message = io.Message });
                default:
                    _logger.LogError(ex, "Unexpected error during {Action}", action);
                    return StatusCode(500, new { message = "An unexpected error occurred." });
            }
        }

        /// <summary>multipart: billDate, nameOnBill, plus exactly one file (PDF, JPG or PNG).</summary>
        [HttpPost]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(RequestCap)]
        [RequestFormLimits(MultipartBodyLengthLimit = RequestCap)]
        public async Task<IActionResult> Submit(string addressId)
        {
            var id = CurrentCaregiverId();
            if (string.IsNullOrWhiteSpace(id)) return Unauthorized(new { message = "Caregiver identity not found in token." });
            try
            {
                var settings = AddressProofSettings.Load(_configuration);
                IFormCollection form;
                try { form = await Request.ReadFormAsync(); }
                catch (Exception ex) when (ex is InvalidDataException || ex is BadHttpRequestException || ex is IOException)
                {
                    return BadRequest(new { message = $"We couldn't read the upload. Please keep the file within {settings.MaxFileSizeMB} MB and try again." });
                }

                if (form.Files.Count > 1)
                    return BadRequest(new { message = "Please attach a single file." });

                var request = new SubmitAddressProofRequest
                {
                    BillDate = form["billDate"].ToString(),
                    NameOnBill = form["nameOnBill"].ToString()
                };
                // Cheap size check BEFORE reading anything into memory.
                foreach (var f in form.Files)
                {
                    if (f.Length > settings.ToFileSettings().MaxFileSizeBytes)
                        return BadRequest(new { message = $"The file must be {settings.MaxFileSizeMB} MB or smaller." });
                    using var ms = new MemoryStream();
                    await f.CopyToAsync(ms);
                    request.File = new ClearanceUploadFile { FileName = f.FileName, ContentType = f.ContentType, Bytes = ms.ToArray() };
                }

                return Ok(await _service.SubmitAsync(id, addressId, request));
            }
            catch (Exception ex) { return Handle(ex, "SubmitAddressProof"); }
        }

        /// <summary>Streams the caller's own bill. Someone else's record is 403.</summary>
        [HttpGet("file")]
        public async Task<IActionResult> GetFile(string addressId)
        {
            var id = CurrentCaregiverId();
            if (string.IsNullOrWhiteSpace(id)) return Unauthorized(new { message = "Caregiver identity not found in token." });
            try { return ClearanceFileResult.From(this, await _service.GetOwnFileAsync(id, addressId)); }
            catch (Exception ex) { return Handle(ex, "GetAddressProofFile"); }
        }
    }
}
