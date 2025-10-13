using Application.Interfaces.Content;
using Infrastructure.Content.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CarePro_Api.Controllers.Content
{
    [Route("api/[controller]")]
    [ApiController]
    public class SearchsController : ControllerBase
    {
        private readonly ISearchService searchService;

        public SearchsController(ISearchService searchService)
        {
            this.searchService = searchService;
        }


        [HttpGet("search-caregivers")]
        public async Task<IActionResult> SearchCaregivers([FromQuery] string searchTerm)
        {
            var results = await searchService.SearchCaregiversWithServicesAsync(searchTerm);
            return Ok(results);
        }

    }
}
