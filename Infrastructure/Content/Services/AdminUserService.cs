using Application.DTOs;
using Application.Interfaces.Content;
using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Content.Services
{
    public class AdminUserService : IAdminUserService
    {
        private readonly CareProDbContext careProDbContext;

        public AdminUserService(CareProDbContext careProDbContext)
        {
            this.careProDbContext = careProDbContext;
        }

        public async Task<string> CreateAdminUserAsync(AddAdminUserRequest addAdminUserRequest)
        {
            var normalizedEmail = addAdminUserRequest.Email.ToLower();

            // Check for duplicate email across ALL collections (case-insensitive)
            var adminUserExist = await careProDbContext.AdminUsers.FirstOrDefaultAsync(x => x.Email.ToLower() == normalizedEmail);
            if (adminUserExist != null)
                throw new InvalidOperationException("User already exists. Kindly login or use 'Forgot Password'!");

            var appUserExist = await careProDbContext.AppUsers.FirstOrDefaultAsync(x => x.Email.ToLower() == normalizedEmail);
            if (appUserExist != null)
                throw new InvalidOperationException("This email is already registered. Please use a different email.");

            var clientExist = await careProDbContext.Clients.FirstOrDefaultAsync(x => x.Email.ToLower() == normalizedEmail);
            if (clientExist != null)
                throw new InvalidOperationException("This email is already registered as a Client. Please use a different email.");

            var caregiverExist = await careProDbContext.CareGivers.FirstOrDefaultAsync(x => x.Email.ToLower() == normalizedEmail);
            if (caregiverExist != null)
                throw new InvalidOperationException("This email is already registered as a Caregiver. Please use a different email.");

            var hashedPassword = BCrypt.Net.BCrypt.HashPassword(addAdminUserRequest.Password);

            /// CONVERT DTO TO DOMAIN OBJECT            
            var adminUser = new AdminUser
            {
                FirstName = addAdminUserRequest.FirstName,
                MiddleName = addAdminUserRequest.MiddleName,
                LastName = addAdminUserRequest.LastName,
                Email = addAdminUserRequest.Email.ToLower(),
                PhoneNo = addAdminUserRequest.PhoneNo,
                Password = hashedPassword,

                // Assign new ID
                Id = ObjectId.GenerateNewId(),
                Role = addAdminUserRequest.Role,
                // Status = true,
                IsDeleted = false,


                CreatedAt = DateTime.UtcNow,
            };

            await careProDbContext.AdminUsers.AddAsync(adminUser);

            var careProAppUser = new AppUser
            {

                Email = addAdminUserRequest.Email.ToLower(),
                Password = hashedPassword,
                FirstName = addAdminUserRequest.FirstName,
                LastName = addAdminUserRequest.LastName,

                // Assign new ID
                Id = ObjectId.GenerateNewId(),
                AppUserId = adminUser.Id,
                //Role = Roles.Caregiver.ToString(),
                Role = adminUser.Role,
                EmailConfirmed = true,
                IsDeleted = false,
                CreatedAt = adminUser.CreatedAt,
            };


            await careProDbContext.AppUsers.AddAsync(careProAppUser);

            await careProDbContext.SaveChangesAsync();



            return adminUser.Id.ToString();
        }

        public async Task<AdminUserResponse> GetAdminUserByIdAsync(string adminUserId)
        {
            var adminUser = await careProDbContext.CareGivers.FirstOrDefaultAsync(x => x.Id.ToString() == adminUserId);

            if (adminUser == null)
            {
                throw new KeyNotFoundException($"Caregiver with ID '{adminUserId}' not found.");
            }


            var adminUserDTO = new AdminUserResponse()
            {
                Id = adminUser.Id.ToString(),
                FirstName = adminUser.FirstName,
                MiddleName = adminUser.MiddleName,
                LastName = adminUser.LastName,
                Email = adminUser.Email,
                PhoneNo = adminUser.PhoneNo,
                Role = adminUser.Role,
                IsDeleted = adminUser.IsDeleted,

                CreatedAt = adminUser.CreatedAt,
            };

            return adminUserDTO;
        }

        public async Task<IEnumerable<AdminUserResponse>> GetAllAdminUsersAsync()
        {
            var adminUsers = await careProDbContext.AdminUsers
                .Where(x => x.IsDeleted == false)
                .OrderBy(x => x.FirstName)
                .ToListAsync();

            var adminUsersDTO = new List<AdminUserResponse>();

            foreach (var adminUser in adminUsers)
            {

                var adminUserDTO = new AdminUserResponse()
                {
                    Id = adminUser.Id.ToString(),
                    FirstName = adminUser.FirstName,
                    MiddleName = adminUser.MiddleName,
                    LastName = adminUser.LastName,
                    Email = adminUser.Email,
                    PhoneNo = adminUser.PhoneNo,
                    Role = adminUser.Role,
                    IsDeleted = adminUser.IsDeleted,
                    Status = adminUser.Status,



                    CreatedAt = adminUser.CreatedAt,
                };


                adminUsersDTO.Add(adminUserDTO);
            }

            return adminUsersDTO;
        }
    }
}
