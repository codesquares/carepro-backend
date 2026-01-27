using Application.DTOs;
using Application.DTOs.Authentication;
using Application.Interfaces.Authentication;
using Application.Interfaces.Content;
using Infrastructure.Content.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Authentication;

namespace CarePro_Api.Controllers.Content
{
    [Route("api/[controller]")]
    [ApiController]
    public class ClientsController : ControllerBase
    {
        private readonly IClientService clientService;
        private readonly ILogger<ClientsController> logger;
        private readonly IHttpContextAccessor httpContextAccessor;
        private readonly IGoogleAuthService _googleAuthService;

        public ClientsController(
            IClientService clientService, 
            ILogger<ClientsController> logger, 
            IHttpContextAccessor httpContextAccessor,
            IGoogleAuthService googleAuthService)
        {
            this.clientService = clientService;
            this.logger = logger;
            this.httpContextAccessor = httpContextAccessor;
            _googleAuthService = googleAuthService;
        }

        /// <summary>
        /// Sign up as Client using Google account
        /// User has selected "Client" on the role selection screen
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

                var (response, conflict) = await _googleAuthService.GoogleSignUpClientAsync(request, origin);

                if (conflict != null)
                {
                    // Account already exists - prompt to link
                    return Conflict(new { 
                        message = conflict.Message, 
                        requiresLinking = conflict.CanLinkAccounts,
                        conflict 
                    });
                }

                logger.LogInformation("New Client created via Google OAuth: {Email}", response?.Email);
                return Ok(response);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during Google sign up for Client");
                return StatusCode(500, new { ErrorMessage = "An error occurred during Google sign up.", Details = ex.Message });
            }
        }

        /// ENDPOINT TO CREATE  CLIENT USERS TO THE DATABASE
        [HttpPost]
        [Route("AddClientUser")]
        // [Authorize(Roles = "Caregiver")]
        public async Task<IActionResult> AddClientUserAsync([FromBody] AddClientUserRequest addClientUserRequest)
        {
            try
            {
                HttpContext httpContext = httpContextAccessor.HttpContext;

                // Get frontend origin from the request
                string origin = httpContext.Request.Headers["Origin"].FirstOrDefault()
                                ?? $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";


                // Pass Domain Object to Repository, to Persist this
                var clientUser = await clientService.CreateClientUserAsync(addClientUserRequest, origin);


                // Send DTO response back to ClientUser
                return Ok(clientUser);

            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { Message = ex.Message });
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


        //[HttpGet("confirm-email")]
        //public async Task<IActionResult> ConfirmEmail([FromQuery] string token)
        //{
        //    var result = await clientService.ConfirmEmailAsync(token);

        //    if (result.StartsWith("Account confirmed"))
        //        return Ok(result);

        //    return BadRequest(result);
        //}


        [HttpGet("validate-email-token")]
        public async Task<IActionResult> ValidateEmailToken([FromQuery] string token)
        {
            var result = await clientService.ValidateEmailTokenAsync(token);

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
            var message = await clientService.ConfirmEmailFromFrontendAsync(userId);
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



                var result = await clientService.ResendEmailConfirmationAsync(email, origin);
                return Ok(new { message = result });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        #endregion


        [HttpPut]
        [Route("UpdateProfilePicture/{clientId}")]
        //[Authorize(Roles = "Caregiver, Client, Admin")]
        public async Task<IActionResult> UpdateProfilePictureAsync(string clientId, [FromForm] UpdateProfilePictureRequest updateProfilePictureRequest)
        {
            try
            {
                logger.LogInformation($"Client with ID: {clientId} Profile Picture has been updated.");
                var client = await clientService.UpdateProfilePictureAsync(clientId, updateProfilePictureRequest);
                return Ok(client);
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




        [HttpGet]
        [Route("{clientId}")]
        //[Authorize(Roles = "Client,Admin")]
        public async Task<IActionResult> GetClientUserAsync(string clientId)
        {
            try
            {
                logger.LogInformation($"Retrieving Client with ID : {clientId}");
                var client = await clientService.GetClientUserAsync(clientId);
                return Ok(client);
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
        [Route("AllClientUsers")]
        //[Authorize(Roles = "Client,Admin")]
        public async Task<IActionResult> GetAllClientUsersAsync()
        {
            try
            {
                logger.LogInformation("Retrieving all Caregivers");
                var caregivers = await clientService.GetAllClientUserAsync();
                return Ok(caregivers);
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { Message = ex.Message });
            }
            catch (AuthenticationException authEx)
            {
                return Unauthorized(new { StatusCode = 401, ErrorMessage = authEx.Message });
            }
            catch (HttpRequestException httpEx)
            {
                return StatusCode(503, new { StatusCode = 503, ErrorMessage = httpEx.Message });
            }
            catch (DbUpdateException dbEx)
            {
                return StatusCode(500, new { StatusCode = 500, ErrorMessage = dbEx.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { StatusCode = 500, ErrorMessage = ex.Message });
            }

        }


        [HttpPut]
        [Route("UpdateClientUser/{clientId}")]
        //[Authorize(Roles = "Client, Admin")]
        public async Task<IActionResult> UpdateClientUserAsync(string clientId, UpdateClientUserRequest updateClientUserRequest)
        {
            try
            {
                logger.LogInformation($"Client with ID: {clientId} Updated");
                var client = await clientService.UpdateClientUserAsync(clientId, updateClientUserRequest);
                return Ok(client);
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
        [Route("SoftDeleteClient/{clientId}")]
        //[Authorize(Roles = "Client, Admin")]
        public async Task<IActionResult> SoftDeleteClientAsync(string clientId)
        {
            try
            {
                logger.LogInformation($"Client with ID: {clientId} Soft Deleted");
                var client = await clientService.SoftDeleteClientAsync(clientId);
                return Ok(client);
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


        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                await clientService.ChangePasswordAsync(request);
                return Ok(new { message = "Password reset successful." });
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
                HttpContext httpContext = httpContextAccessor.HttpContext;

                // Determine the origin of the request (frontend/backend)
                string origin = httpContext.Request.Headers["Origin"].FirstOrDefault()
                                ?? $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";

                await clientService.GeneratePasswordResetTokenAsync(request, origin);
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
                await clientService.ResetPasswordWithJwtAsync(request);
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


        [HttpPut]
        [Route("UpdateClientLocation/{clientId}")]
        //[Authorize(Roles = "Client, Admin")]
        public async Task<IActionResult> UpdateClientLocationAsync(string clientId, [FromBody] UpdateCaregiverLocationRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                logger.LogInformation($"Updating location for client with ID: {clientId}");
                var result = await clientService.UpdateClientLocationAsync(clientId, request);
                return Ok(new
                {
                    success = true,
                    message = "Client location updated successfully",
                    data = result
                });
            }
            catch (ArgumentException ex)
            {
                logger.LogWarning(ex, $"Invalid request data for updating client {clientId} location");
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                logger.LogWarning(ex, $"Client {clientId} not found");
                return NotFound(new { success = false, message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                logger.LogWarning(ex, $"Invalid operation for updating client {clientId} location");
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error updating location for client {clientId}");
                return StatusCode(500, new { success = false, message = "An error occurred while updating the client location" });
            }
        }


    }
}
