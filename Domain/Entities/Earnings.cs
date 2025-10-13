using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Entities
{
    public class Earnings
    {
        public ObjectId Id { get; set; }
        public string ClientOrderId { get; set; }
        public string ClientOrderStatus { get; set; }
        public string CaregiverId { get; set; }
        public decimal Amount { get; set; }
        public DateTime CreatedAt { get; set; }



        //public string CaregiverId { get; set; }
        //public decimal TotalEarned { get; set; }
        //public decimal WithdrawableAmount { get; set; }
        //public decimal WithdrawnAmount { get; set; }
       
        //public DateTime UpdatedAt { get; set; }
    }
}
