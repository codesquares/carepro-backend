using Application.DTOs.Authentication;

namespace Application.Interfaces.Authentication
{
    /// <summary>
    /// Service for handling Google OAuth authentication
    /// </summary>
    public interface IGoogleAuthService
    {
        /// <summary>
        /// Validates a Google ID token and extracts user information
        /// </summary>
        /// <param name="idToken">The ID token from Google Sign-In</param>
        /// <returns>Parsed user info or null if invalid</returns>
        Task<GoogleUserInfo?> ValidateGoogleTokenAsync(string idToken);

        /// <summary>
        /// Signs in a user with their Google account
        /// Returns conflict response if account exists but needs linking
        /// </summary>
        Task<(GoogleAuthResponse? Response, GoogleAuthConflictResponse? Conflict)> GoogleSignInAsync(GoogleSignInRequest request);

        /// <summary>
        /// Creates a new Client account using Google authentication
        /// User selected "Client" on role selection screen
        /// </summary>
        Task<(GoogleAuthResponse? Response, GoogleAuthConflictResponse? Conflict)> GoogleSignUpClientAsync(GoogleSignUpRequest request, string? origin);

        /// <summary>
        /// Creates a new Caregiver account using Google authentication
        /// User selected "Caregiver" on role selection screen
        /// </summary>
        Task<(GoogleAuthResponse? Response, GoogleAuthConflictResponse? Conflict)> GoogleSignUpCaregiverAsync(GoogleSignUpRequest request, string? origin);

        /// <summary>
        /// Links a Google account to an existing local (password-based) account
        /// User must be authenticated first
        /// </summary>
        Task<GoogleAuthResponse> LinkGoogleAccountAsync(LinkGoogleAccountRequest request);

        /// <summary>
        /// Checks if a Google ID is already registered
        /// </summary>
        Task<bool> IsGoogleIdRegisteredAsync(string googleId);

        /// <summary>
        /// Checks if an email is already registered (any provider)
        /// </summary>
        Task<(bool Exists, string? Role, string? AuthProvider)> CheckEmailExistsAsync(string email);
    }
}
