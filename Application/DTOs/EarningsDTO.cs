using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.DTOs
{
    public class EarningsDTO
    {
        public string Id { get; set; }
        public string ClientOrderId { get; set; }
        public string CaregiverId { get; set; }
        public decimal Amount { get; set; }
        public DateTime CreatedAt { get; set; }


        //public string Id { get; set; }
        //public string CaregiverId { get; set; }
        //public decimal TotalEarned { get; set; }
        //public decimal WithdrawableAmount { get; set; }
        //public decimal WithdrawnAmount { get; set; }
        //public DateTime CreatedAt { get; set; }
        //public DateTime UpdatedAt { get; set; }
    }

    public class EarningsResponse
    {
        public string Id { get; set; }
        public string ClientOrderId { get; set; }
        public string CaregiverId { get; set; }
        public string CaregiverName { get; set; }
        public string Activity { get; set; }
        public string Description { get; set; }
        public string ClientOrderStatus { get; set; }
        public decimal Amount { get; set; }
        public decimal WithdrawableAmount { get; set; }
        public DateTime OrderCreatedAt { get; set; }
        public DateTime CreatedAt { get; set; }
       // public decimal TotalEarned { get; set; }
       // public decimal WithdrawableAmount { get; set; }
       // public decimal WithdrawnAmount { get; set; }
       // public DateTime LastUpdated { get; set; }
    }


    public class CaregiverEarningSummaryResponse
    {
        public decimal TotalEarning { get; set; }
        public decimal WithdrawableAmount { get; set; }
       // public List<EarningsResponse> Earnings { get; set; }
    }


    public class CreateEarningsRequest
    {
        public string CaregiverId { get; set; }
        public decimal TotalEarned { get; set; }
        public decimal WithdrawableAmount { get; set; }
        public decimal WithdrawnAmount { get; set; }
    }

    public class AddEarningsRequest
    {
        public string ClientOrderId { get; set; }        
        public string CaregiverId { get; set; }        
    }

    public class UpdateEarningsRequest
    {
        public decimal? TotalEarned { get; set; }
        public decimal? WithdrawableAmount { get; set; }
        public decimal? WithdrawnAmount { get; set; }
    }
}
