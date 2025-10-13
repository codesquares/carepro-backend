using Microsoft.AspNetCore.Http;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.DTOs
{
    public class GigDTO
    {
        public string Id { get; set; }

       
        public string Title { get; set; }
        public string Category { get; set; }
        public List<string> SubCategory { get; set; }
        public string Tags { get; set; }
        public string PackageType { get; set; }
        public string PackageName { get; set; }
        public List<string> PackageDetails { get; set; }
        public string DeliveryTime { get; set; }
        public int Price { get; set; }
        public string Image1 { get; set; }
        
        public string? VideoURL { get; set; }
        public string Status { get; set; }

        public string CaregiverId { get; set; }
        public string CaregiverName { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedOn { get; set; }
        public bool? IsUpdatedToPause { get; set; }


    }

    public class AddGigRequest
    {
        public string Title { get; set; }
        public string Category { get; set; }
        public List<string> SubCategory { get; set; }
        public string Tags { get; set; }
        public string PackageType { get; set; }
        public string PackageName { get; set; }
        public string PackageDetails { get; set; }
        public string DeliveryTime { get; set; }
        public int Price { get; set; }
        
        public IFormFile Image1 { get; set; }

        
       // public string? VideoURL { get; set; }
        public string Status { get; set; }

        public string CaregiverId { get; set; }        
    }

    public class UpdateGigStatusToPauseRequest
    {
        public string Status { get; set; }

        public string CaregiverId { get; set; }

    }

    public class UpdateGigRequest
    {
        public string Category { get; set; }
        public List<string> SubCategory { get; set; }
        public string Tags { get; set; }
        public string PackageType { get; set; }
        public string PackageName { get; set; }
        public string PackageDetails { get; set; }
        public string DeliveryTime { get; set; }
        public int Price { get; set; }

        public IFormFile Image1 { get; set; }
               
        public string CaregiverId { get; set; }

    }
}
