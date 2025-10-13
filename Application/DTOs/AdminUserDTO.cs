using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.DTOs
{
    public class AdminUserDTO
    {
        public string Id { get; set; }

        public string FirstName { get; set; }

        public string? MiddleName { get; set; }

        public string LastName { get; set; }

        public string Email { get; set; }

        public string? PhoneNo { get; set; }

        public string Password { get; set; }

        public string Role { get; set; }

        public string? Status { get; set; }

        public bool IsDeleted { get; set; }
        public DateTime? DeletedOn { get; set; }

        public DateTime CreatedAt { get; set; }

    }


    public class AddAdminUserRequest
    {
        public string FirstName { get; set; }

        public string? MiddleName { get; set; }

        public string LastName { get; set; }

        public string Email { get; set; }

        public string? PhoneNo { get; set; }

        public string Password { get; set; }

        public string Role { get; set; }

        public string? Status { get; set; }

    }


    public class AdminUserResponse
    {
        public string Id { get; set; }

        public string FirstName { get; set; }

        public string? MiddleName { get; set; }

        public string LastName { get; set; }

        public string Email { get; set; }

        public string? PhoneNo { get; set; }

        public string Password { get; set; }

        public string Role { get; set; }

        public string? Status { get; set; }

        public bool IsDeleted { get; set; }
        public DateTime? DeletedOn { get; set; }

        public DateTime CreatedAt { get; set; }

    }

}
