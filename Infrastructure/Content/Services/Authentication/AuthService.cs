using Application.DTOs;
using Application.DTOs.Authentication;
using Application.Interfaces.Authentication;
using Domain;
using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Authentication;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Content.Services.Authentication
{
    public class AuthService : IAuthService
    {
        private readonly CareProDbContext careProDbContext;
        private readonly ITokenHandler tokenHandler;

        public AuthService(CareProDbContext careProDbContext, ITokenHandler tokenHandler)
        {
            this.careProDbContext = careProDbContext;
            this.tokenHandler = tokenHandler;
        }
        
        
        public async Task<AppUserDTO> AuthenticateUserAsync(LoginRequest loginRequest)
        {      
            var appUser = await careProDbContext.AppUsers
                .FirstOrDefaultAsync(x => x.Email.ToLower() == loginRequest.Email.ToLower());
           

            if (appUser != null)
            {
                // ✅ Check email confirmation before proceeding
                if (!appUser.EmailConfirmed)
                {
                    throw new UnauthorizedAccessException("Email not yet verified. Please check your inbox to verify your account, or request for Resend Confirmation.");
                }


                //return appUser;
                var careGiverAppUser = await careProDbContext.CareGivers.FirstOrDefaultAsync(x => x.Id == appUser.AppUserId);
                var clientAppUser = await careProDbContext.Clients.FirstOrDefaultAsync(x => x.Id == appUser.AppUserId);
                var adminAppUser = await careProDbContext.AdminUsers.FirstOrDefaultAsync(x => x.Id == appUser.AppUserId);

                if (careGiverAppUser != null || clientAppUser != null || adminAppUser != null)
                {
                    var appUserDetails = new AppUserDTO()
                    {
                        AppUserId = appUser.AppUserId.ToString(),
                        Email = appUser.Email,
                       // FirstName = careGiverAppUser?.FirstName ?? clientAppUser?.FirstName,
                        FirstName = appUser?.FirstName,
                        MiddleName = careGiverAppUser?.MiddleName ?? clientAppUser?.MiddleName,
                        LastName = appUser?.LastName,
                        // Use PhoneNo from caregiver if available; otherwise, fallback to a default or null
                        PhoneNo = careGiverAppUser?.PhoneNo ?? clientAppUser?.PhoneNo ?? "Not Provided",
                        // Use HomeAddress from client if available; otherwise, fallback to a default or null
                        HomeAddress = clientAppUser?.HomeAddress ?? careGiverAppUser?.HomeAddress ?? "Not Provided",

                        

                        Role = appUser.Role,
                        Password = appUser.Password,
                        CreatedAt = appUser.CreatedAt,
                    };

                    return appUserDetails;
                }
            }

            
          
            return null;
        }




        public async Task<LoginResponse> AuthenticateUserLoginAsync(LoginRequest loginRequest)
        {
            var appUser = await careProDbContext.AppUsers
                .FirstOrDefaultAsync(x => x.Email.ToLower() == loginRequest.Email.ToLower());

            if (appUser == null)
                throw new UnauthorizedAccessException("Account does not exist, please check the e-mail you entered or Click on Sign Up!");

            if (!appUser.EmailConfirmed && appUser.Role != "Admin")
                throw new UnauthorizedAccessException("Email not yet verified. Please check your inbox to verify your account, or request a resend confirmation.");

            // ✅ Verify password here
            bool isValidPassword = BCrypt.Net.BCrypt.Verify(loginRequest.Password, appUser.Password);
            if (!isValidPassword)
                throw new UnauthorizedAccessException("Incorrect Password.");

            // Retrieve related role data
            var careGiverAppUser = await careProDbContext.CareGivers.FirstOrDefaultAsync(x => x.Id == appUser.AppUserId);
            var clientAppUser = await careProDbContext.Clients.FirstOrDefaultAsync(x => x.Id == appUser.AppUserId);
            var adminAppUser = await careProDbContext.AdminUsers.FirstOrDefaultAsync(x => x.Id == appUser.AppUserId);

            // Map AppUserDTO (optional intermediate step if you still want it for consistency)
            var appUserDetails = new AppUserDTO
            {
                AppUserId = appUser.AppUserId.ToString(),
                Email = appUser.Email,
                FirstName = appUser?.FirstName,
                MiddleName = careGiverAppUser?.MiddleName ?? clientAppUser?.MiddleName,
                LastName = appUser?.LastName,
                PhoneNo = careGiverAppUser?.PhoneNo ?? clientAppUser?.PhoneNo ?? "Not Provided",
                HomeAddress = clientAppUser?.HomeAddress ?? careGiverAppUser?.HomeAddress ?? "Not Provided",
                Role = appUser.Role,
                CreatedAt = appUser.CreatedAt,
            };

            // ✅ Generate JWT Token in the service
            var token = await tokenHandler.CreateTokenAsync(appUserDetails);

            // ✅ Build the response object
            return new LoginResponse
            {
                Id = appUserDetails.AppUserId,
                FirstName = appUserDetails.FirstName,
                MiddleName = appUserDetails.MiddleName,
                LastName = appUserDetails.LastName,
                Email = appUserDetails.Email,
                Role = appUserDetails.Role,
                Token = token
            };
        }


    }
}
