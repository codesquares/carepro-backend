using Application.DTOs;
using Application.Interfaces;
using Domain.Entities;
using Infrastructure.Content.Data;
using Infrastructure.Content.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

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
                // The gig is created FOR a caregiver, so the caller must be that caregiver (or an admin). The
                // caregiverId in the form is not trusted: a non-admin must be a Caregiver and can only create for
                // their own token identity; anything else is rejected before any validation or work happens.
                if (!IsAdminCaller())
                {
                    if (!User.IsInRole("Caregiver"))
                    {
                        return Forbid();
                    }

                    var callerId = CurrentUserId();
                    if (!string.IsNullOrWhiteSpace(addGigRequest?.CaregiverId) && addGigRequest!.CaregiverId != callerId)
                    {
                        logger.LogWarning("Blocked gig creation for another caregiver. CallerId: {CallerId}, RequestedCaregiverId: {Requested}", callerId, addGigRequest.CaregiverId);
                        return Forbid();
                    }

                    if (addGigRequest != null) addGigRequest.CaregiverId = callerId!;
                }

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
            catch (InvalidOperationException ex)
            {
                // Image moderation rejection — deserialize and return structured 422
                try
                {
                    var rejection = System.Text.Json.JsonSerializer.Deserialize<Application.DTOs.GigImageModerationResult>(ex.Message);
                    if (rejection != null && !rejection.IsApproved)
                    {
                        return UnprocessableEntity(new Application.DTOs.GigImageRejectionResponse
                        {
                            Reason = rejection.RejectionReason,
                            Suggestions = rejection.Suggestions
                        });
                    }
                }
                catch { /* not a moderation error — fall through */ }
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
            [FromQuery] string? category = null,
            [FromQuery] string? sort = null)
        {
            try
            {
                logger.LogInformation($"Retrieving all Gigs available");

                // If a sort or pageSize is specified (even without explicit page), use the
                // paginated path so the marketing/marketplace can request e.g. ?sort=newest&pageSize=4.
                if (page.HasValue || pageSize.HasValue || !string.IsNullOrWhiteSpace(sort))
                {
                    var paginatedGigs = await gigServices.GetAllGigsPaginatedAsync(
                        page ?? 1, pageSize ?? 20, status, search, category, sort);
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
                                // Caregiver-scoped listing: only that caregiver (or an admin) may read it.
                if (!IsGigOwnerOrAdmin(caregiverId))
                {
                    return Forbid();
                }

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
                                // Caregiver-scoped listing: only that caregiver (or an admin) may read it.
                if (!IsGigOwnerOrAdmin(caregiverId))
                {
                    return Forbid();
                }

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
                                // Caregiver-scoped listing: only that caregiver (or an admin) may read it.
                if (!IsGigOwnerOrAdmin(caregiverId))
                {
                    return Forbid();
                }

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
                                // Caregiver-scoped listing: only that caregiver (or an admin) may read it.
                if (!IsGigOwnerOrAdmin(caregiverId))
                {
                    return Forbid();
                }

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
        public async Task<IActionResult> GetGigAsync(string gigId)
        {
            try
            {
                logger.LogInformation($"Retrieving  Service with MessageId: {gigId}");

                var gig = await gigServices.GetGigAsync(gigId);
                var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? User.FindFirst("sub")?.Value
                    ?? User.FindFirst("userId")?.Value;

                // Special gigs are private offers tied to a specific care request.
                // They must only be visible to the scoped client, the assigned caregiver,
                // or an admin. Returning 404 (not 403) avoids leaking existence to others.
                if (gig.IsSpecialGig == true)
                {
                    var isAdmin = User.IsInRole("Admin") || User.IsInRole("SuperAdmin");
                    var isScopedClient = !string.IsNullOrEmpty(currentUserId)
                        && currentUserId == gig.ScopedClientId;
                    var isAssignedCaregiver = !string.IsNullOrEmpty(currentUserId)
                        && currentUserId == gig.CaregiverId;

                    if (!isAdmin && !isScopedClient && !isAssignedCaregiver)
                    {
                        return NotFound(new { message = $"Gig with ID '{gigId}' not found." });
                    }
                }

                // Only the owning caregiver and admins may see a gig that is not publicly listed (Draft, Paused,
                // or any other non-Published/Active state). 404 rather than 403 so existence isn't leaked.
                var isOwnerOrAdmin = IsGigOwnerOrAdmin(gig.CaregiverId);
                if (!isOwnerOrAdmin && !IsPubliclyListedStatus(gig.Status))
                {
                    return NotFound(new { message = $"Gig with ID '{gigId}' not found." });
                }

                var sessionId = Request.Headers["X-Session-Id"].FirstOrDefault()
                    ?? Request.Cookies["sessionId"]
                    ?? Request.Cookies["session_id"];
                var source = Request.Query["source"].FirstOrDefault();

                _ = gigServices.TrackGigViewAsync(gig.Id, currentUserId, sessionId, source)
                    .ContinueWith(t =>
                    {
                        if (t.Exception != null)
                        {
                            logger.LogWarning(t.Exception, "Failed to track view for gig {GigId}", gigId);
                        }
                    }, TaskScheduler.Default);

                // Clients (and other caregivers) must not learn who is behind a gig — the platform
                // assigns caregivers internally. Only the gig's own caregiver and admins get the name, the
                // caregiver id, the intro video (their face/voice) and their professional history; the rest
                // are omitted from the JSON entirely. Internal callers of IGigServices.GetGigAsync are
                // unaffected since this is response-only.
                if (!isOwnerOrAdmin)
                {
                    gig.CaregiverName = null!;
                    gig.CaregiverId = null!;
                    gig.VideoURL = null;
                    gig.CaregiverEducation = null!;
                    gig.CaregiverCertifications = null!;
                    gig.CaregiverWorkExperience = null!;
                }

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
                var (ownerId, failure) = await AuthorizeGigWriteAsync(gigId);
                if (failure != null) return failure;
                if (updateGigStatusToPauseRequest != null) updateGigStatusToPauseRequest.CaregiverId = ownerId!;

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
        public async Task<ActionResult<GigDTO>> UpdateGigAsync(string gigId, UpdateGigRequest updateGigRequest)
        {
            try
            {
                var (ownerId, failure) = await AuthorizeGigWriteAsync(gigId);
                if (failure != null) return failure;
                if (updateGigRequest != null) updateGigRequest.CaregiverId = ownerId!;

                var result = await gigServices.UpdateGigAsync(gigId, updateGigRequest);
                logger.LogInformation($"Gig Status with ID: {gigId} updated.");
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                // Image moderation rejection — deserialize and return structured 422
                try
                {
                    var rejection = System.Text.Json.JsonSerializer.Deserialize<Application.DTOs.GigImageModerationResult>(ex.Message);
                    if (rejection != null && !rejection.IsApproved)
                    {
                        return UnprocessableEntity(new Application.DTOs.GigImageRejectionResponse
                        {
                            Reason = rejection.RejectionReason,
                            Suggestions = rejection.Suggestions
                        });
                    }
                }
                catch { /* not a moderation error — fall through */ }
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
                // The caregiverId query parameter is kept for client compatibility but is ignored: ownership is
                // resolved from the gig and the caller's token.
                var (ownerId, failure) = await AuthorizeGigWriteAsync(gigId);
                if (failure != null) return failure;

                var result = await gigServices.SoftDeleteGigAsync(gigId, ownerId!);
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
        [Authorize(Policy = "OperationsPolicy")]
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

                // The audit trail must record who actually made the call, not whatever id the body claims.
                var auditAdminId = CurrentUserId();
                if (string.IsNullOrWhiteSpace(auditAdminId))
                {
                    return Unauthorized(new { message = "User not authenticated." });
                }

                var result = await gigServices.AdminBulkSoftDeleteGigsAsync(request.GigIds, request.DeleteAll, auditAdminId);

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
                var (ownerId, failure) = await AuthorizeGigWriteAsync(gigId);
                if (failure != null) return failure;

                var result = await gigServices.RestoreGigAsync(gigId, ownerId!);
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

                // Each DeletedGigDTO carries the caregiver's name, and this route had no ownership
                // check, so any signed-in user could read any caregiver's name by passing their ID.
                if (!IsGigOwnerOrAdmin(caregiverId))
                {
                    return Forbid();
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
        [Authorize(Policy = "OperationsPolicy")]
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


        /// <summary>True when the caller is the caregiver who owns the gig, or an admin.</summary>
        private bool IsGigOwnerOrAdmin(string? caregiverId)
        {
            if (User.IsInRole("Admin") || User.IsInRole("SuperAdmin"))
            {
                return true;
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User.FindFirst("sub")?.Value
                ?? User.FindFirst("userId")?.Value;

            return !string.IsNullOrEmpty(userId) && userId == caregiverId;
        }

        private string? CurrentUserId() =>
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirst("sub")?.Value
            ?? User.FindFirst("userId")?.Value;

        private bool IsAdminCaller() => User.IsInRole("Admin") || User.IsInRole("SuperAdmin");

        /// <summary>
        /// Authorizes a write to an existing gig. Ownership comes from the gig record and the caller's JWT —
        /// never from a caregiverId supplied in the request body/query, which is attacker-controlled. Returns the
        /// owning caregiver's id to hand to the service (so a request-supplied id can't be trusted downstream),
        /// or the failure result: 404 if the gig doesn't exist, 403 if the caller is neither its owner nor an admin.
        /// </summary>
        private async Task<(string? ownerId, ActionResult? failure)> AuthorizeGigWriteAsync(string gigId)
        {
            var ownerId = await gigServices.GetGigOwnerIdAsync(gigId);
            if (ownerId == null)
            {
                return (null, NotFound(new { message = $"Gig with ID '{gigId}' not found." }));
            }

            if (!IsGigOwnerOrAdmin(ownerId))
            {
                logger.LogWarning("Blocked gig write by non-owner. GigId: {GigId}, CallerId: {CallerId}", gigId, CurrentUserId());
                return (null, Forbid());
            }

            return (ownerId, null);
        }

        /// <summary>A gig is publicly visible only while Published or Active; Draft/Paused/etc. are owner+admin only.</summary>
        private static bool IsPubliclyListedStatus(string? status) =>
            string.Equals(status, "Published", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase);

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

            // A draft only needs a title and category — the rest is filled in before publishing.
            // Matches the frontend's own client-side gate (GigsForm.jsx handleSaveAsDraft).
            if (!string.Equals(addGigRequest.Status, "Draft", StringComparison.OrdinalIgnoreCase))
            {
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

                if (addGigRequest.Price <= 0)
                {
                    ModelState.AddModelError(nameof(addGigRequest.Price),
                        $"{nameof(addGigRequest.Price)} cannot be 0.");
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
