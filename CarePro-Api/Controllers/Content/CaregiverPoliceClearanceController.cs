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
    /// Caregiver police clearance (vetting step). Same rules as <see cref="CaregiverVettingController"/>: the caregiver id is
    /// taken from the JWT only — there is no id in the route, body or query, and any "caregiverId" a client sends is ignored.
    /// Admins get 403 here (role check); staff use /api/admin/police-clearances.
    /// </summary>
    [Route("api/caregiver/vetting/police-clearance")]
    [ApiController]
    [Authorize(Roles = "Caregiver")]
    public class CaregiverPoliceClearanceController : ControllerBase
    {
        // 25 MB hard cap on the whole request (default 20 MB total of files + multipart overhead). Larger -> 413, not 500.
        private const long RequestCap = 26_214_400;

        private readonly IPoliceClearanceService _service;
        private readonly IConfiguration _configuration;
        private readonly ILogger<CaregiverPoliceClearanceController> _logger;

        public CaregiverPoliceClearanceController(IPoliceClearanceService service, IConfiguration configuration,
            ILogger<CaregiverPoliceClearanceController> logger)
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
                    _logger.LogWarning("Police clearance access refused during {Action}: {Message}", action, u.Message);
                    return StatusCode(403, new { message = u.Message });
                case InvalidOperationException io: return StatusCode(503, new { message = io.Message });
                default:
                    _logger.LogError(ex, "Unexpected error during {Action}", action);
                    return StatusCode(500, new { message = "An unexpected error occurred." });
            }
        }

        /// <summary>Latest status, reason (if rejected/revoked), issue date, resubmit flag and a short history. Own data only.</summary>
        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var id = CurrentCaregiverId();
            if (string.IsNullOrWhiteSpace(id)) return Unauthorized(new { message = "Caregiver identity not found in token." });
            try { return Ok(await _service.GetStatusAsync(id)); }
            catch (Exception ex) { return Handle(ex, "GetPoliceClearance"); }
        }

        /// <summary>multipart: issueDate, nameOnDocument, referenceNumber, plus 1–5 files (one PDF, or up to 5 JPG/PNG images).</summary>
        [HttpPost]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(RequestCap)]
        [RequestFormLimits(MultipartBodyLengthLimit = RequestCap)]
        public async Task<IActionResult> Submit()
        {
            var id = CurrentCaregiverId();
            if (string.IsNullOrWhiteSpace(id)) return Unauthorized(new { message = "Caregiver identity not found in token." });
            try
            {
                var settings = PoliceClearanceSettings.Load(_configuration);
                IFormCollection form;
                try { form = await Request.ReadFormAsync(); }
                catch (Exception ex) when (ex is InvalidDataException || ex is BadHttpRequestException || ex is IOException)
                {
                    return BadRequest(new { message = $"We couldn't read the upload. Please keep the files within {settings.MaxTotalSizeMB} MB in total and try again." });
                }

                var request = new SubmitPoliceClearanceRequest
                {
                    IssueDate = form["issueDate"].ToString(),
                    NameOnDocument = form["nameOnDocument"].ToString(),
                    ReferenceNumber = form["referenceNumber"].ToString()
                };

                // Cheap checks BEFORE reading anything into memory.
                if (form.Files.Count > settings.MaxFiles)
                    return BadRequest(new { message = $"You can attach at most {settings.MaxFiles} files." });
                foreach (var f in form.Files)
                {
                    if (f.Length > settings.MaxFileSizeBytes)
                        return BadRequest(new { message = $"Each file must be {settings.MaxFileSizeMB} MB or smaller." });
                    using var ms = new MemoryStream();
                    await f.CopyToAsync(ms);
                    request.Files.Add(new ClearanceUploadFile { FileName = f.FileName, ContentType = f.ContentType, Bytes = ms.ToArray() });
                }

                return Ok(await _service.SubmitAsync(id, request));
            }
            catch (Exception ex) { return Handle(ex, "SubmitPoliceClearance"); }
        }

        /// <summary>Streams one of the caller's own files. Someone else's record is 403.</summary>
        [HttpGet("{clearanceId}/files/{index:int}")]
        public async Task<IActionResult> GetFile(string clearanceId, int index)
        {
            var id = CurrentCaregiverId();
            if (string.IsNullOrWhiteSpace(id)) return Unauthorized(new { message = "Caregiver identity not found in token." });
            try
            {
                var file = await _service.GetOwnFileAsync(id, clearanceId, index);
                return ClearanceFileResult.From(this, file);
            }
            catch (Exception ex) { return Handle(ex, "GetPoliceClearanceFile"); }
        }
    }

    internal static class ClearanceFileResult
    {
        /// <summary>Streams the bytes with caching disabled — these are sensitive personal documents.</summary>
        public static IActionResult From(ControllerBase controller, ClearanceFileContent file)
        {
            controller.Response.Headers["Cache-Control"] = "private, no-store";
            controller.Response.Headers["X-Content-Type-Options"] = "nosniff";
            return controller.File(file.Bytes, file.ContentType);
        }
    }
}
