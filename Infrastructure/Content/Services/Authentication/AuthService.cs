using Application.DTOs;
using Application.DTOs.Authentication;
using Application.Interfaces.Authentication;
using Domain;
using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
        private readonly IConfiguration configuration;

        public AuthService(CareProDbContext careProDbContext, ITokenHandler tokenHandler, IConfiguration configuration)
        {
            this.careProDbContext = careProDbContext;
            this.tokenHandler = tokenHandler;
            this.configuration = configuration;
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
                        FirstName = appUser?.FirstName ?? string.Empty,
                        MiddleName = careGiverAppUser?.MiddleName ?? clientAppUser?.MiddleName,
                        LastName = appUser?.LastName ?? string.Empty,
                        // Use PhoneNo from caregiver if available; otherwise, fallback to a default or null
                        PhoneNo = careGiverAppUser?.PhoneNo ?? clientAppUser?.PhoneNo ?? "Not Provided",
                        // Use HomeAddress from client if available; otherwise, fallback to a default or null
                        HomeAddress = clientAppUser?.HomeAddress ?? careGiverAppUser?.HomeAddress ?? "Not Provided",



                        Role = appUser?.Role ?? "User",
                        Password = appUser?.Password ?? string.Empty,
                        CreatedAt = appUser?.CreatedAt ?? DateTime.UtcNow,
                    };

                    return appUserDetails;
                }
            }



            return null!;
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

            // ✅ Track first-time login before retrieving related data
            bool isFirstLogin = (appUser.LoginCount ?? 0) == 0 || appUser.LastLoginAt == null;
            appUser.LastLoginAt = DateTime.UtcNow;
            appUser.LoginCount = (appUser.LoginCount ?? 0) + 1;
            careProDbContext.AppUsers.Update(appUser);

            // Retrieve related role data
            var careGiverAppUser = await careProDbContext.CareGivers.FirstOrDefaultAsync(x => x.Id == appUser.AppUserId);
            var clientAppUser = await careProDbContext.Clients.FirstOrDefaultAsync(x => x.Id == appUser.AppUserId);
            var adminAppUser = await careProDbContext.AdminUsers.FirstOrDefaultAsync(x => x.Id == appUser.AppUserId);

            // Map AppUserDTO (optional intermediate step if you still want it for consistency)
            var appUserDetails = new AppUserDTO
            {
                AppUserId = appUser.AppUserId.ToString(),
                Email = appUser.Email,
                FirstName = appUser?.FirstName ?? string.Empty,
                MiddleName = careGiverAppUser?.MiddleName ?? clientAppUser?.MiddleName,
                LastName = appUser?.LastName ?? string.Empty,
                PhoneNo = careGiverAppUser?.PhoneNo ?? clientAppUser?.PhoneNo ?? "Not Provided",
                HomeAddress = clientAppUser?.HomeAddress ?? careGiverAppUser?.HomeAddress ?? "Not Provided",
                Role = appUser?.Role ?? "User",
                CreatedAt = appUser?.CreatedAt ?? DateTime.UtcNow,
            };

            // ✅ Generate JWT Token in the service
            var token = await tokenHandler.CreateTokenAsync(appUserDetails);
            
            // ✅ Generate Refresh Token
            var refreshToken = tokenHandler.GenerateRefreshToken();
            
            // ✅ Save refresh token to database
            var refreshTokenExpirationDays = configuration.GetValue<int>("Jwt:RefreshTokenExpirationDays", 30);
            var refreshTokenEntity = new RefreshToken
            {
                Token = refreshToken,
                UserId = appUserDetails.AppUserId,
                ExpiryDate = DateTime.UtcNow.AddDays(refreshTokenExpirationDays),
                CreatedAt = DateTime.UtcNow
            };
            
            await careProDbContext.RefreshTokens.AddAsync(refreshTokenEntity);
            await careProDbContext.SaveChangesAsync();

            // ✅ Build the response object
            return new LoginResponse
            {
                Id = appUserDetails.AppUserId,
                FirstName = appUserDetails.FirstName ?? string.Empty,
                MiddleName = appUserDetails.MiddleName,
                LastName = appUserDetails.LastName ?? string.Empty,
                Email = appUserDetails.Email,
                Role = appUserDetails.Role,
                Token = token,
                RefreshToken = refreshToken,
                IsFirstLogin = isFirstLogin
            };
        }

        public async Task<RefreshTokenResponse> RefreshTokenAsync(RefreshTokenRequest request)
        {
            // Find the refresh token in the database
            var refreshTokenEntity = await careProDbContext.RefreshTokens
                .FirstOrDefaultAsync(rt => rt.Token == request.RefreshToken);

            if (refreshTokenEntity == null || !refreshTokenEntity.IsActive)
            {
                throw new UnauthorizedAccessException("Invalid or expired refresh token");
            }

            // Get user details
            var appUser = await careProDbContext.AppUsers
                .FirstOrDefaultAsync(u => u.AppUserId.ToString() == refreshTokenEntity.UserId);

            if (appUser == null)
            {
                throw new UnauthorizedAccessException("User not found");
            }

            // Get role-specific user data
            var careGiverAppUser = await careProDbContext.CareGivers.FirstOrDefaultAsync(x => x.Id == appUser.AppUserId);
            var clientAppUser = await careProDbContext.Clients.FirstOrDefaultAsync(x => x.Id == appUser.AppUserId);
            var adminAppUser = await careProDbContext.AdminUsers.FirstOrDefaultAsync(x => x.Id == appUser.AppUserId);

            var appUserDetails = new AppUserDTO
            {
                AppUserId = appUser.AppUserId.ToString(),
                Email = appUser.Email,
                FirstName = appUser.FirstName ?? string.Empty,
                MiddleName = careGiverAppUser?.MiddleName ?? clientAppUser?.MiddleName,
                LastName = appUser.LastName ?? string.Empty,
                PhoneNo = careGiverAppUser?.PhoneNo ?? clientAppUser?.PhoneNo ?? "Not Provided",
                HomeAddress = clientAppUser?.HomeAddress ?? careGiverAppUser?.HomeAddress ?? "Not Provided",
                Role = appUser.Role,
                CreatedAt = appUser.CreatedAt,
            };

            // Generate new access token
            var newAccessToken = await tokenHandler.CreateTokenAsync(appUserDetails);

            // Generate new refresh token (rotating refresh tokens)
            var newRefreshToken = tokenHandler.GenerateRefreshToken();

            // Revoke the old refresh token
            refreshTokenEntity.IsRevoked = true;
            refreshTokenEntity.RevokedAt = DateTime.UtcNow;
            refreshTokenEntity.ReplacedByToken = newRefreshToken;

            // Create new refresh token entity
            var refreshTokenExpirationDays = configuration.GetValue<int>("Jwt:RefreshTokenExpirationDays", 30);
            var newRefreshTokenEntity = new RefreshToken
            {
                Token = newRefreshToken,
                UserId = appUser.AppUserId.ToString(),
                ExpiryDate = DateTime.UtcNow.AddDays(refreshTokenExpirationDays),
                CreatedAt = DateTime.UtcNow
            };

            await careProDbContext.RefreshTokens.AddAsync(newRefreshTokenEntity);
            await careProDbContext.SaveChangesAsync();

            var tokenExpirationHours = configuration.GetValue<double>("Jwt:DurationInHours", 0.5);
            return new RefreshTokenResponse
            {
                Token = newAccessToken,
                RefreshToken = newRefreshToken,
                ExpiresAt = DateTime.UtcNow.AddHours(tokenExpirationHours)
            };
        }

        public async Task<bool> RevokeRefreshTokenAsync(string refreshToken)
        {
            var refreshTokenEntity = await careProDbContext.RefreshTokens
                .FirstOrDefaultAsync(rt => rt.Token == refreshToken);

            if (refreshTokenEntity == null)
            {
                return false;
            }

            refreshTokenEntity.IsRevoked = true;
            refreshTokenEntity.RevokedAt = DateTime.UtcNow;

            await careProDbContext.SaveChangesAsync();
            return true;
        }


    }
}
