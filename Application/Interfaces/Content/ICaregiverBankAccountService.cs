using Application.DTOs;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    public interface ICaregiverBankAccountService
    {
        Task<CaregiverBankAccountResponse> GetBankAccountAsync(string caregiverId);
        Task<CaregiverBankAccountResponse> CreateOrUpdateBankAccountAsync(string caregiverId, CaregiverBankAccountRequest request);
        Task<AdminCaregiverFinancialSummary> GetAdminCaregiverFinancialSummaryAsync(string caregiverId);
    }
}
