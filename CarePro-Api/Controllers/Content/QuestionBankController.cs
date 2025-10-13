using Application.DTOs;
using Application.Interfaces.Content;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace CarePro_Api.Controllers.Content
{
    [Route("api/[controller]")]
    [ApiController]
    public class QuestionBankController : ControllerBase
    {
        private readonly IQuestionBankService questionBankService;
        private readonly ILogger<QuestionBankController> logger;

        public QuestionBankController(
            IQuestionBankService questionBankService,
            ILogger<QuestionBankController> logger)
        {
            this.questionBankService = questionBankService;
            this.logger = logger;
        }

        [HttpPost]
        // [Authorize(Roles = "Admin")]
        public async Task<IActionResult> AddQuestionAsync([FromBody] AddQuestionBankRequest addQuestionRequest)
        {
            try
            {
                var questionId = await questionBankService.AddQuestionAsync(addQuestionRequest);
                return Ok(questionId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error adding question to question bank");
                return BadRequest(new { ErrorMessage = ex.Message });
            }
        }

        [HttpPost("batch")]
        // [Authorize(Roles = "Admin")]
        public async Task<IActionResult> BatchAddQuestionsAsync([FromBody] BatchAddQuestionBankRequest batchAddRequest)
        {
            try
            {
                var result = await questionBankService.BatchAddQuestionsAsync(batchAddRequest);
                return Ok(new { Success = result });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error batch adding questions to question bank");
                return BadRequest(new { ErrorMessage = ex.Message });
            }
        }

        [HttpGet("{id}")]
        // [Authorize(Roles = "Admin,Caregiver,Cleaner")]
        public async Task<IActionResult> GetQuestionByIdAsync(string id)
        {
            try
            {
                var question = await questionBankService.GetQuestionByIdAsync(id);
                return Ok(question);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { Message = ex.Message });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting question by ID");
                return BadRequest(new { ErrorMessage = ex.Message });
            }
        }

        [HttpGet("userType/{userType}")]
        // [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetQuestionsByUserTypeAsync(string userType)
        {
            try
            {
                var questions = await questionBankService.GetQuestionsByUserTypeAsync(userType);
                return Ok(questions);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting questions by user type");
                return BadRequest(new { ErrorMessage = ex.Message });
            }
        }

        [HttpGet("category/{category}")]
        // [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetQuestionsByCategoryAsync(string category)
        {
            try
            {
                var questions = await questionBankService.GetQuestionsByCategoryAsync(category);
                return Ok(questions);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting questions by category");
                return BadRequest(new { ErrorMessage = ex.Message });
            }
        }

        [HttpGet]
        // [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetAllQuestionsAsync()
        {
            try
            {
                var questions = await questionBankService.GetAllQuestionsAsync();
                return Ok(questions);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting all questions");
                return BadRequest(new { ErrorMessage = ex.Message });
            }
        }

        [HttpPut]
        // [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateQuestionAsync([FromBody] UpdateQuestionBankRequest updateQuestionRequest)
        {
            try
            {
                var result = await questionBankService.UpdateQuestionAsync(updateQuestionRequest);
                return Ok(new { Success = result });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { Message = ex.Message });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error updating question");
                return BadRequest(new { ErrorMessage = ex.Message });
            }
        }

        [HttpDelete("{id}")]
        // [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteQuestionAsync(string id)
        {
            try
            {
                var result = await questionBankService.DeleteQuestionAsync(id);
                return Ok(new { Success = result });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { Message = ex.Message });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error deleting question");
                return BadRequest(new { ErrorMessage = ex.Message });
            }
        }
    }
}
