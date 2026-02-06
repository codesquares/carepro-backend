using System.ComponentModel.DataAnnotations;

namespace Application.DTOs.Authentication
{
    /// <summary>
    /// Request DTO for Google Sign In - user already has an account
    /// </summary>
    public class GoogleSignInRequest
    {
        [Required(ErrorMessage = "Google ID token is required")]
        public string IdToken { get; set; } = string.Empty;
    }

    /// <summary>
    /// Request DTO for Google Sign Up - creating new account
    /// Role is selected on frontend before calling this endpoint
    /// </summary>
    public class GoogleSignUpRequest
    {
        [Required(ErrorMessage = "Google ID token is required")]
        public string IdToken { get; set; } = string.Empty;
        
        // Optional: Additional info not in Google profile
        public string? PhoneNo { get; set; }
        public string? HomeAddress { get; set; }
    }

    /// <summary>
    /// Request DTO for linking Google account to existing local account
    /// User must be authenticated (logged in with password)
    /// </summary>
    public class LinkGoogleAccountRequest
    {
        [Required(ErrorMessage = "Google ID token is required")]
        public string IdToken { get; set; } = string.Empty;
        
        [Required(ErrorMessage = "User ID is required")]
        public string UserId { get; set; } = string.Empty;
    }

    /// <summary>
    /// Parsed user info from Google ID token
    /// </summary>
    public class GoogleUserInfo
    {
        public string GoogleId { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public bool EmailVerified { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? FullName { get; set; }
        public string? ProfilePicture { get; set; }
        public string? Locale { get; set; }
    }

    /// <summary>
    /// Response when existing account is found with same email during Google sign up
    /// Frontend should prompt user to link accounts
    /// </summary>
    public class GoogleAuthConflictResponse
    {
        public bool AccountExists { get; set; } = true;
        public string Message { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string ExistingAuthProvider { get; set; } = string.Empty; // "local", "google", "both"
        public string ExistingRole { get; set; } = string.Empty; // "Client", "Caregiver"
        public bool CanLinkAccounts { get; set; }
    }

    /// <summary>
    /// Response after successful Google authentication
    /// Same structure as LoginResponse for consistency
    /// </summary>
    public class GoogleAuthResponse
    {
        public string Id { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string? MiddleName { get; set; }
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string Token { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public string? ProfilePicture { get; set; }
        public string AuthProvider { get; set; } = string.Empty;
        public bool IsNewUser { get; set; }
        public bool IsFirstLogin { get; set; }
    }
}
