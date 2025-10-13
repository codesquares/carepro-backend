using Application.DTOs;
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

        public CareGiversController(CareProDbContext careProDbContext, ICareGiverService careGiverService, ILogger<CareGiversController> logger, IHttpContextAccessor httpContextAccessor)
        {
            this.careProDbContext = careProDbContext;
            this.careGiverService = careGiverService;
            this.logger = logger;
            this.httpContextAccessor = httpContextAccessor;
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

                

                HttpContext httpContext = httpContextAccessor.HttpContext;

                // Get frontend origin from the request
                string origin = httpContext.Request.Headers["Origin"].FirstOrDefault()
                                ?? $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";

                


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
                HttpContext httpContext = httpContextAccessor.HttpContext;

                // Get frontend origin from the request
                string origin = httpContext.Request.Headers["Origin"].FirstOrDefault()
                                ?? $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";



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
            HttpContext httpContext = httpContextAccessor.HttpContext;

            // Get the Referer header of the incoming request
            string referer = httpContext.Request.Headers["Referer"];

            if (string.IsNullOrEmpty(referer))
            {
                // Referer header is missing or empty
                return BadRequest("Referer header not found in the request.");
            }
            else
            {
                // Referer header is present, extract the origin URL
                Uri refererUri;
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

        [HttpGet]
        [Route("AllCaregivers")]
        //[Authorize(Roles = "Client,Admin")]
        public async Task<IActionResult> GetAllCaregiverAsync()
        {
            try
            {
                logger.LogInformation("Retrieving all Caregivers");
                var caregivers = await careGiverService.GetAllCaregiverUserAsync();
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


        [HttpGet]
        [Route("{caregiverId}")]
        //[Authorize(Roles = "Client,Admin")]
        public async Task<IActionResult> GetCaregiverAsync(string caregiverId)
        {
            try
            {
                logger.LogInformation("Retrieving all Caregivers");
                var caregiver = await careGiverService.GetCaregiverUserAsync(caregiverId);
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
        public async Task<IActionResult> UpdateProfilePictureAsync(string caregiverId, [FromForm] UpdateProfilePictureRequest updateProfilePictureRequest )
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
        public async Task<IActionResult> UpdateCaregiverAvailabilityAsync(string caregiverId, UpdateCaregiverAvailabilityRequest  updateCaregiverAvailabilityRequest)
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
        [Route("SoftDeleteCaregiver/{caregiverId}")]
        //[Authorize(Roles = "Caregiver, Client, Admin")]
        public async Task<IActionResult> SoftDeleteCaregiverAsync(string caregiverId )
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
            HttpContext httpContext = httpContextAccessor.HttpContext;

            // Determine the origin of the request (frontend/backend)
            string origin = httpContext.Request.Headers["Origin"].FirstOrDefault()
                            ?? $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";

            await careGiverService.GeneratePasswordResetTokenAsync(request, origin);
            return Ok(new { message = "A reset link has been sent to the registered Email ." });
        }


        // ✅ This is for resetting the password using the token
        [HttpPost("resetPassword")]
        [AllowAnonymous] // Allow unauthenticated access
        public async Task<IActionResult> ResetPassword([FromBody] PasswordResetDto request)
        {
            await careGiverService.ResetPasswordWithJwtAsync(request);
            return Ok(new { message = "Password reset successful." });
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

            var user = await careProDbContext.CareGivers.FirstOrDefaultAsync(x => x.Email == addCaregiverRequest.Email);
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

            if (string.IsNullOrWhiteSpace(addCaregiverRequest.PhoneNo))
            {
                ModelState.AddModelError(nameof(addCaregiverRequest.PhoneNo),
                    $"{nameof(addCaregiverRequest.PhoneNo)} is required.");
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
    }




}

