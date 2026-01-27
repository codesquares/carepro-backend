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
    public class ClientDTO
    {
        public string? Id { get; set; }

        public string? FirstName { get; set; }

        public string? MiddleName { get; set; }

        public string? LastName { get; set; }

        public string? Email { get; set; }

        public string? Role { get; set; }

        public string? Password { get; set; }

        public string? HomeAddress { get; set; }

        public bool IsDeleted { get; set; }

        public bool Status { get; set; }

        public DateTime CreatedAt { get; set; }
    }

    public class AddClientUserRequest
    {
        [Required(ErrorMessage = "First name is required")]
        [StringLength(50, MinimumLength = 2, ErrorMessage = "First name must be between 2 and 50 characters")]
        public string? FirstName { get; set; }

        [StringLength(50, ErrorMessage = "Middle name cannot exceed 50 characters")]
        public string? MiddleName { get; set; }

        [Required(ErrorMessage = "Last name is required")]
        [StringLength(50, MinimumLength = 2, ErrorMessage = "Last name must be between 2 and 50 characters")]
        public string? LastName { get; set; }

        [Required(ErrorMessage = "Email is required")]
        [EmailAddress(ErrorMessage = "Invalid email format")]
        [StringLength(256, ErrorMessage = "Email cannot exceed 256 characters")]
        public string? Email { get; set; }

        [Required(ErrorMessage = "Password is required")]
        [StringLength(128, MinimumLength = 8, ErrorMessage = "Password must be between 8 and 128 characters")]
        [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).{8,}$", 
            ErrorMessage = "Password must contain at least one uppercase letter, one lowercase letter, and one number")]
        public string? Password { get; set; }

        [StringLength(500, ErrorMessage = "Home address cannot exceed 500 characters")]
        public string? HomeAddress { get; set; }

    }

    public class ClientResponse
    {
        public string? Id { get; set; }

        public string? FirstName { get; set; }

        public string? MiddleName { get; set; }

        public string? LastName { get; set; }

        public string? Email { get; set; }

        public string? Role { get; set; }

        public string? HomeAddress { get; set; }
        public string? ProfileImage { get; set; }

        public string? PhoneNo { get; set; }
        public bool IsDeleted { get; set; }


        public bool Status { get; set; }

        public DateTime CreatedAt { get; set; }
    }




    public class UpdateClientUserRequest
    {
        public string? FirstName { get; set; }

        public string? MiddleName { get; set; }

        public string? LastName { get; set; }

        // public string Email { get; set; }

        public string? HomeAddress { get; set; }

        //public string Location { get; set; }

        public string? PhoneNo { get; set; }

    }
}
