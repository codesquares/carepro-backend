using Application.DTOs.Account;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Interfaces.Authentication
{
    public interface IAuthResponseService
    {
        //For signup logic
        Task<AuthResponse> SignUpAsync(SignUp signUpModel, string? orgin);

        //For login logic
        Task<AuthResponse> LoginAsync(Login model);

        //for addroles logic
        Task<string> AssignRolesAsync(AssignRolesDto model);

        //for checking if the sent token is valid
        Task<AuthResponse> RefreshTokenCheckAsync(string token);

        // for revoking refreshrokens
        Task<bool> RevokeTokenAsync(string token);

        Task<string> ConfirmEmailAsync(string userId, string code);

        Task<List<AuthResponse>> GetAllApplicantUsersAsync(int pageNumber, int pageSize);

        Task<AuthResponse> GetApplicantUserAsync(string applicantUserId);
    }
}
