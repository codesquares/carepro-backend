using Application.DTOs;
using Application.Interfaces.Authentication;
using Domain.Entities;
using Microsoft.AspNet.Identity;
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

        
        public Task<string> CreateTokenAsync(AppUserDTO  appUserDTO)
        {
            // Create Claims
            var claims = new List<Claim>();
            //claims.Add(new Claim(ClaimTypes.GivenName, appUser.FirstName));
            //claims.Add(new Claim(ClaimTypes.Surname, appUser.LastName));
            claims.Add(new Claim(ClaimTypes.Email, appUserDTO.Email));
            claims.Add(new Claim(ClaimTypes.Role, appUserDTO.Role));

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Jwt:Key"]));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                configuration["Jwt:Issuer"],
                configuration["Jwt:Audience"],
                claims,
                expires: DateTime.Now.AddMinutes(40),                
                

                signingCredentials: credentials);

            return Task.FromResult(new JwtSecurityTokenHandler().WriteToken(token));
        }

       

        public string GeneratePasswordResetToken(string email)
        {
            var secretKey = configuration["JwtSettings:Secret"];
            var issuer = configuration["JwtSettings:Issuer"];
            var audience = configuration["JwtSettings:Audience"];
            var expires = DateTime.UtcNow.AddMinutes(int.Parse(configuration["JwtSettings:ExpiresInMinutes"]));

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
            new Claim(JwtRegisteredClaimNames.Sub, email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
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
            

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
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
