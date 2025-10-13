using Application.DTOs;
using Application.Interfaces.Content;
using Infrastructure.Content.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;

namespace CarePro_Api.Controllers.Content
{
    [Route("api/[controller]")]
    [ApiController]
    public class AssessmentsController : ControllerBase
    {
        private readonly IAssessmentService assessmentService;
        private readonly ICareGiverService careGiverService;
        private readonly ILogger<AssessmentsController> logger;

        public AssessmentsController(IAssessmentService assessmentService, ICareGiverService careGiverService, ILogger<AssessmentsController> logger)
        {
            this.assessmentService = assessmentService;
            this.careGiverService = careGiverService;
            this.logger = logger;
        }

        [HttpPost]
        // [Authorize(Roles = "Caregiver")]
        public async Task<IActionResult> AddAssessmentAsync([FromBody] AddAssessmentRequest addAssessmentRequest)
        {
            try
            {
                // Submit assessment and calculate score
                var assessmentId = await assessmentService.SubmitAssessmentAsync(addAssessmentRequest);

                // Return the assessment ID
                return Ok(assessmentId);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { Message = ex.Message });
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
                return StatusCode(500, new { ex /*ErrorMessage = "An error occurred on the server."*/ });
            }

        }

        [HttpGet]
        [Route("careGiverId")]
        // [Authorize(Roles = "Caregiver, Client, Admin")]
        public async Task<IActionResult> GetAssessmentAsync(string careGiverId)
        {
            try
            {
                logger.LogInformation($"Retrieving Assessment for caregiver with ID '{careGiverId}'.");

                var assessment = await assessmentService.GetAssesementAsync(careGiverId);

                return Ok(assessment);

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
                return StatusCode(500, new { ex /*ErrorMessage = "An error occurred on the server."*/ });
            }
        }
        
        [HttpGet("questions/{userType}")]
        // [Authorize(Roles = "Caregiver, Cleaner")]
        public async Task<IActionResult> GetQuestionsForAssessmentAsync(string userType)
        {
            try
            {
                // Validate user type
                if (userType != "Cleaner" && userType != "Caregiver")
                {
                    return BadRequest(new { Message = "User type must be either 'Cleaner' or 'Caregiver'" });
                }
                
                var questions = await assessmentService.GetQuestionsForAssessmentAsync(userType);
                return Ok(questions);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting questions for assessment");
                return StatusCode(500, new { ErrorMessage = ex.Message });
            }
        }
        
        [HttpGet("{id}")]
        // [Authorize(Roles = "Caregiver, Cleaner, Admin")]
        public async Task<IActionResult> GetAssessmentByIdAsync(string id)
        {
            try
            {
                var assessment = await assessmentService.GetAssessmentByIdAsync(id);
                return Ok(assessment);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { Message = ex.Message });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting assessment by ID");
                return StatusCode(500, new { ErrorMessage = ex.Message });
            }
        }
        
        [HttpGet("user/{userId}")]
        // [Authorize(Roles = "Caregiver, Cleaner, Admin")]
        public async Task<IActionResult> GetAssessmentsByUserIdAsync(string userId)
        {
            try
            {
                var assessments = await assessmentService.GetAssessmentsByUserIdAsync(userId);
                return Ok(assessments);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting assessments by user ID");
                return StatusCode(500, new { ErrorMessage = ex.Message });
            }
        }
        
        [HttpPost("calculate-score/{assessmentId}")]
        // [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CalculateAssessmentScoreAsync(string assessmentId)
        {
            try
            {
                var assessment = await assessmentService.CalculateAssessmentScoreAsync(assessmentId);
                return Ok(assessment);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { Message = ex.Message });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error calculating assessment score");
                return StatusCode(500, new { ErrorMessage = ex.Message });
            }
        }
    }
}
