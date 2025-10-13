using Application.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    public interface IWithdrawalRequestService
    {
        Task<WithdrawalRequestResponse> GetWithdrawalRequestByIdAsync(string withdrawalRequestId);
        Task<WithdrawalRequestResponse> GetWithdrawalRequestByTokenAsync(string token);
        Task<List<WithdrawalRequestResponse>> GetAllWithdrawalRequestsAsync();
        Task<List<WithdrawalRequestResponse>> GetWithdrawalRequestsByCaregiverIdAsync(string caregiverId);
        Task<List<CaregiverWithdrawalHistoryResponse>> GetCaregiverWithdrawalRequestHistoryAsync(string caregiverId);
        Task<List<WithdrawalRequestResponse>> GetWithdrawalRequestsByStatusAsync(string status);

        Task<WithdrawalRequestResponse> CreateWithdrawalRequestAsync(CreateWithdrawalRequestRequest request);
       // Task<CaregiverWithdrawalSummaryResponse> GetTotalWithdrawnByCaregiverIdAsync(string caregiverId);
        Task<decimal> GetTotalWithdrawnByCaregiverIdAsync(string caregiverId);
        Task<CaregiverWithdrawalSummaryResponse> GetTotalAmountEarnedAndWithdrawnByCaregiverIdAsync(string caregiverId);

        Task<WithdrawalRequestResponse> VerifyWithdrawalRequestAsync(AdminWithdrawalVerificationRequest request);
        Task<WithdrawalRequestResponse> CompleteWithdrawalRequestAsync(string token, string adminId);
        Task<WithdrawalRequestResponse> RejectWithdrawalRequestAsync(AdminWithdrawalVerificationRequest request);
        Task<bool> TokenExists(string token);
        Task<bool> HasPendingRequest(string caregiverId);
    }
}
