using Application.DTOs;

namespace Application.Interfaces.Content
{
    public interface IContractService
    {
        Task<ContractDTO> GetContractByIdAsync(string contractId);
        Task<ContractGenerationDataDTO?> GetContractPdfDataAsync(string contractId);
        Task<List<ContractDTO>> GetContractsByClientIdAsync(string clientId);
        Task<List<ContractDTO>> GetContractsByCaregiverIdAsync(string caregiverId);
        Task<bool> UpdateContractStatusAsync(string contractId, string status);

        // Contract Analytics & Reporting
        Task<ContractAnalyticsDTO> GetContractAnalyticsAsync(string userId, string userType);
        Task<List<ContractDTO>> GetActiveContractsAsync();
        Task<List<ContractDTO>> GetExpiredContractsAsync();

        Task<List<ContractHistoryDTO>> GetContractHistoryAsync(string contractId);

        // Contract Lifecycle
        Task<bool> ExpireContractAsync(string contractId);
        Task<bool> CompleteContractAsync(string contractId, decimal? rating = null);
        Task<bool> TerminateContractAsync(string contractId, string reason);

        /// <summary>
        /// Client stamps their real-time GPS onto the contract's service location.
        /// This replaces any previously geocoded coordinates and enables accurate 1500m proximity enforcement.
        /// </summary>
        Task<SetServiceLocationResponse> SetServiceLocationAsync(string contractId, string clientId, SetServiceLocationRequest request);
    }
}
