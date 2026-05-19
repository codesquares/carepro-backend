using Application.DTOs;
using Application.DTOs.Authentication;
using Application.Interfaces.Authentication;
using Application.Interfaces.Content;
using Infrastructure.Content.Data;
using Infrastructure.Content.Services;
using Infrastructure.Content.Services.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
//using Microsoft.AspNetCore.Identity.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.Web.CodeGenerators.Mvc.Templates.BlazorIdentity.Pages.Manage;
using System;
using System.Security.Authentication;
using System.Security.Claims;


namespace CarePro_Api.Controllers.Content
{
    [Route("api/[controller]")]
    [ApiController]
    public class CareGiversController : ControllerBase
    {
        private readonly CareProDbContext careProDbContext;
        private readonly ICareGiverService careGiverService;
        private readonly ILogger<CareGiversController> logger;
        private readonly IHttpContextAccessor httpContextAccessor;
        private readonly IGoogleAuthService _googleAuthService;
        private readonly IUserDeletionService _userDeletionService;

        public CareGiversController(
            CareProDbContext careProDbContext, 
            ICareGiverService careGiverService, 
            ILogger<CareGiversController> logger, 
            IHttpContextAccessor httpContextAccessor,
            IGoogleAuthService googleAuthService,
            IUserDeletionService userDeletionService)
        {
            this.careProDbContext = careProDbContext;
            this.careGiverService = careGiverService;
            this.logger = logger;
            this.httpContextAccessor = httpContextAccessor;
            _googleAuthService = googleAuthService;
            _userDeletionService = userDeletionService;
        }

        /// <summary>
        /// Sign up as Caregiver using Google account
        /// User has selected "Caregiver" on the role selection screen
        /// </summary>
        [HttpPost("GoogleSignUp")]
        [AllowAnonymous]
        public async Task<IActionResult> GoogleSignUp([FromBody] GoogleSignUpRequest request)
        {
            try
            {
                HttpContext httpContext = httpContextAccessor.HttpContext!;
                string origin = httpContext.Request.Headers["Origin"].FirstOrDefault()
                                ?? $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";

                var (response, conflict) = await _googleAuthService.GoogleSignUpCaregiverAsync(request, origin);

                if (conflict != null)
                {
                    // Account already exists - prompt to link
                    return Conflict(new { 
                        message = conflict.Message, 
                        requiresLinking = conflict.CanLinkAccounts,
                        conflict 
                    });
                }

                logger.LogInformation("New Caregiver created via Google OAuth: {Email}", response?.Email);
                return Ok(response);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during Google sign up for Caregiver");
                return StatusCode(500, new { ErrorMessage = "An error occurred during Google sign up.", Details = ex.Message });
            }
        }

        /// ENDPOINT TO CREATE  CARE GIVER USERS TO THE DATABASE        
        [HttpPost]
        [Route("AddCaregiverUser")]
        public async Task<IActionResult> AddCaregiverUserAsync([FromBody] AddCaregiverRequest addCaregiverRequest)
        {
            try
            {
                // Validate the incoming request
                if (!(await ValidateAddCaregiverAsync(addCaregiverRequest)))
                {
                    return BadRequest(ModelState);
                }



                HttpContext? httpContext = httpContextAccessor.HttpContext;

                // Get frontend origin from the request
                string origin = httpContext?.Request.Headers["Origin"].FirstOrDefault()
                                ?? $"{httpContext?.Request.Scheme}://{httpContext?.Request.Host}";




                // Pass Domain Object to Repository to Persist this
                var careGiverUser = await careGiverService.CreateCaregiverUserAsync(addCaregiverRequest, origin);

                // Send DTO response back to ClientUser
                return Ok(careGiverUser);
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { Message = ex.Message }); // Or BadRequest
            }
            catch (AuthenticationException authEx)
            {
                // Handle authentication-related exceptions
                return BadRequest(new { StatusCode = 400, ErrorMessage = authEx.Message });
            }
            catch (HttpRequestException httpEx)
            {
                // Handle HTTP request-related exceptions
                return StatusCode(500, new { StatusCode = 500, ErrorMessage = httpEx.Message });
            }
            catch (DbUpdateException dbEx)
            {
                // Handle database update-related exceptions
                return StatusCode(500, new { StatusCode = 500, ErrorMessage = dbEx.Message });
            }
            catch (Exception ex)
            {
                // Handle other exceptions
                return StatusCode(500, new { StatusCode = 500, ErrorMessage = ex.Message });
            }
        }



        #region Email Handling

        /// Confirm Email from the API
        //[HttpGet("confirm-email")]
        //public async Task<IActionResult> ConfirmEmail([FromQuery] string token)
        //{
        //    var result = await careGiverService.ConfirmEmailAsync(token);

        //    if (result.StartsWith("Account confirmed"))
        //        return Ok(result);

        //    return BadRequest(result);
        //}



        [HttpGet("validate-email-token")]
        public async Task<IActionResult> ValidateEmailToken([FromQuery] string token)
        {
            var result = await careGiverService.ValidateEmailTokenAsync(token);

            if (!result.IsValid)
                return BadRequest(new { success = false, message = result.ErrorMessage });

            return Ok(new
            {
                success = true,
                userId = result.UserId,
                email = result.Email
            });
        }

        /// Confirm Email from the Front-end
        [HttpPost("confirm-email")]
        public async Task<IActionResult> ConfirmEmail([FromBody] string userId)
        {
            var message = await careGiverService.ConfirmEmailFromFrontendAsync(userId);
            return Ok(new { message });
        }




        [HttpPost("resend-confirmation")]
        public async Task<IActionResult> ResendConfirmationEmail([FromBody] string email)
        {
            try
            {
                HttpContext? httpContext = httpContextAccessor.HttpContext;

                // Get frontend origin from the request
                string origin = httpContext?.Request.Headers["Origin"].FirstOrDefault()
                                ?? $"{httpContext?.Request.Scheme}://{httpContext?.Request.Host}";



                var result = await careGiverService.ResendEmailConfirmationAsync(email, origin);
                return Ok(new { message = result });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        #endregion




        #region get origin
        [HttpGet("GetOrigin")]
        public IActionResult GetOrigin()
        {
            // Retrieve the Origin header from the HTTP request
            HttpContext? httpContext = httpContextAccessor.HttpContext;

            // Get the Referer header of the incoming request
            string? referer = httpContext?.Request.Headers["Referer"];

            if (string.IsNullOrEmpty(referer))
            {
                // Referer header is missing or empty
                return BadRequest("Referer header not found in the request.");
            }
            else
            {
                // Referer header is present, extract the origin URL
                Uri? refererUri;
                if (Uri.TryCreate(referer, UriKind.Absolute, out refererUri))
                {
                    // Build the base URL with scheme, host, and port components
                    string origin = $"{refererUri.Scheme}://{refererUri.Host}:{refererUri.Port}";
                    return Ok($"Origin: {origin}");
                }
                else
                {
                    // Invalid Referer URL format
                    return BadRequest("Invalid Referer header value.");
                }
            }
        }

        /// <summary>
        /// Get all caregivers - public endpoint returns limited info (no email/phone/address)
        /// </summary>
        [HttpGet]
        [Route("AllCaregivers")]
        public async Task<IActionResult> GetAllCaregiverAsync()
        {
            try
            {
                logger.LogInformation("Retrieving all Caregivers (public)");
                // Use public response to protect PII (no email, phone, address)
                var caregivers = await careGiverService.GetAllCaregiverUserPublicAsync();
                return Ok(caregivers);
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { Message = ex.Message }); // Or BadRequest
            }
            catch (AuthenticationException authEx)
            {
                // Handle authentication-related exceptions
                return BadRequest(new { StatusCode = 400, ErrorMessage = authEx.Message });
            }
            catch (HttpRequestException httpEx)
            {
                // Handle HTTP request-related exceptions
                return StatusCode(500, new { StatusCode = 500, ErrorMessage = httpEx.Message });
            }
            catch (DbUpdateException dbEx)
            {
                // Handle database update-related exceptions
                return StatusCode(500, new { StatusCode = 500, ErrorMessage = dbEx.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { StatusCode = 500, ErrorMessage = ex.Message });
            }

        }

        /// <summary>
        /// Get all caregivers with full details - Admin only
        /// </summary>
        [HttpGet]
        [Route("AllCaregiversAdmin")]
        [Authorize(Policy = "OperationsPolicy")]
        public async Task<IActionResult> GetAllCaregiverAdminAsync()
        {
            try
            {
                logger.LogInformation("Retrieving all Caregivers (admin - full details)");
                var caregivers = await careGiverService.GetAllCaregiverUserAsync();
                return Ok(caregivers);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { StatusCode = 500, ErrorMessage = ex.Message });
            }
        }


        /// <summary>
        /// Get single caregiver - public endpoint returns limited info (no email/phone/address)
        /// </summary>
        [HttpGet]
        [Route("{caregiverId}")]
        public async Task<IActionResult> GetCaregiverAsync(string caregiverId)
        {
            try
            {
                logger.LogInformation("Retrieving Caregiver {CaregiverId} (public)", caregiverId);
                // Use public response to protect PII
                var caregiver = await careGiverService.GetCaregiverUserPublicAsync(caregiverId);
                return Ok(caregiver);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { StatusCode = 500, ErrorMessage = ex.Message });
            }
        }

        /// <summary>
        /// Get single caregiver with full details - Admin only
        /// </summary>
        [HttpGet]
        [Route("{caregiverId}/admin")]
        [Authorize(Policy = "OperationsPolicy")]
        public async Task<IActionResult> GetCaregiverAdminAsync(string caregiverId)
        {
            try
            {
                logger.LogInformation("Retrieving Caregiver {CaregiverId} (admin - full details)", caregiverId);
                var caregiver = await careGiverService.GetCaregiverUserAsync(caregiverId);
                return Ok(caregiver);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { StatusCode = 500, ErrorMessage = ex.Message });
            }
        }




        #endregion




        [HttpPut]
        [Route("UpdateCaregiverInfo/{caregiverId}")]
        //[Authorize(Roles = "Caregiver, Client, Admin")]
        public async Task<IActionResult> UpdateCaregiverAdditionalInfoAsync(string caregiverId, [FromForm] UpdateCaregiverAdditionalInfoRequest updateCaregiverAdditionalInfoRequest)
        {
            try
            {
                logger.LogInformation($"Caregiver with ID: {caregiverId} additional Information has been updated.");
                var caregiver = await careGiverService.UpdateCaregiverInformationAsync(caregiverId, updateCaregiverAdditionalInfoRequest);
                return Ok(caregiver);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
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



        [HttpPut]
        [Route("UpdateProfilePicture/{caregiverId}")]
        //[Authorize(Roles = "Caregiver, Client, Admin")]
        public async Task<IActionResult> UpdateProfilePictureAsync(string caregiverId, [FromForm] UpdateProfilePictureRequest updateProfilePictureRequest)
        {
            try
            {
                logger.LogInformation($"Caregiver with ID: {caregiverId} Profile Picture has been updated.");
                var caregiver = await careGiverService.UpdateProfilePictureAsync(caregiverId, updateProfilePictureRequest);
                return Ok(caregiver);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
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



        [HttpPut]
        [Route("UpdateCaregiverAboutMeInfo/{caregiverId}")]
        //[Authorize(Roles = "Caregiver, Client, Admin")]
        public async Task<IActionResult> UpdateCaregiverAboutMeAsync(string caregiverId, UpdateCaregiverAdditionalInfoRequest updateCaregiverAdditionalInfoRequest)
        {
            try
            {
                logger.LogInformation($"Caregiver with ID: {caregiverId} additional Information has been updated.");
                var caregiver = await careGiverService.UpdateCaregiverInformationAsync(caregiverId, updateCaregiverAdditionalInfoRequest);
                return Ok(caregiver);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
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



        [HttpPut]
        [Route("UpdateCaregiverAvailability/{caregiverId}")]
        //[Authorize(Roles = "Caregiver, Client, Admin")]
        public async Task<IActionResult> UpdateCaregiverAvailabilityAsync(string caregiverId, UpdateCaregiverAvailabilityRequest updateCaregiverAvailabilityRequest)
        {
            try
            {
                logger.LogInformation($"Caregiver with ID: {caregiverId} additional Information has been updated.");
                var caregiver = await careGiverService.UpdateCaregiverAvailabilityAsync(caregiverId, updateCaregiverAvailabilityRequest);
                return Ok(caregiver);
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



        [HttpPut]
        [Route("UpdateCaregiverLocation/{caregiverId}")]
        //[Authorize(Roles = "Caregiver, Admin")]
        public async Task<IActionResult> UpdateCaregiverLocationAsync(string caregiverId, [FromBody] UpdateCaregiverLocationRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                logger.LogInformation($"Updating location for caregiver with ID: {caregiverId}");
                var result = await careGiverService.UpdateCaregiverLocationAsync(caregiverId, request);
                return Ok(new
                {
                    success = true,
                    message = "Caregiver location updated successfully",
                    data = result
                });
            }
            catch (ArgumentException ex)
            {
                logger.LogWarning(ex, $"Invalid request data for updating caregiver {caregiverId} location");
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                logger.LogWarning(ex, $"Caregiver {caregiverId} not found");
                return NotFound(new { success = false, message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                logger.LogWarning(ex, $"Invalid operation for updating caregiver {caregiverId} location");
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error updating location for caregiver {caregiverId}");
                return StatusCode(500, new { success = false, message = "An error occurred while updating the caregiver location" });
            }
        }



        [HttpPut]
        [Route("SoftDeleteCaregiver/{caregiverId}")]
        //[Authorize(Roles = "Caregiver, Client, Admin")]
        public async Task<IActionResult> SoftDeleteCaregiverAsync(string caregiverId)
        {
            try
            {
                logger.LogInformation($"Caregiver with ID: {caregiverId} Soft Deleted");
                var caregiver = await careGiverService.SoftDeleteCaregiverAsync(caregiverId);
                return Ok(caregiver);
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



        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] ResetPasswordRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                await careGiverService.ChangePasswordAsync(request);
                return Ok(new { message = "Password changed successful." });
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                // Log exception here if needed
                return StatusCode(500, new { message = "An unexpected error occurred.", details = ex.Message });
            }
        }




        // ✅ This is for generating the token and sending the reset email
        [HttpPost("request-reset")]
        [AllowAnonymous] // Allow unauthenticated access
        public async Task<IActionResult> RequestPasswordReset([FromBody] PasswordResetRequestDto request)
        {
            try
            {
                HttpContext? httpContext = httpContextAccessor.HttpContext;

                // Determine the origin of the request (frontend/backend)
                string origin = httpContext?.Request.Headers["Origin"].FirstOrDefault()
                                ?? $"{httpContext?.Request.Scheme}://{httpContext?.Request.Host}";

                await careGiverService.GeneratePasswordResetTokenAsync(request, origin);
                return Ok(new { message = "A reset link has been sent to the registered Email." });
            }
            catch (InvalidOperationException ex)
            {
                // Handle business logic errors (e.g., user not found)
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                // Handle unexpected errors (e.g., email service failures)
                // Log the error for debugging
                // Note: In production, you should use proper logging
                Console.WriteLine($"Password reset error: {ex.Message}");
                
                return StatusCode(500, new { 
                    message = "Password reset request processed. If the email exists, a reset link will be sent.",
                    error = "Email service temporarily unavailable"
                });
            }
        }


        // ✅ This is for resetting the password using the token
        [HttpPost("resetPassword")]
        [AllowAnonymous] // Allow unauthenticated access
        public async Task<IActionResult> ResetPassword([FromBody] PasswordResetDto request)
        {
            try
            {
                await careGiverService.ResetPasswordWithJwtAsync(request);
                return Ok(new { message = "Password reset successful." });
            }
            catch (UnauthorizedAccessException ex)
            {
                // Handle invalid or expired token
                return Unauthorized(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                // Handle business logic errors (e.g., user not found)
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                // Handle unexpected errors
                logger.LogError(ex, "Error resetting password");
                return StatusCode(500, new { 
                    message = "An error occurred while resetting the password.",
                    error = ex.Message 
                });
            }
        }



        #region Validation Region

        private async Task<bool> ValidateAddCaregiverAsync(AddCaregiverRequest addCaregiverRequest)
        {
            if (addCaregiverRequest == null)
            {
                ModelState.AddModelError(nameof(addCaregiverRequest), $" cannot be empty.");
                return false;
            }

            // Email format validation
            var emailAttribute = new System.ComponentModel.DataAnnotations.EmailAddressAttribute();
            if (!emailAttribute.IsValid(addCaregiverRequest.Email))
            {
                ModelState.AddModelError(nameof(addCaregiverRequest.Email), "Invalid email format.");
                return false;
            }

            var user = await careProDbContext.CareGivers.FirstOrDefaultAsync(x => x.Email.ToLower() == addCaregiverRequest.Email.ToLower());
            if (user != null)
            {
                ModelState.AddModelError(nameof(addCaregiverRequest.Email),
                    "Email already exists. Kindly sign in or click on 'Forget Password'.");
                return false;
            }


            if (string.IsNullOrWhiteSpace(addCaregiverRequest.FirstName))
            {
                ModelState.AddModelError(nameof(addCaregiverRequest.FirstName),
                    $"{nameof(addCaregiverRequest.FirstName)} is required.");
            }

            if (string.IsNullOrWhiteSpace(addCaregiverRequest.LastName))
            {
                ModelState.AddModelError(nameof(addCaregiverRequest.LastName),
                    $"{nameof(addCaregiverRequest.LastName)} is required");
            }

            if (string.IsNullOrWhiteSpace(addCaregiverRequest.Role))
            {
                ModelState.AddModelError(nameof(addCaregiverRequest.Role),
                    $"{nameof(addCaregiverRequest.Role)} is required.");
            }



            if (ModelState.ErrorCount > 0)
            {
                return false;
            }

            return true;
        }



        #endregion

        #region Account Deletion

        /// <summary>
        /// Request deletion of the authenticated caregiver's own account.
        /// Schedules permanent anonymisation after a 30-day grace period.
        /// Blocked if active orders, pending withdrawals, or outstanding wallet balance exist.
        /// </summary>
        [HttpDelete("request-account-deletion")]
        [Authorize(Roles = "Caregiver")]
        public async Task<IActionResult> RequestAccountDeletion([FromBody] RequestAccountDeletionRequest request)
        {
            var caregiverId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                              ?? User.FindFirst("sub")?.Value
                              ?? User.FindFirst("userId")?.Value;

            if (string.IsNullOrEmpty(caregiverId))
                return Unauthorized(new { message = "Unable to identify user." });

            try
            {
                var origin = HttpContext.Request.Headers["Origin"].FirstOrDefault()
                             ?? $"{HttpContext.Request.Scheme}://{HttpContext.Request.Host}";

                var result = await _userDeletionService.RequestCaregiverAccountDeletionAsync(caregiverId, request?.Reason ?? string.Empty, origin);

                if (!result.Success)
                    return BadRequest(new { success = false, message = result.Message, blockers = result.Blockers });

                return Ok(new { success = true, message = result.Message, permanentDeletionDate = result.PermanentDeletionDate });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error requesting account deletion for caregiver {CaregiverId}", caregiverId);
                return StatusCode(500, new { message = "An error occurred while processing your deletion request." });
            }
        }

        /// <summary>
        /// Cancel a pending account deletion within the 30-day grace period.
        /// Restores the account and all gigs that were soft-deleted at the same time.
        /// </summary>
        [HttpPost("cancel-account-deletion")]
        [Authorize(Roles = "Caregiver")]
        public async Task<IActionResult> CancelAccountDeletion()
        {
            var caregiverId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                              ?? User.FindFirst("sub")?.Value
                              ?? User.FindFirst("userId")?.Value;

            if (string.IsNullOrEmpty(caregiverId))
                return Unauthorized(new { message = "Unable to identify user." });

            try
            {
                var message = await _userDeletionService.CancelCaregiverAccountDeletionAsync(caregiverId);
                return Ok(new { success = true, message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error cancelling account deletion for caregiver {CaregiverId}", caregiverId);
                return StatusCode(500, new { message = "An error occurred while cancelling your deletion request." });
            }
        }

        #endregion

        #region Token-based cancellation (email link)

        /// <summary>
        /// Unauthenticated endpoint consumed when the user clicks the cancellation deep link
        /// embedded in the deletion scheduled email. Validates the signed 30-day JWT,
        /// checks the purpose claim, then cancels the deletion.
        /// </summary>
        [HttpPost("cancel-account-deletion-by-token")]
        [AllowAnonymous]
        public async Task<IActionResult> CancelAccountDeletionByToken([FromQuery] string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return BadRequest(new { message = "Cancellation token is required." });

            try
            {
                var tokenHandler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                var jwtToken = tokenHandler.ReadJwtToken(token);

                var purpose = jwtToken.Claims.FirstOrDefault(c => c.Type == "purpose")?.Value;
                if (purpose != "account_deletion_cancel")
                    return BadRequest(new { message = "Invalid cancellation token." });

                var caregiverId = jwtToken.Claims.FirstOrDefault(c => c.Type == "userId")?.Value;
                if (string.IsNullOrEmpty(caregiverId))
                    return BadRequest(new { message = "Invalid cancellation token." });

                var message = await _userDeletionService.CancelCaregiverAccountDeletionAsync(caregiverId);
                return Ok(new { success = true, message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in token-based cancellation for caregiver");
                return StatusCode(500, new { message = "An error occurred while cancelling your deletion request." });
            }
        }

        #endregion
    }




}

