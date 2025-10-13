using Microsoft.AspNetCore.Http;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.DTOs
{
    public class ClientDTO
    {
        public string Id { get; set; }

        public string FirstName { get; set; }

        public string? MiddleName { get; set; }

        public string LastName { get; set; }

        public string Email { get; set; }

        public string Role { get; set; }

        public string Password { get; set; }

        public string HomeAddress { get; set; }

        public bool IsDeleted { get; set; }

        public bool Status { get; set; }

        public DateTime CreatedAt { get; set; }
    }

    public class AddClientUserRequest
    {        
        public string FirstName { get; set; }

        public string? MiddleName { get; set; }

        public string LastName { get; set; }

        public string Email { get; set; }

        public string Password { get; set; }

        public string? HomeAddress { get; set; }
                
    }

    public class ClientResponse
    {
        public string Id { get; set; }

        public string FirstName { get; set; }

        public string? MiddleName { get; set; }

        public string LastName { get; set; }

        public string Email { get; set; }

        public string Role { get; set; }

        public string HomeAddress { get; set; }
        public string ProfileImage { get; set; }

        public string PhoneNo { get; set; }
        public bool IsDeleted { get; set; }


        public bool Status { get; set; }

        public DateTime CreatedAt { get; set; }
    }


    

    public class UpdateClientUserRequest
    {
        public string FirstName { get; set; }

        public string? MiddleName { get; set; }

        public string LastName { get; set; }

       // public string Email { get; set; }
               
        public string HomeAddress { get; set; }

        //public string Location { get; set; }

        public string PhoneNo { get; set; }
                
    }
}
