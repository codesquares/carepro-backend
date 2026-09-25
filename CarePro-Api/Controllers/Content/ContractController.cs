using Microsoft.AspNetCore.Mvc;
using Application.Interfaces.Content;
using Application.DTOs;
using Microsoft.AspNetCore.Authorization;

namespace CarePro_Api.Controllers.Content
{
    [ApiController]
    [Route("api/contracts")]
    [Authorize]
    public class ContractController : ControllerBase
    {
        private readonly IContractService _contractService;
        private readonly IContractPdfService _pdfService;
        private readonly ILogger<ContractController> _logger;

        public ContractController(IContractService contractService, IContractPdfService pdfService, ILogger<ContractController> logger)
        {
            _contractService = contractService;
            _pdfService = pdfService;
            _logger = logger;
        }

        [HttpGet("{contractId}")]
        public async Task<ActionResult<ContractDTO>> GetContract(string contractId)
        {
            try
            {
                var contract = await _contractService.GetContractByIdAsync(contractId);
                if (contract == null)
                    return NotFound("Contract not found");

                if (!IsPartyToContractOrAdmin(contract.ClientId, contract.CaregiverId))
                    return Forbid();

                return Ok(contract);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting contract {ContractId}", contractId);
                return StatusCode(500, "Failed to get contract");
            }
        }

        // Client Dashboard Endpoints
        [HttpGet("client/{clientId}")]
        public async Task<ActionResult<List<ContractDTO>>> GetClientContracts(string clientId)
        {
            try
            {
                if (!IsSelfOrAdmin(clientId))
                    return Forbid();

                var contracts = await _contractService.GetContractsByClientIdAsync(clientId);
                return Ok(contracts);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting contracts for client {ClientId}", clientId);
                return StatusCode(500, "Failed to get contracts");
            }
        }

        [HttpDelete("{contractId}/rescind")]
        public async Task<ActionResult> RescindContract(string contractId)
        {
            try
            {
                var existing = await _contractService.GetContractByIdAsync(contractId);
                if (existing == null)
                    return NotFound("Contract not found");

                if (!IsPartyToContractOrAdmin(existing.ClientId, existing.CaregiverId))
                    return Forbid();

                var success = await _contractService.ExpireContractAsync(contractId);
                if (success)
                    return Ok(new { message = "Contract rescinded successfully" });

                return BadRequest("Failed to rescind contract");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error rescinding contract {ContractId}", contractId);
                return StatusCode(500, "Failed to rescind contract");
            }
        }

        // Contract History and Analytics
        [HttpGet("history/{userId}")]
        public async Task<ActionResult<ContractHistoryDTO>> GetContractHistory(string userId, [FromQuery] string userType)
        {
            try
            {
                if (!IsSelfOrAdmin(userId))
                    return Forbid();

                var history = await _contractService.GetContractHistoryAsync(userId);
                return Ok(history);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting contract history for user {UserId}", userId);
                return StatusCode(500, "Failed to get contract history");
            }
        }

        [HttpGet("stats/{userId}")]
        public async Task<ActionResult<ContractStatsDTO>> GetContractStats(string userId, [FromQuery] string userType)
        {
            try
            {
                if (!IsSelfOrAdmin(userId))
                    return Forbid();

                var stats = await _contractService.GetContractAnalyticsAsync(userId, userType);
                return Ok(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting contract stats for user {UserId}", userId);
                return StatusCode(500, "Failed to get contract stats");
            }
        }

        // Contract Lifecycle Management
        [HttpPut("{contractId}/complete")]
        public async Task<ActionResult> CompleteContract(string contractId, [FromBody] ContractCompletionDTO completion)
        {
            try
            {
                var existing = await _contractService.GetContractByIdAsync(contractId);
                if (existing == null)
                    return NotFound("Contract not found");

                if (!IsPartyToContractOrAdmin(existing.ClientId, existing.CaregiverId))
                    return Forbid();

                var success = await _contractService.CompleteContractAsync(contractId, completion.Rating);
                if (success)
                    return Ok(new { message = "Contract completed successfully" });

                return BadRequest("Failed to complete contract");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error completing contract {ContractId}", contractId);
                return StatusCode(500, "Failed to complete contract");
            }
        }

        [HttpPut("{contractId}/terminate")]
        public async Task<ActionResult> TerminateContract(string contractId, [FromBody] ContractTerminationDTO termination)
        {
            try
            {
                var existing = await _contractService.GetContractByIdAsync(contractId);
                if (existing == null)
                    return NotFound("Contract not found");

                if (!IsPartyToContractOrAdmin(existing.ClientId, existing.CaregiverId))
                    return Forbid();

                var success = await _contractService.TerminateContractAsync(contractId, termination.Reason);
                if (success)
                    return Ok(new { message = "Contract terminated successfully" });

                return BadRequest("Failed to terminate contract");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error terminating contract {ContractId}", contractId);
                return StatusCode(500, "Failed to terminate contract");
            }
        }

        /// <summary>
        /// Download the contract as a formatted PDF.
        /// Available to the client or caregiver who is party to the contract.
        /// </summary>
        [HttpGet("{contractId}/pdf")]
        public async Task<IActionResult> DownloadContractPdf(string contractId)
        {
            try
            {
                var userId = GetUserIdFromToken();
                if (string.IsNullOrEmpty(userId))
                    return Unauthorized("Authorization required");

                var pdfData = await _contractService.GetContractPdfDataAsync(contractId);
                if (pdfData == null)
                    return NotFound("Contract not found");

                // Ensure requester is a party to this contract, or an admin
                if (pdfData.ClientId != userId && pdfData.CaregiverId != userId && !IsAdmin())
                    return Forbid();

                var pdfBytes = _pdfService.GeneratePdf(pdfData);
                return File(pdfBytes, "application/pdf", $"CarePro-Contract-{contractId}.pdf");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating PDF for contract {ContractId}", contractId);
                return StatusCode(500, "Failed to generate contract PDF");
            }
        }

        /// <summary>
        /// Client stamps their real-time device GPS onto the contract's service location.
        /// Once set, caregiver check-ins use these accurate coordinates for the 1500m proximity check.
        /// </summary>
        [HttpPost("{contractId}/service-location")]
        [Authorize(Roles = "Client")]
        public async Task<ActionResult<SetServiceLocationResponse>> SetServiceLocation(
            string contractId, [FromBody] SetServiceLocationRequest request)
        {
            try
            {
                var clientId = GetUserIdFromToken();
                if (string.IsNullOrEmpty(clientId))
                    return Unauthorized("Client authorization required.");

                var result = await _contractService.SetServiceLocationAsync(contractId, clientId, request);
                return Ok(result);
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { error = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting service location for contract {ContractId}", contractId);
                return StatusCode(500, new { error = "Failed to set service location." });
            }
        }

        // Helper method to extract user ID from JWT token
        private string? GetUserIdFromToken()
        {
            var userIdClaim = User.FindFirst("userId") ?? User.FindFirst("sub") ?? User.FindFirst("id");
            return userIdClaim?.Value;
        }

        private bool IsAdmin() => User.IsInRole("Admin") || User.IsInRole("SuperAdmin");

        /// <summary>
        /// True if the caller is the given client, the given caregiver, or an admin.
        /// Used to gate every route in this controller keyed by a bare contract ID,
        /// none of which had an ownership check before this fix.
        /// </summary>
        private bool IsPartyToContractOrAdmin(string? clientId, string? caregiverId)
        {
            var userId = GetUserIdFromToken();
            return (userId != null && (userId == clientId || userId == caregiverId)) || IsAdmin();
        }

        /// <summary>
        /// True if the caller's own ID matches the target user ID, or the caller is an admin.
        /// Used for routes keyed by a bare client/user ID (client/{clientId}, history/{userId}, stats/{userId}).
        /// </summary>
        private bool IsSelfOrAdmin(string targetUserId)
        {
            var userId = GetUserIdFromToken();
            return userId == targetUserId || IsAdmin();
        }
    }

    // Supporting DTOs
    public class ContractCompletionDTO
    {
        public decimal? Rating { get; set; }
    }

    public class ContractTerminationDTO
    {
        public string? Reason { get; set; }
    }
}
