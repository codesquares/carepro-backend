using Application.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    public interface IEarningsService
    {
        Task<EarningsResponse> GetEarningsByIdAsync(string id);
        Task<EarningsResponse> GetEarningsByCaregiverIdAsync(string caregiverId);
        Task <CaregiverEarningSummaryResponse> GetEarningByCaregiverIdAsync(string caregiverId);
        Task<EarningsDTO> CreateEarningsAsync(CreateEarningsRequest request);
        Task<string> CreateEarningsAsync(AddEarningsRequest addEarningsRequest );
        Task<EarningsDTO> UpdateEarningsAsync(string id, UpdateEarningsRequest request);
        Task<bool> UpdateWithdrawalAmountsAsync(string caregiverId, decimal withdrawalAmount);
        Task<bool> DoesEarningsExistForCaregiverAsync(string caregiverId);


        Task <IEnumerable<EarningsResponse>> GetAllCaregiverEarningAsync(string caregiverId);
        Task<IEnumerable<TransactionHistoryResponse>> GetCaregiverTransactionHistoryAsync(string caregiverId);

    }
}
