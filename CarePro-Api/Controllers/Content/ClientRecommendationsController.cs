using Application.DTOs;
using Application.Interfaces.Content;
using Infrastructure.Content.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CarePro_Api.Controllers.Content
{
    [Route("api/[controller]")]
    [ApiController]
    public class ClientRecommendationsController : ControllerBase
    {
        private readonly IClientRecommendationService clientRecommendationService;
        private readonly IClientService clientService;
        private readonly ILogger<ClientRecommendationsController> logger;

        public ClientRecommendationsController(IClientRecommendationService clientRecommendationService, IClientService clientService, ILogger<ClientRecommendationsController> logger)
        {
            this.clientRecommendationService = clientRecommendationService;
            this.clientService = clientService;
            this.logger = logger;
        }



        [HttpGet]
        [Route("clientId")]
        // [Authorize(Roles = "Caregiver, Client, Admin")]
        public async Task<IActionResult> GetClientRecommendationAsync(string clientId)
        {

            try
            {
                logger.LogInformation($"Retrieving Recommendation for Client with ID '{clientId}'.");

                var clientRecommendation = await clientRecommendationService.GetClientRecommendationAsync(clientId);

                return Ok(clientRecommendation);

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


    }
}
