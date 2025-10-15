using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Entities
{
    public class Location
    {
        public ObjectId Id { get; set; }

        public string UserId { get; set; } = null!;

        public string UserType { get; set; } = null!; // "Client" or "Caregiver"

        public string Address { get; set; } = null!;

        public string City { get; set; } = null!;

        public string? State { get; set; }

        public string? Country { get; set; }

        public double Latitude { get; set; }

        public double Longitude { get; set; }

        public bool IsActive { get; set; }

        public bool IsDeleted { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime? UpdatedAt { get; set; }
    }
}