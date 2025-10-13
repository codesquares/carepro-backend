using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Entities
{
    public class Review
    {
        public ObjectId ReviewId { get; set; }
        public string ClientId { get; set; }
        public string CaregiverId { get; set; }
        public string GigId { get; set; }
        public string Message { get; set; }
        public int Rating { get; set; }
        public DateTime ReviewedOn { get; set; }
    }
}
