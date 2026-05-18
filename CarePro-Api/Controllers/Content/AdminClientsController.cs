using Application.DTOs;
using Application.Interfaces.Content;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CarePro_Api.Controllers.Content
{
    [Route("api/Admin/Clients")]
    [ApiController]
    [Authorize(Policy = "OperationsPolicy")]
    public class AdminClientsController : ControllerBase
    {
        private readonly IClientService _clientService;
        private readonly ILogger<AdminClientsController> _logger;

        public AdminClientsController(
            IClientService clientService,
            ILogger<AdminClientsController> logger)
        {
            _clientService = clientService;
            _logger = logger;
        }

        [HttpPut("BulkClearMiddleName")]
        public async Task<IActionResult> BulkClearClientMiddleName(
            [FromBody] AdminBulkClearMiddleNameRequest request)
        {
            try
            {
                if (request == null)
                    return BadRequest(new { error = "Request body is required" });

                var result = await _clientService.BulkClearClientMiddleNameAsync(request);
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Validation error on client bulk clear middle name");
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during client bulk clear middle name");
                return StatusCode(500, new { error = "Failed to bulk clear client middle names" });
            }
        }
    }
}
