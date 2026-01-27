using Application.DTOs;
using Application.DTOs.Authentication;
using Application.Interfaces.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NuGet.Protocol.Plugins;

namespace CarePro_Api.Controllers.Authentication
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthenticationsController : ControllerBase
    {
        private readonly IAuthService authService;
        private readonly ITokenHandler tokenHandler;
        private readonly IGoogleAuthService _googleAuthService;

        public AuthenticationsController(
            IAuthService authService, 
            ITokenHandler tokenHandler,
            IGoogleAuthService googleAuthService)
        {
            this.authService = authService;
            this.tokenHandler = tokenHandler;
            _googleAuthService = googleAuthService;
        }


        //[HttpPost]
        //[Route("UserLogin")]
        //[AllowAnonymous]
        //public async Task<IActionResult> UserLogin([FromBody] LoginRequest loginRequest)
        //{
        //    try
        //    {
        //        // Call repository to login client user
        //        var careProUserDomain = await authService.AuthenticateUserAsync(loginRequest);
        //        var auth = new LoginResponse();

        //        if (careProUserDomain == null)
        //        {
        //            return Unauthorized(new { message = "Email Entered does not exist, Please Check!" });
        //        }

        //        bool isValidPassword = BCrypt.Net.BCrypt.Verify(loginRequest.Password, careProUserDomain.Password);

        //        if (careProUserDomain != null && isValidPassword)
        //        {

        //            // Generate a JWT Token
        //            var token = await tokenHandler.CreateTokenAsync(careProUserDomain);

        //            // Return both token and clientUserDomain
        //            var response = new LoginResponse
        //            {
        //                Id = careProUserDomain.AppUserId.ToString(),
        //                FirstName = careProUserDomain.FirstName,
        //                MiddleName = careProUserDomain.LastName,
        //                LastName = careProUserDomain.LastName,
        //                Email = loginRequest.Email,
        //                Role = careProUserDomain.Role,
        //                Token = token,
        //            };

        //            return Ok(response);

        //        }
        //        else if (careProUserDomain != null && !isValidPassword)
        //        {
        //            return Unauthorized(new { message = "Incorrect Password." });
        //        }
        //        else
        //        {
        //            return BadRequest("Bad Request (Email or Password incorrect)");
        //        }

        //    }
        //    catch (UnauthorizedAccessException ex)
        //    {
        //        return Unauthorized(new { message = ex.Message });
        //    }
        //    catch (ApplicationException appEx)
        //    {
        //        // Handle application-specific exceptions
        //        return BadRequest(new { ErrorMessage = appEx.Message });
        //    }
        //    catch (HttpRequestException httpEx)
        //    {
        //        // Handle HTTP request-related exceptions
        //        return StatusCode(500, new { ErrorMessage = "An error occurred on the server. \n ", httpEx.Message });
        //    }
        //    catch (Exception Ex)
        //    {
        //        // Handle application-specific exceptions
        //        return BadRequest(new { ErrorMessage = Ex.Message });
        //    }

        //}

        [HttpPost("UserLogin")]
        [AllowAnonymous]
        public async Task<IActionResult> UserLogin([FromBody] LoginRequest loginRequest)
        {
            try
            {
                var response = await authService.AuthenticateUserLoginAsync(loginRequest);
                return Ok(response);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
            catch (ApplicationException appEx)
            {
                return BadRequest(new { ErrorMessage = appEx.Message });
            }
            catch (HttpRequestException httpEx)
            {
                return StatusCode(500, new { ErrorMessage = "An error occurred on the server.", httpEx.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { ErrorMessage = ex.Message });
            }
        }

        [HttpPost("RefreshToken")]
        [AllowAnonymous]
        public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request)
        {
            try
            {
                var response = await authService.RefreshTokenAsync(request);
                return Ok(response);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
            catch (ApplicationException appEx)
            {
                return BadRequest(new { ErrorMessage = appEx.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { ErrorMessage = "An error occurred on the server.", Details = ex.Message });
            }
        }

        [HttpPost("RevokeToken")]
        [AllowAnonymous]
        public async Task<IActionResult> RevokeToken([FromBody] RefreshTokenRequest request)
        {
            try
            {
                var success = await authService.RevokeRefreshTokenAsync(request.RefreshToken);
                if (success)
                {
                    return Ok(new { message = "Token revoked successfully" });
                }
                return BadRequest(new { message = "Failed to revoke token" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { ErrorMessage = "An error occurred on the server.", Details = ex.Message });
            }
        }

        #region Google OAuth Endpoints

        /// <summary>
        /// Sign in with Google account (for existing users)
        /// </summary>
        [HttpPost("GoogleSignIn")]
        [AllowAnonymous]
        public async Task<IActionResult> GoogleSignIn([FromBody] GoogleSignInRequest request)
        {
            try
            {
                var (response, conflict) = await _googleAuthService.GoogleSignInAsync(request);

                if (conflict != null)
                {
                    // Account needs linking or doesn't exist
                    return conflict.CanLinkAccounts 
                        ? Ok(new { requiresLinking = true, conflict })
                        : NotFound(new { message = conflict.Message, conflict });
                }

                return Ok(response);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { ErrorMessage = "An error occurred during Google sign in.", Details = ex.Message });
            }
        }

        /// <summary>
        /// Link Google account to existing local account
        /// User must be logged in (have valid session)
        /// </summary>
        [HttpPost("LinkGoogleAccount")]
        [Authorize]
        public async Task<IActionResult> LinkGoogleAccount([FromBody] LinkGoogleAccountRequest request)
        {
            try
            {
                var response = await _googleAuthService.LinkGoogleAccountAsync(request);
                return Ok(new { message = "Google account linked successfully", response });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { ErrorMessage = "An error occurred while linking Google account.", Details = ex.Message });
            }
        }

        /// <summary>
        /// Check if email exists before Google sign up
        /// </summary>
        [HttpGet("CheckEmailExists")]
        [AllowAnonymous]
        public async Task<IActionResult> CheckEmailExists([FromQuery] string email)
        {
            try
            {
                var (exists, role, authProvider) = await _googleAuthService.CheckEmailExistsAsync(email);
                return Ok(new { exists, role, authProvider });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { ErrorMessage = "An error occurred.", Details = ex.Message });
            }
        }

        #endregion Google OAuth Endpoints

    }
}
