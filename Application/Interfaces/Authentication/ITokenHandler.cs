using Application.DTOs;
using Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Interfaces.Authentication
{
    public interface ITokenHandler
    {
        Task<string> CreateTokenAsync(AppUserDTO appUserDTO);

        string GenerateRefreshToken();

        string GeneratePasswordResetToken(string userId, string email);

        /// <summary>
        /// Generates a 30-day signed JWT used in the account deletion cancellation email link.
        /// The token carries a "purpose" claim of "account_deletion_cancel" to prevent
        /// it being used in place of other token types.
        /// </summary>
        string GenerateCancellationToken(string userId);

        //string GenerateEmailVerificationToken(AppUser user, string jwtSecret, int expireMinutes = 30);
        string GenerateEmailVerificationToken(string userId, string email, string secretKey, int expireMinutes = 30);

        // string GenerateEmailVerificationToken(string userId, string email, string secretKey, int expireMinutes = 30);
    }
}
