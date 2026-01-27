using Application.DTOs;
using Domain.Entities;

namespace Application.Interfaces.Content
{
    public interface IContractService
    {
        // ========================================
        // NEW FLOW: Caregiver-Initiated Contract Generation
        // ========================================
        
        /// <summary>
        /// Caregiver generates contract after agreeing on schedule with client.
        /// Triggered when caregiver clicks "Generate Contract" on an order.
        /// </summary>
        Task<ContractDTO> CaregiverGenerateContractAsync(string caregiverId, CaregiverContractGenerationDTO request);
        
        /// <summary>
        /// Client approves the contract sent by caregiver.
        /// </summary>
        Task<ContractDTO> ClientApproveContractAsync(string contractId, string clientId);
        
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
    }

    public interface IContractNotificationService
    {
        // NEW FLOW: Notifications for caregiver-initiated contracts
        Task<bool> SendContractNotificationToClientAsync(string contractId);
        Task<bool> SendContractEmailToClientAsync(string contractId);
        Task<bool> NotifyCaregiverOfClientResponseAsync(string contractId, string response);
        Task<bool> SendContractReminderToClientAsync(string contractId);
        
        // LEGACY: Notifications for client-initiated contracts
        Task<bool> SendContractNotificationToCaregiverAsync(string contractId);
        Task<bool> SendContractEmailToCaregiverAsync(string contractId);
        Task<bool> NotifyClientOfResponseAsync(string contractId, string response);
        Task<bool> SendContractReminderToCaregiverAsync(string contractId);
        Task<bool> NotifyClientOfExpiryAsync(string contractId);
        Task<bool> SendResponseEmailToClientAsync(string contractId, string response);
        Task<bool> CreateDashboardNotificationAsync(string userId, string message, string type, string contractId);
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