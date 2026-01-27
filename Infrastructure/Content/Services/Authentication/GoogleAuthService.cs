using Application.DTOs;
using Application.DTOs.Authentication;
using Application.Interfaces.Authentication;
using Application.Interfaces.Content;
using Domain;
using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using System.Net.Http.Json;
using System.Text.Json;

namespace Infrastructure.Content.Services.Authentication
{
    public class GoogleAuthService : IGoogleAuthService
    {
        private readonly CareProDbContext _context;
        private readonly ITokenHandler _tokenHandler;
        private readonly IConfiguration _configuration;
        private readonly ILogger<GoogleAuthService> _logger;
        private readonly HttpClient _httpClient;
        private readonly ILocationService _locationService;
        private readonly string _googleClientId;

        public GoogleAuthService(
            CareProDbContext context,
            ITokenHandler tokenHandler,
            IConfiguration configuration,
            ILogger<GoogleAuthService> logger,
            IHttpClientFactory httpClientFactory,
            ILocationService locationService)
        {
            _context = context;
            _tokenHandler = tokenHandler;
            _configuration = configuration;
            _logger = logger;
            _httpClient = httpClientFactory.CreateClient();
            _locationService = locationService;
            _googleClientId = _configuration["Google:ClientId"] ?? 
                throw new InvalidOperationException("Google ClientId not configured");
        }

        public async Task<GoogleUserInfo?> ValidateGoogleTokenAsync(string idToken)
        {
            try
            {
                // Validate token with Google's tokeninfo endpoint
                var response = await _httpClient.GetAsync(
                    $"https://oauth2.googleapis.com/tokeninfo?id_token={idToken}");

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Google token validation failed: {StatusCode}", response.StatusCode);
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();
                var tokenInfo = JsonDocument.Parse(json).RootElement;

                // Verify the token is for our app
                var audience = tokenInfo.GetProperty("aud").GetString();
                if (audience != _googleClientId)
                {
                    _logger.LogWarning("Google token audience mismatch. Expected: {Expected}, Got: {Got}", 
                        _googleClientId, audience);
                    return null;
                }

                // Extract user info
                var userInfo = new GoogleUserInfo
                {
                    GoogleId = tokenInfo.GetProperty("sub").GetString() ?? string.Empty,
                    Email = tokenInfo.GetProperty("email").GetString() ?? string.Empty,
                    EmailVerified = tokenInfo.TryGetProperty("email_verified", out var ev) && 
                                   ev.GetString()?.ToLower() == "true",
                    FirstName = tokenInfo.TryGetProperty("given_name", out var gn) ? gn.GetString() : null,
                    LastName = tokenInfo.TryGetProperty("family_name", out var fn) ? fn.GetString() : null,
                    FullName = tokenInfo.TryGetProperty("name", out var name) ? name.GetString() : null,
                    ProfilePicture = tokenInfo.TryGetProperty("picture", out var pic) ? pic.GetString() : null,
                    Locale = tokenInfo.TryGetProperty("locale", out var loc) ? loc.GetString() : null
                };

                _logger.LogInformation("Google token validated for email: {Email}", userInfo.Email);
                return userInfo;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating Google token");
                return null;
            }
        }

        public async Task<(GoogleAuthResponse? Response, GoogleAuthConflictResponse? Conflict)> GoogleSignInAsync(
            GoogleSignInRequest request)
        {
            try
            {
                // Validate the Google token
                var googleUser = await ValidateGoogleTokenAsync(request.IdToken);
                if (googleUser == null)
                {
                    throw new UnauthorizedAccessException("Invalid Google token");
                }

                // First, try to find by GoogleId (exclude soft-deleted users)
                var appUser = await _context.AppUsers
                    .FirstOrDefaultAsync(u => u.GoogleId == googleUser.GoogleId && !u.IsDeleted);

                // If not found by GoogleId, try by email (exclude soft-deleted users)
                if (appUser == null)
                {
                    appUser = await _context.AppUsers
                        .FirstOrDefaultAsync(u => u.Email.ToLower() == googleUser.Email.ToLower() && !u.IsDeleted);
                }

                if (appUser == null)
                {
                    // No account exists - user needs to sign up
                    return (null, new GoogleAuthConflictResponse
                    {
                        AccountExists = false,
                        Message = "No account found with this email. Please sign up first.",
                        Email = googleUser.Email,
                        CanLinkAccounts = false
                    });
                }

                // Account exists - check if it's a local account that needs linking
                // Treat null AuthProvider as "local" for backwards compatibility
                var authProvider = appUser.AuthProvider ?? "local";
                if (appUser.GoogleId == null && authProvider == "local")
                {
                    return (null, new GoogleAuthConflictResponse
                    {
                        AccountExists = true,
                        Message = "An account with this email already exists. Would you like to link your Google account?",
                        Email = googleUser.Email,
                        ExistingAuthProvider = authProvider,
                        ExistingRole = appUser.Role,
                        CanLinkAccounts = true
                    });
                }

                // User has Google linked - proceed with sign in
                return await GenerateAuthResponseAsync(appUser, googleUser, isNewUser: false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during Google sign in");
                throw;
            }
        }

        public async Task<(GoogleAuthResponse? Response, GoogleAuthConflictResponse? Conflict)> GoogleSignUpClientAsync(
            GoogleSignUpRequest request, string? origin)
        {
            return await GoogleSignUpAsync(request, "Client", origin);
        }

        public async Task<(GoogleAuthResponse? Response, GoogleAuthConflictResponse? Conflict)> GoogleSignUpCaregiverAsync(
            GoogleSignUpRequest request, string? origin)
        {
            return await GoogleSignUpAsync(request, "Caregiver", origin);
        }

        private async Task<(GoogleAuthResponse? Response, GoogleAuthConflictResponse? Conflict)> GoogleSignUpAsync(
            GoogleSignUpRequest request, string role, string? origin)
        {
            try
            {
                // Validate the Google token
                var googleUser = await ValidateGoogleTokenAsync(request.IdToken);
                if (googleUser == null)
                {
                    throw new UnauthorizedAccessException("Invalid Google token");
                }

                // Check if GoogleId is already registered (exclude soft-deleted users)
                var existingByGoogleId = await _context.AppUsers
                    .FirstOrDefaultAsync(u => u.GoogleId == googleUser.GoogleId && !u.IsDeleted);
                
                if (existingByGoogleId != null)
                {
                    return (null, new GoogleAuthConflictResponse
                    {
                        AccountExists = true,
                        Message = "This Google account is already registered. Please sign in instead.",
                        Email = googleUser.Email,
                        ExistingAuthProvider = existingByGoogleId.AuthProvider,
                        ExistingRole = existingByGoogleId.Role,
                        CanLinkAccounts = false
                    });
                }

                // Check if email is already registered
                var (exists, existingRole, authProvider) = await CheckEmailExistsAsync(googleUser.Email);
                if (exists)
                {
                    // Account exists with same email - prompt to link
                    return (null, new GoogleAuthConflictResponse
                    {
                        AccountExists = true,
                        Message = $"An account with this email already exists as a {existingRole}. Would you like to link your Google account to it?",
                        Email = googleUser.Email,
                        ExistingAuthProvider = authProvider ?? "local",
                        ExistingRole = existingRole ?? "Unknown",
                        CanLinkAccounts = true
                    });
                }

                // Create new account based on role
                AppUser appUser;
                if (role == "Client")
                {
                    appUser = await CreateClientWithGoogleAsync(googleUser, request);
                }
                else
                {
                    appUser = await CreateCaregiverWithGoogleAsync(googleUser, request);
                }

                // Create default location
                try
                {
                    var defaultLocationRequest = new SetLocationRequest
                    {
                        UserId = appUser.AppUserId.ToString(),
                        UserType = role,
                        Address = "Adeola Odeku Street, Victoria Island, Lagos, Nigeria"
                    };
                    await _locationService.SetUserLocationAsync(defaultLocationRequest);
                }
                catch (Exception locationEx)
                {
                    _logger.LogWarning(locationEx, "Default location creation failed for {Role}: {UserId}", 
                        role, appUser.AppUserId);
                }

                _logger.LogInformation("New {Role} created via Google OAuth: {Email}", role, googleUser.Email);
                
                return await GenerateAuthResponseAsync(appUser, googleUser, isNewUser: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during Google sign up for {Role}", role);
                throw;
            }
        }

        private async Task<AppUser> CreateClientWithGoogleAsync(GoogleUserInfo googleUser, GoogleSignUpRequest request)
        {
            var clientId = ObjectId.GenerateNewId();

            var client = new Client
            {
                Id = clientId,
                FirstName = googleUser.FirstName ?? googleUser.FullName?.Split(' ').FirstOrDefault() ?? "User",
                LastName = googleUser.LastName ?? googleUser.FullName?.Split(' ').LastOrDefault() ?? "",
                Email = googleUser.Email.ToLower(),
                Password = "", // No password for Google accounts
                PhoneNo = request.PhoneNo,
                HomeAddress = request.HomeAddress,
                ProfileImage = googleUser.ProfilePicture, // Use Google's profile picture
                GoogleId = googleUser.GoogleId,
                Role = Roles.Client.ToString(),
                Status = true,
                IsDeleted = false,
                CreatedAt = DateTime.UtcNow
            };

            await _context.Clients.AddAsync(client);

            var appUser = new AppUser
            {
                Id = ObjectId.GenerateNewId(),
                AppUserId = clientId,
                Email = googleUser.Email.ToLower(),
                FirstName = client.FirstName,
                LastName = client.LastName,
                Password = "", // No password for Google accounts
                Role = Roles.Client.ToString(),
                GoogleId = googleUser.GoogleId,
                AuthProvider = "google",
                ProfilePicture = googleUser.ProfilePicture,
                EmailConfirmed = true, // Google already verified the email
                IsDeleted = false,
                CreatedAt = DateTime.UtcNow
            };

            await _context.AppUsers.AddAsync(appUser);
            await _context.SaveChangesAsync();

            return appUser;
        }

        private async Task<AppUser> CreateCaregiverWithGoogleAsync(GoogleUserInfo googleUser, GoogleSignUpRequest request)
        {
            var caregiverId = ObjectId.GenerateNewId();

            var caregiver = new Caregiver
            {
                Id = caregiverId,
                FirstName = googleUser.FirstName ?? googleUser.FullName?.Split(' ').FirstOrDefault() ?? "User",
                LastName = googleUser.LastName ?? googleUser.FullName?.Split(' ').LastOrDefault() ?? "",
                Email = googleUser.Email.ToLower(),
                Password = "", // No password for Google accounts
                PhoneNo = request.PhoneNo ?? "",
                HomeAddress = request.HomeAddress,
                ProfileImage = googleUser.ProfilePicture, // Use Google's profile picture
                GoogleId = googleUser.GoogleId,
                Role = Roles.Caregiver.ToString(),
                Status = true,
                IsAvailable = true,
                IsDeleted = false,
                CreatedAt = DateTime.UtcNow
            };

            await _context.CareGivers.AddAsync(caregiver);

            var appUser = new AppUser
            {
                Id = ObjectId.GenerateNewId(),
                AppUserId = caregiverId,
                Email = googleUser.Email.ToLower(),
                FirstName = caregiver.FirstName,
                LastName = caregiver.LastName,
                Password = "", // No password for Google accounts
                Role = Roles.Caregiver.ToString(),
                GoogleId = googleUser.GoogleId,
                AuthProvider = "google",
                ProfilePicture = googleUser.ProfilePicture,
                EmailConfirmed = true, // Google already verified the email
                IsDeleted = false,
                CreatedAt = DateTime.UtcNow
            };

            await _context.AppUsers.AddAsync(appUser);
            await _context.SaveChangesAsync();

            return appUser;
        }

        public async Task<GoogleAuthResponse> LinkGoogleAccountAsync(LinkGoogleAccountRequest request)
        {
            try
            {
                // Validate the Google token
                var googleUser = await ValidateGoogleTokenAsync(request.IdToken);
                if (googleUser == null)
                {
                    throw new UnauthorizedAccessException("Invalid Google token");
                }

                // Find the user by ID
                var appUser = await _context.AppUsers
                    .FirstOrDefaultAsync(u => u.AppUserId.ToString() == request.UserId);

                if (appUser == null)
                {
                    throw new InvalidOperationException("User not found");
                }

                // Verify emails match
                if (appUser.Email.ToLower() != googleUser.Email.ToLower())
                {
                    throw new InvalidOperationException(
                        "Google account email does not match your account email. " +
                        "Please use the Google account associated with " + appUser.Email);
                }

                // Check if this GoogleId is already linked to another account
                var existingGoogleUser = await _context.AppUsers
                    .FirstOrDefaultAsync(u => u.GoogleId == googleUser.GoogleId);
                
                if (existingGoogleUser != null && existingGoogleUser.Id != appUser.Id)
                {
                    throw new InvalidOperationException(
                        "This Google account is already linked to a different account");
                }

                // Link the Google account
                appUser.GoogleId = googleUser.GoogleId;
                appUser.AuthProvider = "both"; // Now supports both local and Google
                
                // Update profile picture if user doesn't have one
                if (string.IsNullOrEmpty(appUser.ProfilePicture) && !string.IsNullOrEmpty(googleUser.ProfilePicture))
                {
                    appUser.ProfilePicture = googleUser.ProfilePicture;
                }

                // Also update the role-specific entity
                if (appUser.Role == "Client")
                {
                    var client = await _context.Clients.FirstOrDefaultAsync(c => c.Id == appUser.AppUserId);
                    if (client != null)
                    {
                        client.GoogleId = googleUser.GoogleId;
                        if (string.IsNullOrEmpty(client.ProfileImage))
                        {
                            client.ProfileImage = googleUser.ProfilePicture;
                        }
                    }
                }
                else if (appUser.Role == "Caregiver")
                {
                    var caregiver = await _context.CareGivers.FirstOrDefaultAsync(c => c.Id == appUser.AppUserId);
                    if (caregiver != null)
                    {
                        caregiver.GoogleId = googleUser.GoogleId;
                        if (string.IsNullOrEmpty(caregiver.ProfileImage))
                        {
                            caregiver.ProfileImage = googleUser.ProfilePicture;
                        }
                    }
                }

                await _context.SaveChangesAsync();

                _logger.LogInformation("Google account linked for user: {Email}", appUser.Email);

                var (response, _) = await GenerateAuthResponseAsync(appUser, googleUser, isNewUser: false);
                return response!;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error linking Google account for user: {UserId}", request.UserId);
                throw;
            }
        }

        public async Task<bool> IsGoogleIdRegisteredAsync(string googleId)
        {
            return await _context.AppUsers.AnyAsync(u => u.GoogleId == googleId);
        }

        public async Task<(bool Exists, string? Role, string? AuthProvider)> CheckEmailExistsAsync(string email)
        {
            var normalizedEmail = email.ToLower();

            // Exclude soft-deleted users
            var appUser = await _context.AppUsers
                .FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail && !u.IsDeleted);

            if (appUser != null)
            {
                return (true, appUser.Role, appUser.AuthProvider);
            }

            // Also check Clients and Caregivers collections directly (in case of orphaned records)
            // Exclude soft-deleted records
            var clientExists = await _context.Clients
                .AnyAsync(c => c.Email.ToLower() == normalizedEmail && !c.IsDeleted);
            if (clientExists)
            {
                return (true, "Client", "local");
            }

            var caregiverExists = await _context.CareGivers
                .AnyAsync(c => c.Email.ToLower() == normalizedEmail && !c.IsDeleted);
            if (caregiverExists)
            {
                return (true, "Caregiver", "local");
            }

            return (false, null, null);
        }

        private async Task<(GoogleAuthResponse? Response, GoogleAuthConflictResponse? Conflict)> GenerateAuthResponseAsync(
            AppUser appUser, GoogleUserInfo googleUser, bool isNewUser)
        {
            // Get role-specific data
            var careGiverAppUser = await _context.CareGivers.FirstOrDefaultAsync(x => x.Id == appUser.AppUserId);
            var clientAppUser = await _context.Clients.FirstOrDefaultAsync(x => x.Id == appUser.AppUserId);

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

            // Generate JWT Token
            var token = await _tokenHandler.CreateTokenAsync(appUserDetails);

            // Generate Refresh Token
            var refreshToken = _tokenHandler.GenerateRefreshToken();

            // Save refresh token to database
            var refreshTokenExpirationDays = _configuration.GetValue<int>("Jwt:RefreshTokenExpirationDays", 30);
            var refreshTokenEntity = new RefreshToken
            {
                Token = refreshToken,
                UserId = appUserDetails.AppUserId,
                ExpiryDate = DateTime.UtcNow.AddDays(refreshTokenExpirationDays),
                CreatedAt = DateTime.UtcNow
            };

            await _context.RefreshTokens.AddAsync(refreshTokenEntity);
            await _context.SaveChangesAsync();

            // Get profile picture (prefer existing, then Google's)
            var profilePicture = appUser.ProfilePicture ?? 
                                 clientAppUser?.ProfileImage ?? 
                                 careGiverAppUser?.ProfileImage ?? 
                                 googleUser.ProfilePicture;

            return (new GoogleAuthResponse
            {
                Id = appUserDetails.AppUserId,
                FirstName = appUserDetails.FirstName ?? string.Empty,
                MiddleName = appUserDetails.MiddleName,
                LastName = appUserDetails.LastName ?? string.Empty,
                Email = appUserDetails.Email,
                Role = appUserDetails.Role,
                Token = token,
                RefreshToken = refreshToken,
                ProfilePicture = profilePicture,
                AuthProvider = appUser.AuthProvider ?? "local",
                IsNewUser = isNewUser
            }, null);
        }
    }
}
