using Application.DTOs.Account;
using Application.Interfaces.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CarePro_Api.Controllers.Authentication
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthsController : ControllerBase
    {
        private readonly IAuthResponseService _authResponseService;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public AuthsController(IAuthResponseService authResponseService, IHttpContextAccessor httpContextAccessor)
        {
            _authResponseService = authResponseService;
            _httpContextAccessor = httpContextAccessor;
        }


        # region SetRefreshTokenInCookies

        private void SetRefreshTokenInCookies(string refreshToken, DateTime expires)
        {
            var cookieOptions = new CookieOptions
            {
                HttpOnly = true,
                Expires = expires.ToLocalTime()
            };

            //cookieOptionsExpires = DateTime.UtcNow.AddSeconds(cookieOptions.Timeout);

            Response.Cookies.Append("refreshTokenKey", refreshToken, cookieOptions);
        }

        #endregion

        #region SignUp Endpoint

        [HttpPost("signUp")]
        public async Task<IActionResult> SignUpAsync([FromBody] SignUp model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);
            HttpContext httpContext = _httpContextAccessor.HttpContext;

            // Get the origin (i.e., source URL) of the incoming request
            string origin = httpContext.Request.Headers["Host"];

            var result = await _authResponseService.SignUpAsync(model, origin);

            if (!result.IsAuthenticated)
                return BadRequest(result.Message);

            //store the refresh token in a cookie
            SetRefreshTokenInCookies(result.RefreshToken, result.RefreshTokenExpiration);

            return Ok(result);
        }

        #endregion
        #region get origin
        [HttpGet("GetOrigin")]
        public IActionResult GetOrigin()
        {
            // Retrieve the Origin header from the HTTP request
            HttpContext httpContext = _httpContextAccessor.HttpContext;

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
        #endregion

        #region Login Endpoint

        [HttpPost("login")]
        public async Task<IActionResult> LoginAsync([FromBody] Login model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var result = await _authResponseService.LoginAsync(model);

            if (!result.IsAuthenticated)
                return BadRequest(result.Message);

            //check if the user has a refresh token or not , to store it in a cookie
            if (!string.IsNullOrEmpty(result.RefreshToken))
                SetRefreshTokenInCookies(result.RefreshToken, result.RefreshTokenExpiration);

            return Ok(result);
        }

        #endregion

        #region AssignRole Endpoint

        [HttpPost("AddRole")]
        public async Task<IActionResult> AddRoleAsync(AssignRolesDto model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var result = await _authResponseService.AssignRolesAsync(model);

            if (!string.IsNullOrEmpty(result))
                return BadRequest(result);

            return Ok(model);
        }

        #endregion

        #region RefreshTokenCheck Endpoint

        [HttpGet("refreshToken")]
        public async Task<IActionResult> RefreshTokenCheckAsync()
        {
            var refreshToken = Request.Cookies["refreshTokenKey"];

            var result = await _authResponseService.RefreshTokenCheckAsync(refreshToken);

            if (!result.IsAuthenticated)
                return BadRequest(result);

            return Ok(result);
        }

        #endregion

        #region RevokeTokenAsync

        [HttpPost("revokeToken")]
        public async Task<IActionResult> RevokeTokenAsync(RevokeToken model)
        {
            var refreshToken = model.Token ?? Request.Cookies["refreshTokenKey"];

            //check if there is no token
            if (string.IsNullOrEmpty(refreshToken))
                return BadRequest("Token is required");

            var result = await _authResponseService.RevokeTokenAsync(refreshToken);

            //check if there is a problem with "result"
            //if (!result)
            //    return BadRequest("Token is Invalid");

            return Ok("Done Revoke");
        }

        #endregion

        #region ConfirmEmail
        [HttpGet("confirm-email")]
        public async Task<IActionResult> ConfirmEmailAsync([FromQuery] string userId, [FromQuery] string code)
        {
            var returnUrl = $"{Request.Scheme}://{Request.Host}/login?code={await _authResponseService.ConfirmEmailAsync(userId, code)}";
            ///await _authService.ConfirmEmailAsync(userId, code);
            return Redirect(returnUrl);
        }
        #endregion

        #region Get ApplicantUser

        [HttpGet]
        [Route("ApplicantUsers")]
        //[Authorize(Roles = "Admin,User")]
        public async Task<IActionResult> GetAllUsers([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10)
        {
            var applicantUsers = await _authResponseService.GetAllApplicantUsersAsync(pageNumber, pageSize);
            return Ok(applicantUsers);
        }

        [HttpGet("{applicantUserId}")]
        [Authorize(Roles = "Admin, User")]
        public async Task<IActionResult> GetUserById(string applicantUserId)
        {
            try
            {
                var userDto = await _authResponseService.GetApplicantUserAsync(applicantUserId);
                if (userDto != null)
                    return Ok(userDto);
                else
                    return NotFound($"User with ID {applicantUserId} not found");
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, "Internal server error");
            }
        }


        #endregion


        #region Validation Methods

        private async Task<bool> ValidateSignUpRequestAsync(SignUp applicantUserSignUp)
        {
            if (applicantUserSignUp == null)
            {
                ModelState.AddModelError(nameof(applicantUserSignUp), $" Sign-up Data is required.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(applicantUserSignUp.FirstName))
            {
                ModelState.AddModelError(nameof(applicantUserSignUp.FirstName),
                $"{nameof(applicantUserSignUp.FirstName)} is required.");
            }

            if (string.IsNullOrWhiteSpace(applicantUserSignUp.LastName))
            {
                ModelState.AddModelError(nameof(applicantUserSignUp.LastName),
                $"{nameof(applicantUserSignUp.LastName)} is required.");
            }

            if (string.IsNullOrWhiteSpace(applicantUserSignUp.Email))
            {
                ModelState.AddModelError(nameof(applicantUserSignUp.Email),
                $"{nameof(applicantUserSignUp.Email)} is required.");
            }

            if (string.IsNullOrWhiteSpace(applicantUserSignUp.Password))
            {
                ModelState.AddModelError(nameof(applicantUserSignUp.Password),
                $"{nameof(applicantUserSignUp.Password)} is required.");
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
