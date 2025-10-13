using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.DTOs
{
    public class AppUserDTO
    {       
        public string AppUserId { get; set; }

        public string Email { get; set; }

        public string Password { get; set; }

        public string FirstName { get; set; }

        public string? MiddleName { get; set; }

        public string LastName { get; set; }

        public string? PhoneNo { get; set; }

        public string Token { get; set; }

        public string Role { get; set; }

        public string? Message { get; set; }

        public bool IsDeleted { get; set; }

        public DateTime CreatedAt { get; set; }

        public string HomeAddress { get; set; }
    }


    public class AppUserResponse
    {
        public string AppUserId { get; set; }

        public string Email { get; set; }


        public string FullName { get; set; }

        //public string FirstName { get; set; }

        //public string? MiddleName { get; set; }

        //public string LastName { get; set; }

        //public string? PhoneNo { get; set; }

       
        public string Role { get; set; }

        // public string Password { get; set; }

        // public string Token { get; set; }

        // public string? Message { get; set; }

        //public bool IsDeleted { get; set; }

        //public DateTime AssessedDate { get; set; }

        //public string HomeAddress { get; set; }
    }


    public class ChatPreviewResponse
    {
        public string FullName { get; set; }
        public string AppUserId { get; set; }
        public string Email { get; set; }
        public string Role { get; set; }

        public string LastMessage { get; set; }
        public DateTime LastMessageTimestamp { get; set; }
    }
}
