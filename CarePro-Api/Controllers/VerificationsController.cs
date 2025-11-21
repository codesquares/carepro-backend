using Application.DTOs;
using Application.Interfaces.Content;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CarePro_Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class VerificationsController : ControllerBase
    {
        private readonly IVerificationService verificationService;
        private readonly ICareGiverService careGiverService;
        private readonly ILogger<VerificationsController> logger;

        public VerificationsController(IVerificationService verificationService, ICareGiverService careGiverService, ILogger<VerificationsController> logger)
        {
            this.verificationService = verificationService;
            this.careGiverService = careGiverService;
            this.logger = logger;
        }

        [HttpPost]
        // [Authorize(Roles = "Caregiver")]
        public async Task<IActionResult> AddVerificationAsync([FromBody] AddVerificationRequest addVerificationRequest)
        {
            try
            {
                // Log the incoming request for debugging
                logger.LogInformation("Starting AddVerificationAsync for UserId: {UserId}, Method: {Method}, Status: {Status}", 
                    addVerificationRequest?.UserId, addVerificationRequest?.VerificationMethod, addVerificationRequest?.VerificationStatus);
                
                // Validate request
                if (addVerificationRequest == null)
                {
                    logger.LogWarning("AddVerificationRequest is null");
                    return BadRequest(new { ErrorMessage = "Request body cannot be null", ErrorCode = "NULL_REQUEST" });
                }
                
                if (string.IsNullOrEmpty(addVerificationRequest.UserId))
                {
                    logger.LogWarning("UserId is null or empty in verification request");
                    return BadRequest(new { ErrorMessage = "UserId is required", ErrorCode = "MISSING_USERID" });
                }

                // Pass Domain Object to Repository, to Persisit this
                var verification = await verificationService.CreateVerificationAsync(addVerificationRequest);

                logger.LogInformation("Successfully created verification with ID: {VerificationId} for UserId: {UserId}", 
                    verification, addVerificationRequest.UserId);

                // Send DTO response back to ClientUser
                return Ok(new { 
                    VerificationId = verification,
                    Message = "Verification created successfully",
                    UserId = addVerificationRequest.UserId,
                    Status = addVerificationRequest.VerificationStatus
                });

            }
            catch (ArgumentException ex)
            {
                logger.LogWarning(ex, "Validation error in AddVerificationAsync for UserId: {UserId}", addVerificationRequest?.UserId);
                return BadRequest(new { 
                    ErrorMessage = ex.Message, 
                    ErrorCode = "VALIDATION_ERROR",
                    UserId = addVerificationRequest?.UserId
                });
            }
            catch (KeyNotFoundException ex)
            {
                logger.LogWarning(ex, "User not found in AddVerificationAsync for UserId: {UserId}", addVerificationRequest?.UserId);
                return NotFound(new { 
                    ErrorMessage = ex.Message, 
                    ErrorCode = "USER_NOT_FOUND",
                    UserId = addVerificationRequest?.UserId
                });
            }
            catch (InvalidOperationException ex)
            {
                logger.LogWarning(ex, "Invalid operation in AddVerificationAsync for UserId: {UserId}", addVerificationRequest?.UserId);
                return BadRequest(new { 
                    ErrorMessage = ex.Message, 
                    ErrorCode = "INVALID_OPERATION",
                    UserId = addVerificationRequest?.UserId
                });
            }
            catch (ApplicationException appEx)
            {
                logger.LogError(appEx, "Application error in AddVerificationAsync for UserId: {UserId}", addVerificationRequest?.UserId);
                return BadRequest(new { 
                    ErrorMessage = appEx.Message, 
                    ErrorCode = "APPLICATION_ERROR",
                    UserId = addVerificationRequest?.UserId
                });
            }
            catch (HttpRequestException httpEx)
            {
                logger.LogError(httpEx, "HTTP request error in AddVerificationAsync for UserId: {UserId}", addVerificationRequest?.UserId);
                return StatusCode(500, new { 
                    ErrorMessage = "External service error: " + httpEx.Message, 
                    ErrorCode = "HTTP_REQUEST_ERROR",
                    UserId = addVerificationRequest?.UserId
                });
            }
            catch (DbUpdateException dbEx)
            {
                logger.LogError(dbEx, "Database error in AddVerificationAsync for UserId: {UserId}. InnerException: {InnerException}", 
                    addVerificationRequest?.UserId, dbEx.InnerException?.Message);
                return StatusCode(500, new { 
                    ErrorMessage = "Database operation failed", 
                    ErrorCode = "DATABASE_ERROR",
                    Details = dbEx.InnerException?.Message,
                    UserId = addVerificationRequest?.UserId
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error in AddVerificationAsync for UserId: {UserId}. Exception Type: {ExceptionType}, Message: {Message}, StackTrace: {StackTrace}", 
                    addVerificationRequest?.UserId, ex.GetType().Name, ex.Message, ex.StackTrace);
                
                return StatusCode(500, new { 
                    ErrorMessage = "An unexpected error occurred on the server", 
                    ErrorCode = "INTERNAL_SERVER_ERROR",
                    ExceptionType = ex.GetType().Name,
                    Details = ex.Message,
                    UserId = addVerificationRequest?.UserId,
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        [HttpGet]
        [Route("userId")]
        // [Authorize(Roles = "Caregiver, Client, Admin")]
        public async Task<IActionResult> GetCaregiverVerificationAsync(string userId)
        {

            try
            {
                logger.LogInformation($"Retrieving Verification for caregiver with ID '{userId}'.");

                var verification = await verificationService.GetVerificationAsync(userId);

                return Ok(verification);

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
                logger.LogError(ex, "An unexpected error occurred"); 
                return StatusCode(500, new { ErrorMessage = "An error occurred on the server." });
            }

        }

        [HttpGet]
        [Route("health")]
        public async Task<IActionResult> HealthCheck()
        {
            try
            {
                logger.LogInformation("Health check endpoint called");
                
                // Test database connectivity
                var testUserId = "test-connection-check";
                logger.LogInformation("Testing database connectivity...");
                
                // This should not throw if database connection is working
                var testQuery = await verificationService.GetUserVerificationStatusAsync(testUserId);
                
                logger.LogInformation("Database connection test completed successfully");
                
                return Ok(new
                {
                    Status = "Healthy",
                    Timestamp = DateTime.UtcNow,
                    DatabaseConnectivity = "OK",
                    Message = "Verification service is running properly"
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Health check failed: {Error}", ex.Message);
                return StatusCode(500, new
                {
                    Status = "Unhealthy",
                    Timestamp = DateTime.UtcNow,
                    Error = ex.Message,
                    ExceptionType = ex.GetType().Name
                });
            }
        }


        [HttpPut]
        [Route("verificationId")]
        // [Authorize(Roles = "Caregiver, Admin")]
        public async Task<ActionResult<string>> UpdateVerificationAsync(string verificationId, UpdateVerificationRequest updateVerificationRequest)
        {
            try
            {
                var result = await verificationService.UpdateVerificationAsync(verificationId, updateVerificationRequest);
                logger.LogInformation($"Verification  with ID: {verificationId} updated.");
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { Message = ex.Message }); // Returns 400 Bad Request
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


    }
}
