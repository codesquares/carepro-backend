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

        // ========================================
        // NEW FLOW: Caregiver-Initiated Contract Endpoints
        // ========================================

        /// <summary>
        /// Caregiver generates contract after agreeing on schedule with client.
        /// Called when caregiver clicks "Generate Contract" button on an order.
        /// </summary>
        [HttpPost("caregiver/generate")]
        public async Task<ActionResult<ContractDTO>> CaregiverGenerateContract([FromBody] CaregiverContractGenerationDTO request)
        {
            return BadRequest("This endpoint is deprecated. Please use the negotiation flow at POST /api/negotiations to create a negotiation, then convert to contract when both parties agree.");
        }

        /// <summary>
        /// Client approves the contract sent by caregiver.
        /// </summary>
        [HttpPut("{contractId}/client-approve")]
        public async Task<ActionResult<ContractDTO>> ClientApproveContract(string contractId, [FromBody] ClientContractApprovalRequest? request = null)
        {
            try
            {
                var clientId = GetUserIdFromToken();
                if (string.IsNullOrEmpty(clientId))
                    return Unauthorized("Client authorization required");

                var contract = await _contractService.ClientApproveContractAsync(contractId, clientId, request);
                
                _logger.LogInformation("Contract {ContractId} approved by client {ClientId}",
                    contractId, clientId);
                
                return Ok(contract);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning("Invalid contract approval request for {ContractId}: {Message}", 
                    contractId, ex.Message);
                return BadRequest(ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning("Unauthorized contract approval attempt for {ContractId}: {Message}", 
                    contractId, ex.Message);
                return Forbid(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error approving contract {ContractId}", contractId);
                return StatusCode(500, "Failed to approve contract");
            }
        }

        /// <summary>
        /// Client requests review/changes (only allowed in Round 1).
        /// </summary>
        [HttpPut("{contractId}/client-request-review")]
        public async Task<ActionResult<ContractDTO>> ClientRequestReview(string contractId, [FromBody] ClientContractReviewRequestDTO request)
        {
            try
            {
                var clientId = GetUserIdFromToken();
                if (string.IsNullOrEmpty(clientId))
                    return Unauthorized("Client authorization required");

                var contract = await _contractService.ClientRequestReviewAsync(contractId, clientId, request);
                
                _logger.LogInformation("Contract {ContractId} review requested by client {ClientId}",
                    contractId, clientId);
                
                return Ok(contract);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning("Invalid review request for {ContractId}: {Message}", 
                    contractId, ex.Message);
                return BadRequest(ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning("Unauthorized review request attempt for {ContractId}: {Message}", 
                    contractId, ex.Message);
                return Forbid(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error requesting review for contract {ContractId}", contractId);
                return StatusCode(500, "Failed to request contract review");
            }
        }

        /// <summary>
        /// Caregiver revises contract after client review request (Round 2).
        /// </summary>
        [HttpPut("caregiver/revise")]
        public async Task<ActionResult<ContractDTO>> CaregiverReviseContract([FromBody] CaregiverContractRevisionDTO revision)
        {
            try
            {
                var caregiverId = GetUserIdFromToken();
                if (string.IsNullOrEmpty(caregiverId))
                    return Unauthorized("Caregiver authorization required");

                var contract = await _contractService.CaregiverReviseContractAsync(caregiverId, revision);
                
                _logger.LogInformation("Contract {ContractId} revised by caregiver {CaregiverId}",
                    revision.ContractId, caregiverId);
                
                return Ok(contract);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning("Invalid contract revision for {ContractId}: {Message}", 
                    revision.ContractId, ex.Message);
                return BadRequest(ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning("Unauthorized contract revision attempt for {ContractId}: {Message}", 
                    revision.ContractId, ex.Message);
                return Forbid(ex.Message);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning("Invalid schedule in contract revision: {Message}", ex.Message);
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error revising contract {ContractId}", revision.ContractId);
                return StatusCode(500, "Failed to revise contract");
            }
        }

        /// <summary>
        /// Client rejects contract (only allowed in Round 2, after revision).
        /// </summary>
        [HttpPut("{contractId}/client-reject")]
        public async Task<ActionResult<ContractDTO>> ClientRejectContract(string contractId, [FromBody] ClientContractRejectionDTO? request)
        {
            try
            {
                var clientId = GetUserIdFromToken();
                if (string.IsNullOrEmpty(clientId))
                    return Unauthorized("Client authorization required");

                var contract = await _contractService.ClientRejectContractAsync(contractId, clientId, request);
                
                _logger.LogInformation("Contract {ContractId} rejected by client {ClientId}. Reason: {Reason}",
                    contractId, clientId, request?.Reason ?? "No reason provided");
                
                return Ok(contract);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning("Invalid contract rejection for {ContractId}: {Message}", 
                    contractId, ex.Message);
                return BadRequest(ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning("Unauthorized contract rejection attempt for {ContractId}: {Message}", 
                    contractId, ex.Message);
                return Forbid(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error rejecting contract {ContractId}", contractId);
                return StatusCode(500, "Failed to reject contract");
            }
        }

        /// <summary>
        /// Get negotiation history for a contract (for safety/audit purposes).
        /// </summary>
        [HttpGet("{contractId}/negotiation-history")]
        public async Task<ActionResult<List<ContractNegotiationHistoryDTO>>> GetNegotiationHistory(string contractId)
        {
            try
            {
                var history = await _contractService.GetNegotiationHistoryAsync(contractId);
                return Ok(history);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting negotiation history for contract {ContractId}", contractId);
                return StatusCode(500, "Failed to get negotiation history");
            }
        }

        /// <summary>
        /// Get contracts pending client approval.
        /// </summary>
        [HttpGet("client/{clientId}/pending-approval")]
        public async Task<ActionResult<List<ContractDTO>>> GetPendingContractsForClient(string clientId)
        {
            try
            {
                var contracts = await _contractService.GetPendingContractsForClientAsync(clientId);
                return Ok(contracts);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting pending contracts for client {ClientId}", clientId);
                return StatusCode(500, "Failed to get pending contracts");
            }
        }

        // ========================================
        // CLIENT-INITIATED CONTRACT FLOW
        // ========================================

        /// <summary>
        /// Client generates contract with service address, access instructions, optional schedule and tasks.
        /// Caregiver must approve and confirm/set the schedule.
        /// </summary>
        [HttpPost("client/generate")]
        public async Task<ActionResult<ContractDTO>> ClientGenerateContract([FromBody] ClientContractGenerationDTO request)
        {
            return BadRequest("This endpoint is deprecated. Please use the negotiation flow at POST /api/negotiations to create a negotiation, then convert to contract when both parties agree.");
        }

        /// <summary>
        /// Caregiver approves a client-initiated contract. Must provide/confirm the schedule.
        /// </summary>
        [HttpPut("{contractId}/caregiver-approve")]
        public async Task<ActionResult<ContractDTO>> CaregiverApproveContract(string contractId, [FromBody] CaregiverContractApprovalRequest request)
        {
            try
            {
                var caregiverId = GetUserIdFromToken();
                if (string.IsNullOrEmpty(caregiverId))
                    return Unauthorized("Caregiver authorization required");

                var contract = await _contractService.CaregiverApproveContractAsync(contractId, caregiverId, request);

                _logger.LogInformation("Contract {ContractId} approved by caregiver {CaregiverId}", contractId, caregiverId);

                return Ok(contract);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning("Invalid caregiver approval for {ContractId}: {Message}", contractId, ex.Message);
                return BadRequest(ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning("Unauthorized caregiver approval attempt for {ContractId}: {Message}", contractId, ex.Message);
                return Forbid(ex.Message);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning("Invalid input in caregiver approval for {ContractId}: {Message}", contractId, ex.Message);
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error approving contract {ContractId} by caregiver", contractId);
                return StatusCode(500, "Failed to approve contract");
            }
        }

        /// <summary>
        /// Caregiver requests changes on a client-initiated contract (Round 1 only).
        /// </summary>
        [HttpPut("{contractId}/caregiver-request-review")]
        public async Task<ActionResult<ContractDTO>> CaregiverRequestReview(string contractId, [FromBody] CaregiverContractReviewRequestDTO request)
        {
            try
            {
                var caregiverId = GetUserIdFromToken();
                if (string.IsNullOrEmpty(caregiverId))
                    return Unauthorized("Caregiver authorization required");

                var contract = await _contractService.CaregiverRequestReviewAsync(contractId, caregiverId, request);

                _logger.LogInformation("Contract {ContractId} review requested by caregiver {CaregiverId}", contractId, caregiverId);

                return Ok(contract);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning("Invalid caregiver review request for {ContractId}: {Message}", contractId, ex.Message);
                return BadRequest(ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning("Unauthorized caregiver review request for {ContractId}: {Message}", contractId, ex.Message);
                return Forbid(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error requesting review for contract {ContractId} by caregiver", contractId);
                return StatusCode(500, "Failed to request contract review");
            }
        }

        /// <summary>
        /// Client revises contract after caregiver review request (Round 2, client-initiated flow).
        /// </summary>
        [HttpPut("client/revise")]
        public async Task<ActionResult<ContractDTO>> ClientReviseContract([FromBody] ClientContractRevisionDTO revision)
        {
            try
            {
                var clientId = GetUserIdFromToken();
                if (string.IsNullOrEmpty(clientId))
                    return Unauthorized("Client authorization required");

                var contract = await _contractService.ClientReviseContractAsync(clientId, revision);

                _logger.LogInformation("Contract {ContractId} revised by client {ClientId}", revision.ContractId, clientId);

                return Ok(contract);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning("Invalid client revision for {ContractId}: {Message}", revision.ContractId, ex.Message);
                return BadRequest(ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning("Unauthorized client revision attempt for {ContractId}: {Message}", revision.ContractId, ex.Message);
                return Forbid(ex.Message);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning("Invalid input in client revision: {Message}", ex.Message);
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error revising contract {ContractId} by client", revision.ContractId);
                return StatusCode(500, "Failed to revise contract");
            }
        }

        /// <summary>
        /// Caregiver rejects a client-initiated contract (Round 2 only).
        /// </summary>
        [HttpPut("{contractId}/caregiver-reject")]
        public async Task<ActionResult<ContractDTO>> CaregiverRejectContract(string contractId, [FromBody] CaregiverContractRejectionDTO? request)
        {
            try
            {
                var caregiverId = GetUserIdFromToken();
                if (string.IsNullOrEmpty(caregiverId))
                    return Unauthorized("Caregiver authorization required");

                var contract = await _contractService.CaregiverRejectContractAsync(contractId, caregiverId, request);

                _logger.LogInformation("Contract {ContractId} rejected by caregiver {CaregiverId}", contractId, caregiverId);

                return Ok(contract);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning("Invalid caregiver rejection for {ContractId}: {Message}", contractId, ex.Message);
                return BadRequest(ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning("Unauthorized caregiver rejection attempt for {ContractId}: {Message}", contractId, ex.Message);
                return Forbid(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error rejecting contract {ContractId} by caregiver", contractId);
                return StatusCode(500, "Failed to reject contract");
            }
        }

        /// <summary>
        /// Get contracts pending caregiver approval (client-initiated flow).
        /// </summary>
        [HttpGet("caregiver/{caregiverId}/pending-approval")]
        public async Task<ActionResult<List<ContractDTO>>> GetPendingContractsForCaregiverApproval(string caregiverId)
        {
            try
            {
                var contracts = await _contractService.GetPendingContractsForCaregiverApprovalAsync(caregiverId);
                return Ok(contracts);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting pending contracts for caregiver approval {CaregiverId}", caregiverId);
                return StatusCode(500, "Failed to get pending contracts");
            }
        }

        // ========================================
        // LEGACY: Original Contract Endpoints (kept for backward compatibility)
        // ========================================

        // Manual Contract Generation from Order - DEPRECATED for new flow
        [HttpPost("generate-from-order/{orderId}")]
        public async Task<ActionResult<ContractDTO>> GenerateContractFromOrder(string orderId)
        {
            try
            {
                var contract = await _contractService.GenerateContractFromOrderAsync(orderId);
                
                _logger.LogInformation("Contract {ContractId} generated from order {OrderId}",
                    contract.Id, orderId);
                
                return Ok(contract);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning("Invalid request to generate contract for order {OrderId}: {Message}", 
                    orderId, ex.Message);
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating contract from order {OrderId}", orderId);
                return StatusCode(500, "Failed to generate contract from order");
            }
        }

        // Helper method to extract user ID from JWT token
        private string? GetUserIdFromToken()
        {
            var userIdClaim = User.FindFirst("userId") ?? User.FindFirst("sub") ?? User.FindFirst("id");
            return userIdClaim?.Value;
        }

        /// <summary>
        /// Admin one-time action: cancel all active (non-terminal) contracts.
        /// Used during migration to the new negotiation flow.
        /// </summary>
        [HttpPost("admin/cancel-all-active")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult> CancelAllActiveContracts()
        {
            try
            {
                var count = await _contractService.CancelAllActiveContractsAsync();
                _logger.LogInformation("Admin cancelled {Count} active contracts for negotiation flow migration", count);
                return Ok(new { message = $"Successfully cancelled {count} active contracts.", count });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cancelling all active contracts");
                return StatusCode(500, "Failed to cancel contracts.");
            }
        }

        // LEGACY: Caregiver Contract Action Endpoints (for old flow)
        [HttpPut("{contractId}/accept")]
        public async Task<ActionResult<ContractDTO>> AcceptContract(string contractId)
        {
            try
            {
                // Get caregiver ID from JWT token - you'll need to implement this based on your auth system
                var caregiverId = GetCaregiverIdFromToken();
                if (string.IsNullOrEmpty(caregiverId))
                    return Unauthorized("Caregiver authorization required");

                var contract = await _contractService.AcceptContractAsync(contractId, caregiverId);
                
                _logger.LogInformation("Contract {ContractId} accepted by caregiver {CaregiverId}",
                    contractId, caregiverId);
                
                return Ok(contract);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning("Invalid contract accept request for {ContractId}: {Message}", 
                    contractId, ex.Message);
                return BadRequest(ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning("Unauthorized contract accept attempt for {ContractId}: {Message}", 
                    contractId, ex.Message);
                return Forbid(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error accepting contract {ContractId}", contractId);
                return StatusCode(500, "Failed to accept contract");
            }
        }

        [HttpPut("{contractId}/reject")]
        public async Task<ActionResult<ContractDTO>> RejectContract(string contractId, [FromBody] ContractRejectRequestDTO request)
        {
            try
            {
                var caregiverId = GetCaregiverIdFromToken();
                if (string.IsNullOrEmpty(caregiverId))
                    return Unauthorized("Caregiver authorization required");

                var contract = await _contractService.RejectContractAsync(contractId, caregiverId, request?.Reason);
                
                _logger.LogInformation("Contract {ContractId} rejected by caregiver {CaregiverId} with reason: {Reason}",
                    contractId, caregiverId, request?.Reason ?? "No reason provided");
                
                return Ok(contract);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning("Invalid contract reject request for {ContractId}: {Message}", 
                    contractId, ex.Message);
                return BadRequest(ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning("Unauthorized contract reject attempt for {ContractId}: {Message}", 
                    contractId, ex.Message);
                return Forbid(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error rejecting contract {ContractId}", contractId);
                return StatusCode(500, "Failed to reject contract");
            }
        }

        [HttpPut("{contractId}/request-review")]
        public async Task<ActionResult<ContractDTO>> RequestContractReview(string contractId, [FromBody] ContractReviewRequestDTO request)
        {
            try
            {
                var caregiverId = GetCaregiverIdFromToken();
                if (string.IsNullOrEmpty(caregiverId))
                    return Unauthorized("Caregiver authorization required");

                var contract = await _contractService.RequestContractReviewAsync(contractId, caregiverId, request?.Comments);
                
                _logger.LogInformation("Contract review requested for {ContractId} by caregiver {CaregiverId}",
                    contractId, caregiverId);
                
                return Ok(contract);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning("Invalid contract review request for {ContractId}: {Message}", 
                    contractId, ex.Message);
                return BadRequest(ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning("Unauthorized contract review request attempt for {ContractId}: {Message}", 
                    contractId, ex.Message);
                return Forbid(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error requesting contract review for {ContractId}", contractId);
                return StatusCode(500, "Failed to request contract review");
            }
        }

        // Helper method to extract caregiver ID from JWT token
        private string? GetCaregiverIdFromToken()
        {
            // Extract caregiver ID from JWT claims (userId, sub, or id)
            var userIdClaim = User.FindFirst("userId") ?? User.FindFirst("sub") ?? User.FindFirst("id");
            return userIdClaim?.Value;
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

        [HttpGet("by-order/{orderId}")]
        public async Task<ActionResult<ContractDTO>> GetContractByOrder(string orderId)
        {
            try
            {
                var contract = await _contractService.GetContractByOrderIdAsync(orderId);
                if (contract == null)
                    return NotFound("Contract not found for this order");

                return Ok(contract);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting contract for order {OrderId}", orderId);
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

                // Ensure requester is a party to this contract
                if (pdfData.ClientId != userId && pdfData.CaregiverId != userId)
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
    }

    // Supporting DTOs
    public class SendToAlternativeDTO
    {
        public string? CaregiverId { get; set; }
    }

    public class ContractCompletionDTO
    {
        public decimal? Rating { get; set; }
    }

    public class ContractTerminationDTO
    {
        public string? Reason { get; set; }
    }
}