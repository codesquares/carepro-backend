using Microsoft.AspNet.Identity.EntityFramework;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Entities
{
    public class AppUser /*: IdentityUser*/
    {
        public ObjectId Id { get; set; }    

        public ObjectId AppUserId { get; set; }    
        
        public string Email { get; set; } = null!;
        public string? FirstName { get; set; } = null!;
        public string? LastName { get; set; } = null!;
         
        public string Role { get; set; }

        public string Password { get; set; } = null!;

        public bool IsDeleted { get; set; }

        public DateTime CreatedAt { get; set; }

        public bool? IsOnline { get; set; }
        public bool EmailConfirmed { get; set; }
        public string? ConnectionId { get; set; }
        public List<string>? DeviceIp { get; set; }
    }
}
