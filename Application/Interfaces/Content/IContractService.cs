using Application.DTOs;
using Domain.Entities;

namespace Application.Interfaces.Content
{
    public interface IContractService
    {
        // Contract Generation (triggered by payment success)
        Task<ContractDTO> GenerateContractAsync(ContractGenerationRequestDTO request);
        Task<bool> SendContractToCaregiverAsync(string contractId);
        
        // Caregiver Response Management
        Task<ContractDTO> ProcessCaregiverResponseAsync(CaregiverContractResponseDTO response);
        Task<List<AlternativeCaregiverDTO>> GetAlternativeCaregiversAsync(string contractId);
        
        // Contract Status Management
        Task<ContractDTO> GetContractByIdAsync(string contractId);
        Task<List<ContractDTO>> GetContractsByClientIdAsync(string clientId);
        Task<List<ContractDTO>> GetContractsByCaregiverIdAsync(string caregiverId);
        Task<List<ContractDTO>> GetPendingContractsForCaregiverAsync(string caregiverId);
        Task<bool> UpdateContractStatusAsync(string contractId, string status);
        
        // Contract Analytics & Reporting
        Task<ContractAnalyticsDTO> GetContractAnalyticsAsync(string userId, string userType);
        Task<List<ContractDTO>> GetActiveContractsAsync();
        Task<List<ContractDTO>> GetExpiredContractsAsync();
        
        // Contract History & Revisions
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
        Task<string> GenerateContractSummaryAsync(string contractContent);
        Task<string> ReviseContractAsync(string originalContract, string revisionNotes);
        Task<bool> IsLLMAvailableAsync();
    }
}