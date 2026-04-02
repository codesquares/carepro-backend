using Application.DTOs;
using Domain.Entities;

namespace Application.Interfaces.Content
{
    public interface IContractService
    {
        // ========================================
        // CAREGIVER-INITIATED CONTRACT FLOW
        // ========================================
        
        /// <summary>
        /// Caregiver generates contract after agreeing on schedule with client.
        /// NOTE: Caregiver cannot set service address or access instructions — only the client can.
        /// </summary>
        Task<ContractDTO> CaregiverGenerateContractAsync(string caregiverId, CaregiverContractGenerationDTO request);
        
        /// <summary>
        /// Client approves the contract sent by caregiver.
        /// Client MUST provide service address when approving a caregiver-initiated contract.
        /// </summary>
        Task<ContractDTO> ClientApproveContractAsync(string contractId, string clientId, ClientContractApprovalRequest? request = null);
        
        /// <summary>
        /// Client requests review/changes (only allowed in Round 1).
        /// </summary>
        Task<ContractDTO> ClientRequestReviewAsync(string contractId, string clientId, ClientContractReviewRequestDTO request);
        
        /// <summary>
        /// Caregiver revises contract after client review request (Round 2).
        /// </summary>
        Task<ContractDTO> CaregiverReviseContractAsync(string caregiverId, CaregiverContractRevisionDTO revision);
        
        /// <summary>
        /// Client rejects contract (only allowed in Round 2, triggers request for new caregiver).
        /// </summary>
        Task<ContractDTO> ClientRejectContractAsync(string contractId, string clientId, ClientContractRejectionDTO? request);

        // ========================================
        // CLIENT-INITIATED CONTRACT FLOW
        // ========================================

        /// <summary>
        /// Client generates contract with service address, access instructions, optional schedule and tasks.
        /// Caregiver must approve and confirm the schedule.
        /// </summary>
        Task<ContractDTO> ClientGenerateContractAsync(string clientId, ClientContractGenerationDTO request);

        /// <summary>
        /// Caregiver approves a client-initiated contract. Must provide/confirm the schedule.
        /// </summary>
        Task<ContractDTO> CaregiverApproveContractAsync(string contractId, string caregiverId, CaregiverContractApprovalRequest request);

        /// <summary>
        /// Caregiver requests review/changes on a client-initiated contract (Round 1 only).
        /// </summary>
        Task<ContractDTO> CaregiverRequestReviewAsync(string contractId, string caregiverId, CaregiverContractReviewRequestDTO request);

        /// <summary>
        /// Client revises contract after caregiver review request (Round 2, client-initiated flow).
        /// </summary>
        Task<ContractDTO> ClientReviseContractAsync(string clientId, ClientContractRevisionDTO revision);

        /// <summary>
        /// Caregiver rejects a client-initiated contract (Round 2 only).
        /// </summary>
        Task<ContractDTO> CaregiverRejectContractAsync(string contractId, string caregiverId, CaregiverContractRejectionDTO? request);

        /// <summary>
        /// Get contracts pending caregiver approval (client-initiated flow).
        /// </summary>
        Task<List<ContractDTO>> GetPendingContractsForCaregiverApprovalAsync(string caregiverId);

        // ========================================
        // SHARED: Negotiation & Queries
        // ========================================
        
        /// <summary>
        /// Get negotiation history for a contract (for safety/audit).
        /// </summary>
        Task<List<ContractNegotiationHistoryDTO>> GetNegotiationHistoryAsync(string contractId);

        /// <summary>
        /// Get contracts pending client approval.
        /// </summary>
        Task<List<ContractDTO>> GetPendingContractsForClientAsync(string clientId);

        // ========================================
        // LEGACY: Original Contract Generation (kept for backward compatibility)
        // ========================================
        
        // Contract Generation (triggered by payment success) - DEPRECATED for new flow
        Task<ContractDTO> GenerateContractAsync(ContractGenerationRequestDTO request);
        Task<ContractDTO> GenerateContractFromOrderAsync(string orderId);
        Task<bool> SendContractToCaregiverAsync(string contractId);

        // Caregiver Response Management - DEPRECATED for new flow
        Task<ContractDTO> ProcessCaregiverResponseAsync(CaregiverContractResponseDTO response);
        Task<ContractDTO> AcceptContractAsync(string contractId, string caregiverId);
        Task<ContractDTO> RejectContractAsync(string contractId, string caregiverId, string? reason);
        Task<ContractDTO> RequestContractReviewAsync(string contractId, string caregiverId, string? comments);
        Task<List<AlternativeCaregiverDTO>> GetAlternativeCaregiversAsync(string contractId);

        // ========================================
        // Contract Status Management (used by both flows)
        // ========================================
        Task<ContractDTO> GetContractByIdAsync(string contractId);
        Task<ContractGenerationDataDTO?> GetContractPdfDataAsync(string contractId);
        Task<ContractDTO?> GetContractByOrderIdAsync(string orderId);
        Task<List<ContractDTO>> GetContractsByClientIdAsync(string clientId);
        Task<List<ContractDTO>> GetContractsByCaregiverIdAsync(string caregiverId);
        Task<List<ContractDTO>> GetPendingContractsForCaregiverAsync(string caregiverId);
        Task<bool> UpdateContractStatusAsync(string contractId, string status);

        // Contract Analytics & Reporting
        Task<ContractAnalyticsDTO> GetContractAnalyticsAsync(string userId, string userType);
        Task<List<ContractDTO>> GetActiveContractsAsync();
        Task<List<ContractDTO>> GetExpiredContractsAsync();

        // Contract History & Revisions - LEGACY
        Task<ContractDTO> ReviseContractAsync(string contractId, ContractRevisionRequestDTO revision);
        Task<List<ContractHistoryDTO>> GetContractHistoryAsync(string contractId);

        // Contract Lifecycle
        Task<bool> ExpireContractAsync(string contractId);
        Task<bool> CompleteContractAsync(string contractId, decimal? rating = null);
        Task<bool> TerminateContractAsync(string contractId, string reason);

        // Client Contract Management
        Task<ContractDTO> SendContractToAlternativeCaregiverAsync(string originalContractId, string newCaregiverId);

        /// <summary>
        /// Admin: Cancel all non-terminal contracts (migration to negotiation flow).
        /// </summary>
        Task<int> CancelAllActiveContractsAsync();
    }

    public interface IContractNotificationService
    {
        // Caregiver-initiated flow: Notifications to client
        Task<bool> SendContractNotificationToClientAsync(string contractId);
        Task<bool> SendContractEmailToClientAsync(string contractId);
        Task<bool> NotifyCaregiverOfClientResponseAsync(string contractId, string response);
        Task<bool> SendContractReminderToClientAsync(string contractId);

        // Client-initiated flow: Notifications to caregiver
        Task<bool> SendContractNotificationToCaregiverForApprovalAsync(string contractId);
        Task<bool> NotifyClientOfCaregiverResponseAsync(string contractId, string response);
        
        // LEGACY: Notifications for client-initiated contracts
        Task<bool> SendContractNotificationToCaregiverAsync(string contractId);
        Task<bool> SendContractEmailToCaregiverAsync(string contractId);
        Task<bool> NotifyClientOfResponseAsync(string contractId, string response);
        Task<bool> SendContractReminderToCaregiverAsync(string contractId);
        Task<bool> NotifyClientOfExpiryAsync(string contractId);
        Task<bool> SendResponseEmailToClientAsync(string contractId, string response);
        Task<bool> CreateDashboardNotificationAsync(string userId, string message, string type, string contractId, string? orderId = null);
    }

    public interface IContractLLMService
    {
        Task<string> GenerateContractAsync(string gigId, PackageSelection package, List<ClientTask> tasks, decimal totalAmount);
        
        // NEW: Generate contract with full enriched data (real names, dates, etc.)
        Task<string> GenerateContractWithScheduleAsync(ContractGenerationDataDTO data);
        
        Task<string> GenerateContractSummaryAsync(string contractContent);
        Task<string> ReviseContractAsync(string originalContract, string revisionNotes);
        Task<bool> IsLLMAvailableAsync();
    }
}