using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.DTOs
{
    public class ReviewDTO
    {
        public string ReviewId { get; set; }
        public string ClientId { get; set; }
        public string CaregiverId { get; set; }
        public string GigId { get; set; }
        public string Message { get; set; }
        public int Rating { get; set; }
        public DateTime ReviewedOn { get; set; }
    }


    public class ReviewResponse
    {
        public string ReviewId { get; set; }
        public string ClientId { get; set; }
        public string ClientName { get; set; }
        public string CaregiverId { get; set; }
        public string CaregiverName { get; set; }
        public string GigId { get; set; }
        public string Message { get; set; }
        public int Rating { get; set; }
        public DateTime ReviewedOn { get; set; }
    }


    public class AddReviewRequest
    {
        public string ClientId { get; set; }
        public string CaregiverId { get; set; }
        public string GigId { get; set; }
        public string? Message { get; set; }
        public int Rating { get; set; }
    }

}
