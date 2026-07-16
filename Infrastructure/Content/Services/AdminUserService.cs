using Application.DTOs;
using Application.Interfaces.Content;
using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using MongoDB.Bson;

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

            // Validate department for Admin role
            if (addAdminUserRequest.Role == "Admin")
            {
                if (string.IsNullOrWhiteSpace(addAdminUserRequest.Department))
                    throw new InvalidOperationException("Department is required for Admin role.");
                if (!AdminDepartments.IsValid(addAdminUserRequest.Department))
                    throw new InvalidOperationException($"Invalid department. Must be one of: {string.Join(", ", AdminDepartments.All)}");
            }

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
                Department = addAdminUserRequest.Role == "SuperAdmin" ? null : addAdminUserRequest.Department,
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
            var adminUser = await careProDbContext.AdminUsers.FirstOrDefaultAsync(x => x.Id.ToString() == adminUserId);

            if (adminUser == null)
            {
                throw new KeyNotFoundException($"Admin user with ID '{adminUserId}' not found.");
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
                Department = adminUser.Department,
                IsDeleted = adminUser.IsDeleted,
                Status = adminUser.Status,

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
                    Department = adminUser.Department,
                    IsDeleted = adminUser.IsDeleted,
                    Status = adminUser.Status,



                    CreatedAt = adminUser.CreatedAt,
                };


                adminUsersDTO.Add(adminUserDTO);
            }

            return adminUsersDTO;
        }

        public async Task<DashboardStatsResponse> GetDashboardStatsAsync()
        {
            var totalCaregivers = await careProDbContext.CareGivers.CountAsync(x => !x.IsDeleted);
            var totalClients = await careProDbContext.Clients.CountAsync(x => !x.IsDeleted);
            var totalOrders = await careProDbContext.ClientOrders.CountAsync();
            var totalGigs = await careProDbContext.Gigs.CountAsync(x => x.IsDeleted != true);
            var pendingCertificates = await careProDbContext.Certifications
                .CountAsync(x => x.VerificationStatus == DocumentVerificationStatus.PendingVerification
                              || x.VerificationStatus == DocumentVerificationStatus.ManualReviewRequired);
            var activeSubscriptions = await careProDbContext.Subscriptions
                .CountAsync(x => x.Status == SubscriptionStatus.Active);
            var pendingWithdrawals = await careProDbContext.WithdrawalRequests
                .CountAsync(x => x.Status == "Pending");
            var totalAdmins = await careProDbContext.AdminUsers.CountAsync(x => !x.IsDeleted);

            return new DashboardStatsResponse
            {
                TotalCaregivers = totalCaregivers,
                TotalClients = totalClients,
                TotalOrders = totalOrders,
                TotalGigs = totalGigs,
                PendingCertificates = pendingCertificates,
                ActiveSubscriptions = activeSubscriptions,
                PendingWithdrawals = pendingWithdrawals,
                TotalAdmins = totalAdmins,
            };
        }

        public async Task<GrantClientOnboardingResetAccessResponse> GrantClientOnboardingResetAccessAsync(GrantClientOnboardingResetAccessRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Email) && string.IsNullOrWhiteSpace(request.AppUserId))
            {
                throw new InvalidOperationException("Provide either email or appUserId.");
            }

            var normalizedEmail = request.Email?.Trim().ToLowerInvariant();
            var normalizedAppUserId = request.AppUserId?.Trim();

            var appUser = await careProDbContext.AppUsers.FirstOrDefaultAsync(x =>
                (!string.IsNullOrWhiteSpace(normalizedEmail) && x.Email.ToLower() == normalizedEmail)
                || (!string.IsNullOrWhiteSpace(normalizedAppUserId) && x.AppUserId.ToString() == normalizedAppUserId));

            if (appUser == null)
            {
                throw new KeyNotFoundException("Target app user not found.");
            }

            if (!string.Equals(appUser.Role, "Client", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Only Client accounts can be granted onboarding reset QA access.");
            }

            appUser.QaAccess = request.QaAccess;
            appUser.OnboardingResetAccess = request.OnboardingResetAccess;

            careProDbContext.AppUsers.Update(appUser);
            await careProDbContext.SaveChangesAsync();

            return new GrantClientOnboardingResetAccessResponse
            {
                AppUserId = appUser.AppUserId.ToString(),
                Email = appUser.Email,
                Role = appUser.Role,
                QaAccess = appUser.QaAccess ?? false,
                OnboardingResetAccess = appUser.OnboardingResetAccess ?? false,
                Message = "Access flags updated. User must login again (or refresh token) to receive new claims."
            };
        }
    }
}
