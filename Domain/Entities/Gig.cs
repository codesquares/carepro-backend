using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Entities
{
    public class Gig
    {
        public ObjectId Id { get; set; }
        public string Title { get; set; }
        public string Category { get; set; }
        public string SubCategory { get; set; }
        public string Tags { get; set; }
        public string PackageType { get; set; }
        public string PackageName { get; set; }
        public List<string> PackageDetails { get; set; }
        public string DeliveryTime { get; set; }
        public int Price { get; set; }
        public string? Image1 { get; set; }

        
        public string Status { get; set; }             

        public string CaregiverId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedOn { get; set; }
        public bool? IsUpdatedToPause { get; set; }
    }
}
