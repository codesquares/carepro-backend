using Microsoft.AspNetCore.Http;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.DTOs
{
    public class CaregiverDTO
    {
        public string Id { get; set; }

        public string FirstName { get; set; } = null!;

        public string? MiddleName { get; set; }

        public string LastName { get; set; } = null!;

        public string Email { get; set; } = null!;

        public string? PhoneNo { get; set; }

        public string Role { get; set; }

        public string Password { get; set; } = null!;

        public bool IsDeleted { get; set; }

        public DateTime CreatedAt { get; set; }

        public bool Status { get; set; }
        public byte[]? ProfileImage { get; set; }


        public string? HomeAddress { get; set; }


        public string? Introduction { get; set; }
        public string? Description { get; set; }
        public string[]? Services { get; set; }
        public string? Location { get; set; }
        public string[]? CertificationIDs { get; set; }
        public string? ReasonForDeactivation { get; set; }
        public string? IntroVideoUrl { get; set; }

    }

    public class AddCaregiverRequest
    {
        [Required(ErrorMessage = "First name is required")]
        [StringLength(50, MinimumLength = 2, ErrorMessage = "First name must be between 2 and 50 characters")]
        public string FirstName { get; set; } = null!;

        [StringLength(50, ErrorMessage = "Middle name cannot exceed 50 characters")]
        public string? MiddleName { get; set; }

        [Required(ErrorMessage = "Last name is required")]
        [StringLength(50, MinimumLength = 2, ErrorMessage = "Last name must be between 2 and 50 characters")]
        public string LastName { get; set; } = null!;

        [Required(ErrorMessage = "Email is required")]
        [EmailAddress(ErrorMessage = "Invalid email format")]
        [StringLength(256, ErrorMessage = "Email cannot exceed 256 characters")]
        public string Email { get; set; } = null!;

        [Required(ErrorMessage = "Role is required")]
        [RegularExpression("^(Caregiver)$", ErrorMessage = "Role must be 'Caregiver'")]
        public string Role { get; set; } = null!;

        [Phone(ErrorMessage = "Invalid phone number format")]
        [StringLength(20, ErrorMessage = "Phone number cannot exceed 20 characters")]
        public string? PhoneNo { get; set; }

        [Required(ErrorMessage = "Password is required")]
        [StringLength(128, MinimumLength = 8, ErrorMessage = "Password must be between 8 and 128 characters")]
        [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).{8,}$", 
            ErrorMessage = "Password must contain at least one uppercase letter, one lowercase letter, and one number")]
        public string Password { get; set; } = null!;


    }

    public class CaregiverResponse
    {
        public string Id { get; set; }

        public string FirstName { get; set; } = null!;

        public string? MiddleName { get; set; }

        public string LastName { get; set; } = null!;

        public string Email { get; set; } = null!;

        public string? PhoneNo { get; set; } = null!;


        public string Role { get; set; }

        // public string Password { get; set; } = null!;

        public bool IsDeleted { get; set; }


        public bool Status { get; set; }



        public string? HomeAddress { get; set; }

        public string? AboutMe { get; set; }
        public string? AboutMeIntro { get; set; }

        // public string? Introduction { get; set; }
        //public string? AboutMe { get; set; }
        //public string[]? Services { get; set; }
        public string? Location { get; set; }
        //public string[]? CertificationIDs { get; set; }
        public string? ReasonForDeactivation { get; set; }
        //public string? IntroVideo { get; set; }


        public bool IsAvailable { get; set; }

        public string? IntroVideo { get; set; }
        public decimal TotalEarning { get; set; }
        public int NoOfOrders { get; set; }
        public int NoOfHoursSpent { get; set; }

        public List<string> Services { get; set; }

        public string? ProfileImage { get; set; }

        public string? AuthProvider { get; set; }

        public DateTime CreatedAt { get; set; }


    }

    /// <summary>
    /// Public-safe response DTO for caregivers - excludes sensitive PII
    /// Use this for public-facing endpoints (search, browse, etc.)
    /// </summary>
    public class CaregiverPublicResponse
    {
        public string Id { get; set; } = null!;
        public string FirstName { get; set; } = null!;
        public string? MiddleName { get; set; }
        public string LastName { get; set; } = null!;
        // Email, PhoneNo, and HomeAddress intentionally excluded for privacy
        public string Role { get; set; } = null!;
        public bool IsAvailable { get; set; }
        public string? AboutMe { get; set; }
        public string? AboutMeIntro { get; set; }
        public string? Location { get; set; }
        public string? IntroVideo { get; set; }
        public decimal TotalEarning { get; set; }
        public int NoOfOrders { get; set; }
        public int NoOfHoursSpent { get; set; }
        public List<string>? Services { get; set; }
        public string? ProfileImage { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class UpdateCaregiverAdditionalInfoRequest
    {

        public string? AboutMe { get; set; }
        public string? Location { get; set; }
        //public string? IntroVideo { get; set; }
        public IFormFile? IntroVideo { get; set; }
    }

    public class UpdateCaregiverAvailabilityRequest
    {
        public bool IsAvailable { get; set; }
    }

    public class UpdateProfilePictureRequest
    {
        public IFormFile? ProfileImage { get; set; }
    }

    public class ResetPasswordRequest
    {
        public string? Email { get; init; }

        public string? CurrentPassword { get; init; }

        public string? NewPassword { get; init; }
    }

    public class PasswordResetRequestDto
    {
        public string? Email { get; set; }
    }

    public class PasswordResetDto
    {
        public string? Token { get; set; }
        public string? NewPassword { get; set; }
    }

    public class UpdateCaregiverLocationRequest
    {
        [Required]
        public string Address { get; set; } = null!;
    }



}
