using Application.DTOs;
using Application.Interfaces.Authentication;
using Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Content.Services.Authentication
{
    public class TokenHandler : ITokenHandler
    {
        private readonly IConfiguration configuration;

        public TokenHandler(IConfiguration configuration)
        {
            this.configuration = configuration;
        }


        public Task<string> CreateTokenAsync(AppUserDTO appUserDTO)
        {
            // Create Claims
            var claims = new List<Claim>();
            claims.Add(new Claim(ClaimTypes.Email, appUserDTO.Email));
            claims.Add(new Claim(ClaimTypes.Role, appUserDTO.Role));
            claims.Add(new Claim("userId", appUserDTO.AppUserId));
            if (!string.IsNullOrEmpty(appUserDTO.Department))
                claims.Add(new Claim("department", appUserDTO.Department));
            if (appUserDTO.QaAccess)
                claims.Add(new Claim("qa_access", "true"));
            if (appUserDTO.OnboardingResetAccess)
                claims.Add(new Claim("onboarding_reset_access", "true"));

            // Get JWT configuration with null checks
            var jwtKey = configuration["Jwt:Key"] ?? throw new InvalidOperationException("JWT Key not found in configuration");
            var jwtIssuer = configuration["Jwt:Issuer"] ?? throw new InvalidOperationException("JWT Issuer not found in configuration");
            var jwtAudience = configuration["Jwt:Audience"] ?? throw new InvalidOperationException("JWT Audience not found in configuration");

            if (string.IsNullOrEmpty(jwtKey))
                throw new InvalidOperationException("JWT Key cannot be null or empty");

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            // Get token expiration from configuration, default to 30 minutes
            var tokenExpirationHours = configuration.GetValue<double>("Jwt:DurationInHours", 0.5); // 30 minutes default
            
            var token = new JwtSecurityToken(
                jwtIssuer,
                jwtAudience,
                claims,
                expires: DateTime.UtcNow.AddHours(tokenExpirationHours),
                signingCredentials: credentials);

            return Task.FromResult(new JwtSecurityTokenHandler().WriteToken(token));
        }

        public string GenerateRefreshToken()
        {
            // Generate a cryptographically secure random string
            var randomBytes = new byte[64];
            using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
            rng.GetBytes(randomBytes);
            return Convert.ToBase64String(randomBytes);
        }



        public string GeneratePasswordResetToken(string userId, string email)
        {
            var secretKey = configuration["JwtSettings:Secret"] ?? throw new InvalidOperationException("JWT Secret not found");
            var issuer = configuration["JwtSettings:Issuer"] ?? throw new InvalidOperationException("JWT Issuer not found");
            var audience = configuration["JwtSettings:Audience"] ?? throw new InvalidOperationException("JWT Audience not found");
            var expiresInMinutes = configuration["JwtSettings:ExpiresInMinutes"] ?? "30";
            var expires = DateTime.UtcNow.AddMinutes(int.Parse(expiresInMinutes));

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
            new Claim(JwtRegisteredClaimNames.Sub, email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim("userId", userId)
        };

            var token = new JwtSecurityToken(
                issuer,
                audience,
                claims,
                expires: expires,
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);

        }



        public string GenerateEmailVerificationToken(string userId, string email, string secretKey, int expireMinutes = 30)
        {
            var issuer = configuration["JwtSettings:Issuer"] ?? throw new InvalidOperationException("JWT Issuer not found");
            var audience = configuration["JwtSettings:Audience"] ?? throw new InvalidOperationException("JWT Audience not found");

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: new[]
                {
                new Claim("userId", userId),
                new Claim("email", email)
                },
                expires: DateTime.UtcNow.AddMinutes(expireMinutes),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public string GenerateCancellationToken(string userId)
        {
            var secretKey = configuration["JwtSettings:Secret"] ?? throw new InvalidOperationException("JWT Secret not found");
            var issuer = configuration["JwtSettings:Issuer"] ?? throw new InvalidOperationException("JWT Issuer not found");
            var audience = configuration["JwtSettings:Audience"] ?? throw new InvalidOperationException("JWT Audience not found");

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim("userId", userId),
                new Claim("purpose", "account_deletion_cancel"),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var token = new JwtSecurityToken(
                issuer,
                audience,
                claims,
                expires: DateTime.UtcNow.AddDays(30),
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public string GenerateEmailUnsubscribeToken(string userId, string email, string preferenceScope)
        {
            var secretKey = configuration["JwtSettings:Secret"] ?? throw new InvalidOperationException("JWT Secret not found");
            var issuer = configuration["JwtSettings:Issuer"] ?? throw new InvalidOperationException("JWT Issuer not found");
            var audience = configuration["JwtSettings:Audience"] ?? throw new InvalidOperationException("JWT Audience not found");

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim("userId", userId),
                new Claim(ClaimTypes.Email, email),
                new Claim("purpose", "email_unsubscribe"),
                new Claim("preference_scope", preferenceScope),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var token = new JwtSecurityToken(
                issuer,
                audience,
                claims,
                expires: DateTime.UtcNow.AddDays(30),
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public (bool IsValid, string? UserId, string? Email, string? PreferenceScope, string? Error) ValidateEmailUnsubscribeToken(string token)
        {
            var secretKey = configuration["JwtSettings:Secret"];
            var issuer = configuration["JwtSettings:Issuer"];
            var audience = configuration["JwtSettings:Audience"];

            if (string.IsNullOrWhiteSpace(secretKey) || string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(audience))
            {
                return (false, null, null, null, "Token validation configuration is missing.");
            }

            var handler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(secretKey);

            try
            {
                var principal = handler.ValidateToken(token, new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidIssuer = issuer,
                    ValidAudience = audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ClockSkew = TimeSpan.Zero
                }, out _);

                var purpose = principal.FindFirst("purpose")?.Value;
                if (!string.Equals(purpose, "email_unsubscribe", StringComparison.Ordinal))
                {
                    return (false, null, null, null, "Invalid token purpose.");
                }

                var userId = principal.FindFirst("userId")?.Value;
                var email = principal.FindFirst(ClaimTypes.Email)?.Value;
                var preferenceScope = principal.FindFirst("preference_scope")?.Value;

                if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(preferenceScope))
                {
                    return (false, null, null, null, "Token payload is incomplete.");
                }

                return (true, userId, email, preferenceScope, null);
            }
            catch (Exception ex)
            {
                return (false, null, null, null, ex.Message);
            }
        }

        //public static string GenerateEmailVerificationToken(AppUser user, string jwtSecret, int expireMinutes = 30)
        //{
        //    var tokenHandler = new JwtSecurityTokenHandler();
        //    var key = Encoding.ASCII.GetBytes(jwtSecret);

        //    var tokenDescriptor = new SecurityTokenDescriptor
        //    {
        //        Subject = new ClaimsIdentity(new[]
        //        {
        //    new Claim("userId", user.AppUserId.ToString()),
        //    new Claim("email", user.Email),
        //}),
        //        Expires = DateTime.UtcNow.AddMinutes(expireMinutes),
        //        SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        //    };

        //    var token = tokenHandler.CreateToken(tokenDescriptor);
        //    return tokenHandler.WriteToken(token);
        //}


    }
}
