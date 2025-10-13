using Microsoft.AspNetCore.Http;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
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

        public string PhoneNo { get; set; }

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

        public string FirstName { get; set; } = null!;

        public string? MiddleName { get; set; }

        public string LastName { get; set; } = null!;

        public string Email { get; set; } = null!;

        public string Role { get; set; } = null!;

        public string PhoneNo { get; set; }

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
        public IFormFile ProfileImage { get; set; }
    }

    public class ResetPasswordRequest
    {       
        public string Email { get; init; }
        
        public string CurrentPassword { get; init; }
        
        public string NewPassword { get; init; }
    }

    public class PasswordResetRequestDto
    {
        public string Email { get; set; }
    }

    public class PasswordResetDto
    {
        public string Token { get; set; }
        public string NewPassword { get; set; }
    }



}
