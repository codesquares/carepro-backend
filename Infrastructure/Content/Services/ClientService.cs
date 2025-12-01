using Application.DTOs;
using Application.Interfaces;
using Application.Interfaces.Common;
using Application.Interfaces.Content;
using Domain.Entities;
using Domain;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Authentication;
using System.Text;
using System.Threading.Tasks;
using Infrastructure.Services;
using Application.Interfaces.Authentication;
using Application.Interfaces.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Web;
using System.Security.Claims;

namespace Infrastructure.Content.Services
{
    public class ClientService : IClientService
    {
        private readonly CareProDbContext careProDbContext;
        private readonly CloudinaryService cloudinaryService;
        private readonly ITokenHandler tokenHandler;
        private readonly IEmailService emailService;
        private readonly IConfiguration configuration;
        private readonly IOriginValidationService originValidationService;
        private readonly ILocationService locationService;
        private readonly IGoogleSheetsService googleSheetsService;
        private readonly ILogger<ClientService> logger;

        public ClientService(CareProDbContext careProDbContext, CloudinaryService cloudinaryService, ITokenHandler tokenHandler, IEmailService emailService, IConfiguration configuration, IOriginValidationService originValidationService, ILocationService locationService, IGoogleSheetsService googleSheetsService, ILogger<ClientService> logger)
        {
            this.careProDbContext = careProDbContext;
            this.cloudinaryService = cloudinaryService;
            this.tokenHandler = tokenHandler;
            this.emailService = emailService;
            this.configuration = configuration;
            this.originValidationService = originValidationService;
            this.locationService = locationService;
            this.googleSheetsService = googleSheetsService;
            this.logger = logger;
        }

        public async Task<ClientDTO> CreateClientUserAsync(AddClientUserRequest addClientUserRequest, string? origin)
        {
            var hashedPassword = BCrypt.Net.BCrypt.HashPassword(addClientUserRequest.Password);

            var clientUserExist = await careProDbContext.Clients.FirstOrDefaultAsync(x => x.Email == addClientUserRequest.Email);

            // var appUserExist = await careProDbContext.AppUsers.FirstOrDefaultAsync(x => x.Email == addClientUserRequest.Email);

            if (clientUserExist != null)
            {
                throw new InvalidOperationException("User already exist, Kindly Login or use a different email!");
            }

            /// CONVERT DTO TO DOMAIN OBJECT            
            var clientUser = new Client
            {
                FirstName = addClientUserRequest.FirstName ?? string.Empty,
                MiddleName = addClientUserRequest.MiddleName,
                LastName = addClientUserRequest.LastName ?? string.Empty,
                Email = addClientUserRequest.Email?.ToLower() ?? throw new ArgumentException("Email is required"),
                Password = hashedPassword,
                HomeAddress = addClientUserRequest.HomeAddress,

                // Assign new ID
                Id = ObjectId.GenerateNewId(),
                Role = Roles.Client.ToString(),
                Status = true,
                IsDeleted = false,
                CreatedAt = DateTime.Now,
            };

            await careProDbContext.Clients.AddAsync(clientUser);

            var careProAppUser = new AppUser
            {

                Email = addClientUserRequest.Email.ToLower(),
                Password = hashedPassword,
                FirstName = addClientUserRequest.FirstName,
                LastName = addClientUserRequest.LastName,

                // Assign new ID
                Id = ObjectId.GenerateNewId(),
                AppUserId = clientUser.Id,
                Role = Roles.Client.ToString(),
                EmailConfirmed = false,
                IsDeleted = false,
                CreatedAt = clientUser.CreatedAt,
            };

            await careProDbContext.AppUsers.AddAsync(careProAppUser);

            await careProDbContext.SaveChangesAsync();

            #region GoogleSheetsLogging
            
            // Log signup to Google Sheets (production only, non-blocking)
            try
            {
                await googleSheetsService.AppendSignupDataAsync(
                    clientUser.FirstName,
                    clientUser.LastName,
                    clientUser.PhoneNo ?? "N/A",
                    clientUser.Email,
                    "Client"
                );
            }
            catch (Exception ex)
            {
                // Log but don't fail signup if Google Sheets logging fails
                logger.LogWarning(ex, "Google Sheets logging failed for client signup: {Email}", clientUser.Email);
            }

            #endregion GoogleSheetsLogging

            #region EmailVerificationHandling

            // Check if this is a development environment or localhost origin
            var isDevelopment = configuration.GetValue<bool>("Development:AutoConfirmEmail", false) ||
                               origin?.Contains("localhost") == true ||
                               origin?.Contains("127.0.0.1") == true;

            if (isDevelopment)
            {
                // Auto-confirm email for development/localhost
                careProAppUser.EmailConfirmed = true;
                careProDbContext.AppUsers.Update(careProAppUser);
                await careProDbContext.SaveChangesAsync();
            }
            else
            {
                // Production/staging: Send verification email
                try
                {
                    var jwtSecretKey = configuration["JwtSettings:Secret"];
                    var token = tokenHandler.GenerateEmailVerificationToken(
                        careProAppUser.AppUserId.ToString(),
                        careProAppUser.Email,
                        jwtSecretKey ?? throw new InvalidOperationException("JWT Secret Key is not configured")
                    );

                    string verificationLink;
                    verificationLink = IsFrontendOrigin(origin ?? string.Empty)
                        ? $"{origin}/confirm-email?token={HttpUtility.UrlEncode(token)}"
                        : $"{origin}/api/Clients/confirm-email?token={HttpUtility.UrlEncode(token)}";

                    await emailService.SendSignUpVerificationEmailAsync(
                        careProAppUser.Email,
                        verificationLink,
                        careProAppUser.FirstName ?? "User"
                    );
                }
                catch (Exception emailEx)
                {
                    // Log email error but don't fail the registration
                    logger.LogError(emailEx, "Failed to send signup verification email to {Email}. Registration completed but user will need to use resend confirmation. Origin: {Origin}, User: {UserId}", 
                        careProAppUser.Email, origin, careProAppUser.AppUserId);
                }
            }

            #endregion EmailVerificationHandling

            #region CreateDefaultLocation

            // Auto-create default location for new client
            try
            {
                var defaultLocationRequest = new SetLocationRequest
                {
                    UserId = clientUser.Id.ToString(),
                    UserType = "Client",
                    Address = "Adeola Odeku Street, Victoria Island, Lagos, Nigeria"
                };

                await locationService.SetUserLocationAsync(defaultLocationRequest);
            }
            catch (Exception locationEx)
            {
                // Log location creation error but don't fail the registration
                logger.LogWarning(locationEx, "Default location creation failed for client: {ClientId}", clientUser.Id);
            }

            #endregion CreateDefaultLocation



            var clientUserDTO = new ClientDTO()
            {
                Id = clientUser.Id.ToString(),
                FirstName = clientUser.FirstName,
                LastName = clientUser.LastName,
                MiddleName = clientUser.MiddleName,
                Email = clientUser.Email,
                HomeAddress = clientUser.HomeAddress,
                Role = clientUser.Role,
                CreatedAt = clientUser.CreatedAt,
            };

            return clientUserDTO;
        }


        // Detect if request is coming from frontend or not
        private bool IsFrontendOrigin(string origin)
        {
            return originValidationService.IsFrontendOrigin(origin);
        }


        public async Task<string> ConfirmEmailAsync(string token)
        {
            var jwtSecret = configuration["JwtSettings:Secret"];
            if (string.IsNullOrWhiteSpace(jwtSecret))
                throw new Exception("JWT Secret not configured.");

            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(jwtSecret);

            try
            {
                var principal = tokenHandler.ValidateToken(token, new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidIssuer = configuration["JwtSettings:Issuer"],
                    ValidAudience = configuration["JwtSettings:Audience"],
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ClockSkew = TimeSpan.Zero
                }, out SecurityToken validatedToken);

                var userId = principal.FindFirst("userId")?.Value;

                if (string.IsNullOrEmpty(userId))
                    return "Invalid token. User ID not found.";

                var objectId = ObjectId.Parse(userId);
                var user = await careProDbContext.AppUsers.FirstOrDefaultAsync(u => u.AppUserId == objectId);

                if (user == null)
                    return "User not found.";

                if (user.EmailConfirmed)
                    return "Email already confirmed.";

                user.EmailConfirmed = true;
                await careProDbContext.SaveChangesAsync();

                return $"Account confirmed for {user.Email}. You can now log in.";
            }
            catch (SecurityTokenException)
            {
                return "Token is invalid or expired.";
            }
            catch (Exception ex)
            {
                return $"An error occurred: {ex.Message}";
            }
        }


        /// Validate the Email (token) from front end
        public async Task<(bool IsValid, string? UserId, string? Email, string? ErrorMessage)> ValidateEmailTokenAsync(string token)
        {
            var jwtSecret = configuration["JwtSettings:Secret"];
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(jwtSecret ?? throw new InvalidOperationException("JWT Secret Key is not configured"));

            try
            {
                var principal = tokenHandler.ValidateToken(token, new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidIssuer = configuration["JwtSettings:Issuer"],
                    ValidAudience = configuration["JwtSettings:Audience"],
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ClockSkew = TimeSpan.Zero
                }, out SecurityToken validatedToken);

                var userId = principal.FindFirst("userId")?.Value;
                var email = principal.FindFirst(ClaimTypes.Email)?.Value;

                if (string.IsNullOrEmpty(userId))
                    return (false, null, null, "Invalid token. User ID missing.");

                return (true, userId, email, null);
            }
            catch (SecurityTokenException)
            {
                return (false, null, null, "Token is invalid or expired.");
            }
            catch (Exception ex)
            {
                return (false, null, null, $"An error occurred: {ex.Message}");
            }
        }

        /// Confirm Email from front end
        public async Task<string> ConfirmEmailFromFrontendAsync(string userId)
        {
            var objectId = ObjectId.Parse(userId);
            var user = await careProDbContext.AppUsers.FirstOrDefaultAsync(u => u.AppUserId == objectId);

            if (user == null)
                return "User not found.";

            if (user.EmailConfirmed)
                return "Email already confirmed.";

            user.EmailConfirmed = true;
            await careProDbContext.SaveChangesAsync();

            return $"Email confirmed successfully for {user.Email}.";
        }




        public async Task<string> ResendEmailConfirmationAsync(string email, string? origin)
        {
            var user = await careProDbContext.AppUsers.FirstOrDefaultAsync(x => x.Email.ToLower() == email.ToLower());

            if (user == null)
                throw new Exception("User does not exist");

            if (user.EmailConfirmed)
                return "Email already confirmed";

            // Check if this is a development environment or localhost origin
            var isDevelopment = configuration.GetValue<bool>("Development:AutoConfirmEmail", false) ||
                               origin?.Contains("localhost") == true ||
                               origin?.Contains("127.0.0.1") == true;

            if (isDevelopment)
            {
                // Auto-confirm email for development/localhost
                user.EmailConfirmed = true;
                careProDbContext.AppUsers.Update(user);
                await careProDbContext.SaveChangesAsync();
                return "Email confirmed automatically in development environment.";
            }

            #region SendVerificationEmail

            try
            {
                var jwtSecretKey = configuration["JwtSettings:Secret"];
                var token = tokenHandler.GenerateEmailVerificationToken(
                    user.AppUserId.ToString(),
                    user.Email,
                    jwtSecretKey ?? throw new InvalidOperationException("JWT Secret Key is not configured")
                );

                string verificationLink;
                verificationLink = IsFrontendOrigin(origin ?? string.Empty)
                    ? $"{origin}/confirm-email?token={HttpUtility.UrlEncode(token)}"
                    : $"{origin}/api/Clients/confirm-email?token={HttpUtility.UrlEncode(token)}";

                await emailService.SendSignUpVerificationEmailAsync(
                    user.Email,
                    verificationLink,
                    user.FirstName + " " + user.LastName
                );

                return "A new confirmation link has been sent to your email.";
            }
            catch (Exception emailEx)
            {
                // Log email error but don't fail completely
                logger.LogError(emailEx, "Failed to resend confirmation email to {Email}. Origin: {Origin}", user.Email, origin);
                return "Failed to send confirmation email. Please try again later.";
            }

            #endregion
        }





        public async Task<ClientResponse> GetClientUserAsync(string clientId)
        {
            var client = await careProDbContext.Clients.FirstOrDefaultAsync(x => x.Id.ToString() == clientId);

            if (client == null)
            {
                throw new KeyNotFoundException($"Client with ID '{clientId}' not found.");
            }

            var clientDTO = new ClientResponse()
            {
                Id = client.Id.ToString(),
                FirstName = client.FirstName,
                MiddleName = client.MiddleName,
                LastName = client.LastName,
                Email = client.Email,
                PhoneNo = client.PhoneNo,
                Role = client.Role,
                Status = client.Status,
                HomeAddress = client.HomeAddress,
                ProfileImage = client.ProfileImage,

                CreatedAt = client.CreatedAt,
            };

            return clientDTO;
        }


        public async Task<IEnumerable<ClientResponse>> GetAllClientUserAsync()
        {
            var clientUsers = await careProDbContext.Clients
                .Where(x => x.Status == true && x.IsDeleted == false)
                .OrderBy(x => x.FirstName)
                .ToListAsync();

            var clientUsersDTOs = new List<ClientResponse>();

            foreach (var clientUser in clientUsers)
            {
                var clientUserDTO = new ClientResponse()
                {
                    Id = clientUser.Id.ToString(),
                    FirstName = clientUser.FirstName,
                    MiddleName = clientUser.MiddleName,
                    LastName = clientUser.LastName,
                    Email = clientUser.Email,
                    PhoneNo = clientUser.PhoneNo,
                    Role = clientUser.Role,
                    IsDeleted = clientUser.IsDeleted,
                    Status = clientUser.Status,
                    HomeAddress = clientUser.HomeAddress,
                    ProfileImage = clientUser.ProfileImage,
                    CreatedAt = clientUser.CreatedAt,
                };
                clientUsersDTOs.Add(clientUserDTO);
            }

            return clientUsersDTOs;
        }

        public async Task<string> UpdateClientUserAsync(string clientId, UpdateClientUserRequest updateClientUserRequest)
        {
            if (!ObjectId.TryParse(clientId, out var objectId))
            {
                throw new ArgumentException("Invalid Client ID format.");
            }

            var existingClient = await careProDbContext.Clients.FindAsync(objectId);
            if (existingClient == null)
            {
                throw new KeyNotFoundException($"Client with ID '{clientId}' not found.");
            }

            // Only update fields if new values are provided and not null/empty
            if (!string.IsNullOrWhiteSpace(updateClientUserRequest.FirstName))
            {
                existingClient.FirstName = updateClientUserRequest.FirstName;
            }

            if (!string.IsNullOrWhiteSpace(updateClientUserRequest.MiddleName))
            {
                existingClient.MiddleName = updateClientUserRequest.MiddleName;
            }

            if (!string.IsNullOrWhiteSpace(updateClientUserRequest.LastName))
            {
                existingClient.LastName = updateClientUserRequest.LastName;
            }

            if (!string.IsNullOrWhiteSpace(updateClientUserRequest.HomeAddress))
            {
                existingClient.HomeAddress = updateClientUserRequest.HomeAddress;
            }

            if (!string.IsNullOrWhiteSpace(updateClientUserRequest.PhoneNo))
            {
                existingClient.PhoneNo = updateClientUserRequest.PhoneNo;
            }

            careProDbContext.Clients.Update(existingClient);
            await careProDbContext.SaveChangesAsync();

            return $"Client with ID '{clientId}' profile updated successfully.";
        }


        public async Task<string> UpdateProfilePictureAsync(string clientId, UpdateProfilePictureRequest updateProfilePictureRequest)
        {

            if (!ObjectId.TryParse(clientId, out var objectId))
            {
                throw new ArgumentException("Invalid Caregiver ID format.");
            }


            var existingCareGiver = await careProDbContext.Clients.FindAsync(objectId);

            if (existingCareGiver == null)
            {
                throw new KeyNotFoundException($"Client with ID '{clientId}' not found.");
            }


            if (updateProfilePictureRequest.ProfileImage != null)
            {
                using var memoryStream = new MemoryStream();
                await updateProfilePictureRequest.ProfileImage.CopyToAsync(memoryStream);
                var imageBytes = memoryStream.ToArray();

                // Now upload imageBytes to Cloudinary
                var imageURL = await cloudinaryService.UploadImageAsync(imageBytes, $"profile_{existingCareGiver.FirstName}{existingCareGiver.LastName}");

                existingCareGiver.ProfileImage = imageURL; // Save Cloudinary URL to DB
            }


            careProDbContext.Clients.Update(existingCareGiver);
            await careProDbContext.SaveChangesAsync();

            return $"Client with ID '{clientId}' ProfilePicture Updated successfully.";

        }




        public async Task<string> SoftDeleteClientAsync(string clientId)
        {
            if (!ObjectId.TryParse(clientId, out var objectId))
            {
                throw new ArgumentException("Invalid Client ID format.");
            }

            var client = await careProDbContext.Clients.FindAsync(objectId);
            if (client == null)
            {
                throw new KeyNotFoundException($"Client with ID '{clientId}' not found.");
            }

            client.IsDeleted = true;
            client.DeletedOn = DateTime.UtcNow;

            careProDbContext.Clients.Update(client);
            await careProDbContext.SaveChangesAsync();

            return $"Client with ID '{clientId}' Soft deleted successfully.";
        }

        public async Task ChangePasswordAsync(ResetPasswordRequest resetPasswordRequest)
        {
            if (string.IsNullOrEmpty(resetPasswordRequest.Email))
                throw new ArgumentException("Email is required.");

            var user = await careProDbContext.AppUsers.FirstOrDefaultAsync(u => u.Email == resetPasswordRequest.Email.ToLower());

            if (user == null)
                throw new InvalidOperationException("User not found.");

            if (!BCrypt.Net.BCrypt.Verify(resetPasswordRequest.CurrentPassword, user.Password))
                throw new UnauthorizedAccessException("Current password is incorrect.");

            user.Password = BCrypt.Net.BCrypt.HashPassword(resetPasswordRequest.NewPassword);


            var client = await careProDbContext.Clients.FirstOrDefaultAsync(c => c.Email == resetPasswordRequest.Email.ToLower());
            if (client != null)
            {
                client.Password = user.Password;
            }

            await careProDbContext.SaveChangesAsync();
        }

        public async Task GeneratePasswordResetTokenAsync(PasswordResetRequestDto passwordResetRequestDto, string? origin)
        {
            if (string.IsNullOrEmpty(passwordResetRequestDto.Email))
                throw new ArgumentException("Email is required.");

            var user = await careProDbContext.AppUsers.FirstOrDefaultAsync(u => u.Email == passwordResetRequestDto.Email.ToLower());

            if (user == null)
                throw new InvalidOperationException("User not found, kindly enter a registered email.");

            var token = tokenHandler.GeneratePasswordResetToken(passwordResetRequestDto.Email);

            // Build the full reset link (example frontend route)
            string resetLink;
            resetLink = IsFrontendOrigin(origin ?? string.Empty)
                ? $"{origin}/forgot-password?token={HttpUtility.UrlEncode(token)}"
                : $"{origin}/api/Clients/resetPassword?token={HttpUtility.UrlEncode(token)}";

            // Check if we're in development mode
            var environment = configuration["ASPNETCORE_ENVIRONMENT"];
            var isDevelopment = environment?.Equals("Development", StringComparison.OrdinalIgnoreCase) == true;

            try
            {
                // Validate email service is available before attempting to send
                if (emailService == null)
                {
                    throw new InvalidOperationException("Email service is not configured");
                }

                // In development mode, optionally skip email sending or use alternative approach
                if (isDevelopment)
                {
                    // Check if email is properly configured for development
                    var smtpServer = configuration["MailSettings:SmtpServer"];
                    var fromEmail = configuration["MailSettings:FromEmail"];
                    
                    if (string.IsNullOrEmpty(smtpServer) || string.IsNullOrEmpty(fromEmail) || 
                        smtpServer == "localhost" || fromEmail.Contains("localhost"))
                    {
                        // Log instead of sending email in development
                        Console.WriteLine("=== DEVELOPMENT MODE - CLIENT PASSWORD RESET EMAIL ===");
                        Console.WriteLine($"To: {passwordResetRequestDto.Email}");
                        Console.WriteLine($"Reset Link: {resetLink}");
                        Console.WriteLine($"User: {user.FirstName ?? "User"}");
                        Console.WriteLine("=====================================================");
                        return; // Exit without sending email
                    }
                }

                // Attempt to send email with timeout and error handling
                await emailService.SendPasswordResetEmailAsync(passwordResetRequestDto.Email, resetLink, user.FirstName ?? "User");
            }
            catch (Exception emailEx)
            {
                // Log email error but don't fail the entire process
                Console.WriteLine($"Email sending failed: {emailEx.Message}");
                
                if (isDevelopment)
                {
                    // In development, log the reset link for testing
                    Console.WriteLine("=== EMAIL FAILED - DEVELOPMENT FALLBACK ===");
                    Console.WriteLine($"Client password reset link for {passwordResetRequestDto.Email}:");
                    Console.WriteLine($"{resetLink}");
                    Console.WriteLine("==========================================");
                    return; // Continue without failing
                }
                
                // In production, re-throw to handle at controller level
                throw new InvalidOperationException("Unable to send reset email. Please try again later.", emailEx);
            }
        }

        public async Task ResetPasswordWithJwtAsync(PasswordResetDto request)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(configuration["JwtSettings:Secret"] ?? throw new InvalidOperationException("JWT Secret Key is not configured"));

            try
            {
                var claimsPrincipal = tokenHandler.ValidateToken(request.Token, new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidIssuer = configuration["JwtSettings:Issuer"],
                    ValidAudience = configuration["JwtSettings:Audience"],
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ClockSkew = TimeSpan.Zero // no extra time
                }, out _);

                //var jwtToken = (JwtSecurityToken)validatedToken;
                //var email = jwtToken.Claims.First(x => x.Type == JwtRegisteredClaimNames.Sub).Value;
                var email = claimsPrincipal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

                var user = await careProDbContext.AppUsers.FirstOrDefaultAsync(u => u.Email == email);
                if (user == null)
                    throw new InvalidOperationException("User not found.");

                var hashedPassword = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
                user.Password = hashedPassword;

                var client = await careProDbContext.Clients.FirstOrDefaultAsync(c => c.Email == user.Email);
                if (client != null)
                {
                    client.Password = hashedPassword;
                }

                await careProDbContext.SaveChangesAsync();
            }
            catch (Exception)
            {
                throw new UnauthorizedAccessException("Invalid or expired reset token.");
            }
        }

        public async Task<LocationDTO> UpdateClientLocationAsync(string clientId, UpdateCaregiverLocationRequest request)
        {
            if (!ObjectId.TryParse(clientId, out var objectId))
            {
                throw new ArgumentException("Invalid Client ID format.");
            }

            var existingClient = await careProDbContext.Clients.FindAsync(objectId);
            if (existingClient == null)
            {
                throw new KeyNotFoundException($"Client with ID '{clientId}' not found.");
            }

            if (string.IsNullOrWhiteSpace(request.Address))
            {
                throw new ArgumentException("Address is required for location update.");
            }

            // Use the location service to update the client's location
            var updateLocationRequest = new UpdateUserLocationRequest
            {
                UserId = clientId,
                UserType = "Client",
                Address = request.Address
            };

            var locationResult = await locationService.UpdateUserLocationAsync(updateLocationRequest);

            // The location service automatically updates the client entity's location fields
            // through its UpdateUserEntityLocation method, so we don't need to manually update here

            return locationResult;
        }
    }
}
