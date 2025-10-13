using Application.DTOs;
using Application.Interfaces.Content;
using Infrastructure.Content.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CarePro_Api.Controllers.Content
{
    [Route("api/[controller]")]
    [ApiController]
    public class ReviewsController : ControllerBase
    {
        private readonly IReviewService reviewService;
        private readonly ILogger<ReviewsController> logger;

        public ReviewsController(IReviewService reviewService, ILogger<ReviewsController> logger)
        {
            this.reviewService = reviewService;
            this.logger = logger;
        }

        // POST: api/Reviews/
        [HttpPost]
        // [Authorize(Roles = "Client")]
        public async Task<IActionResult> CreateReview([FromBody] AddReviewRequest  addReviewRequest)
        {
            try
            {
                var review = await reviewService.CreateReviewAsync(addReviewRequest);

                return Ok(new { message = "Review Submitted", review });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error sending review");
                return StatusCode(500, new { message = "Failed to send review", error = ex.Message });
            }
        }


        // GET: api/Reviews
        [HttpGet]
        public async Task<IActionResult> GetAllGigReviewAsync(string gigId)
        {
            try
            {
                var reviews = await reviewService.GetAllGigReviewAsync(gigId);
                return Ok(reviews);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error retrieving reviews");
                return StatusCode(500, new { message = "Failed to retrieve reviews", error = ex.Message });
            }
        }


        // GET: api/Reviews
        [HttpGet]
        [Route("{reviewId}")]
        public async Task<IActionResult> GetGigReviewAsync(string reviewId)
        {
            try
            {
                var review = await reviewService.GetGigReviewAsync(reviewId);
                return Ok(review);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error retrieving reviews");
                return StatusCode(500, new { message = "Failed to retrieve reviews", error = ex.Message });
            }
        }



        // GET: api/Reviews/count
        [HttpGet("count")]
        public async Task<IActionResult> GetReviewCount(string gigId)
        {
            try
            {
                var count = await reviewService.GetReviewCountAsync(gigId);
                return Ok(new { count });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error retrieving unread notification count");
                return StatusCode(500, new { message = "Failed to retrieve unread notification count", error = ex.Message });
            }
        }



    }
}
