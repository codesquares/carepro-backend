using Microsoft.AspNetCore.Mvc;
using Application.Interfaces.Content;
using Application.DTOs;

namespace CarePro_Api.Controllers.Content
{
    [ApiController]
    [Route("api/contracts")]
    public class ContractController : ControllerBase
    {
        private readonly IContractService _contractService;
        private readonly ILogger<ContractController> _logger;

        public ContractController(IContractService contractService, ILogger<ContractController> logger)
        {
            _contractService = contractService;
            _logger = logger;
        }

        // Caregiver Dashboard Endpoints
        [HttpGet("caregiver/{caregiverId}/pending")]
        public async Task<ActionResult<List<ContractDTO>>> GetPendingContracts(string caregiverId)
        {
            try
            {
                var contracts = await _contractService.GetPendingContractsForCaregiverAsync(caregiverId);
                return Ok(contracts);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting pending contracts for caregiver {CaregiverId}", caregiverId);
                return StatusCode(500, "Failed to get pending contracts");
            }
        }

        [HttpPost("caregiver/respond")]
        public async Task<ActionResult<ContractDTO>> RespondToContract([FromBody] CaregiverContractResponseDTO response)
        {
            try
            {
                var contract = await _contractService.ProcessCaregiverResponseAsync(response);
                return Ok(contract);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing caregiver response for contract {ContractId}", response.ContractId);
                return StatusCode(500, "Failed to process response");
            }
        }

        [HttpGet("{contractId}")]
        public async Task<ActionResult<ContractDTO>> GetContract(string contractId)
        {
            try
            {
                var contract = await _contractService.GetContractByIdAsync(contractId);
                if (contract == null)
                    return NotFound("Contract not found");

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
                var contracts = await _contractService.GetContractsByClientIdAsync(clientId);
                return Ok(contracts);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting contracts for client {ClientId}", clientId);
                return StatusCode(500, "Failed to get contracts");
            }
        }

        [HttpGet("{contractId}/alternatives")]
        public async Task<ActionResult<List<AlternativeCaregiverDTO>>> GetAlternativesCaregivers(string contractId)
        {
            try
            {
                var alternatives = await _contractService.GetAlternativeCaregiversAsync(contractId);
                return Ok(alternatives);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting alternative caregivers for contract {ContractId}", contractId);
                return StatusCode(500, "Failed to get alternatives");
            }
        }

        [HttpPost("{contractId}/send-to-alternative")]
        public async Task<ActionResult<ContractDTO>> SendToAlternativeCaregiver(string contractId, [FromBody] SendToAlternativeDTO request)
        {
            try
            {
                var newContract = await _contractService.SendContractToAlternativeCaregiverAsync(contractId, request.CaregiverId);
                return Ok(newContract);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending contract {ContractId} to alternative caregiver {CaregiverId}", contractId, request.CaregiverId);
                return StatusCode(500, "Failed to send to alternative");
            }
        }

        [HttpPut("{contractId}/revise")]
        public async Task<ActionResult<ContractDTO>> ReviseContract(string contractId, [FromBody] ContractRevisionRequestDTO revisionRequest)
        {
            try
            {
                var contract = await _contractService.ReviseContractAsync(contractId, revisionRequest);
                return Ok(contract);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error revising contract {ContractId}", contractId);
                return StatusCode(500, "Failed to revise contract");
            }
        }

        [HttpDelete("{contractId}/rescind")]
        public async Task<ActionResult> RescindContract(string contractId)
        {
            try
            {
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
    }

    // Supporting DTOs
    public class SendToAlternativeDTO
    {
        public string CaregiverId { get; set; }
    }

    public class ContractCompletionDTO
    {
        public decimal? Rating { get; set; }
    }

    public class ContractTerminationDTO
    {
        public string Reason { get; set; }
    }
}