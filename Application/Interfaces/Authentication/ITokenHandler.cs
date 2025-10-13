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
        Task<string> CreateTokenAsync(AppUserDTO  appUserDTO);

        string GeneratePasswordResetToken(string email);


        //string GenerateEmailVerificationToken(AppUser user, string jwtSecret, int expireMinutes = 30);
        string GenerateEmailVerificationToken(string userId, string email, string secretKey, int expireMinutes = 30);

       // string GenerateEmailVerificationToken(string userId, string email, string secretKey, int expireMinutes = 30);
    }
}
