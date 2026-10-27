using Application.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    public interface IAdminUserService
    {
        Task<string> CreateAdminUserAsync(AddAdminUserRequest addAdminUserRequest);

        Task<IEnumerable<AdminUserResponse>> GetAllAdminUsersAsync();

        Task<AdminUserResponse> GetAdminUserByIdAsync(string adminUserId);

        Task<AdminUserResponse> UpdateAdminApprovalStatusAsync(string adminUserId, string status);

        Task<DashboardStatsResponse> GetDashboardStatsAsync();

        Task<GrantClientOnboardingResetAccessResponse> GrantClientOnboardingResetAccessAsync(GrantClientOnboardingResetAccessRequest request);

    }
}
