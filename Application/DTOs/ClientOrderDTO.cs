using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.DTOs
{
    public class ClientOrderDTO
    {
        public string Id { get; set; }
        public string ClientId { get; set; }
        public string CaregiverId { get; set; }
        public string GigId { get; set; }
        public string PaymentOption { get; set; }
        public int Amount { get; set; }
        public string TransactionId { get; set; }
        public string ClientOrderStatus { get; set; }

        public DateTime OrderCreatedAt { get; set; }
    }


    public class ClientOrderResponse
    {
        public string Id { get; set; }

        public string ClientId { get; set; }
        public string ClientName { get; set; }

        public string CaregiverId { get; set; }
        public string CaregiverName { get; set; }

        public string GigId { get; set; }
        public string GigTitle { get; set; }
        public List<string> GigPackageDetails { get; set; }
        public string GigImage { get; set; }
        public string GigStatus { get; set; }


        public string PaymentOption { get; set; }
        public int Amount { get; set; }
        public string TransactionId { get; set; }
        public string? ClientOrderStatus { get; set; }
        public DateTime OrderCreatedOn { get; set; }

        public string? DeclineReason { get; set; }
        public bool? IsDeclined { get; set; }


       // public decimal TotalEarning { get; set; }
        public int NoOfOrders { get; set; }
       // public int NoOfHoursSpent { get; set; }
    }

    public class CaregiverClientOrdersSummaryResponse
    {
        public int NoOfOrders { get; set; }
        public decimal TotalEarning { get; set; }
        public List<ClientOrderResponse> ClientOrders { get; set; }
    }


    public class AddClientOrderRequest
    {
        public string ClientId { get; set; }
        public string GigId { get; set; }
        public string PaymentOption { get; set; }
        public int Amount { get; set; }
        public string TransactionId { get; set; }
    }

    public class UpdateClientOrderStatusRequest
    {
        public string ClientOrderStatus { get; set; }
        public string UserId { get; set; }        
    }


    public class UpdateClientOrderStatusHasDisputeRequest
    {
        public string ClientOrderStatus { get; set; }
        public string DisputeReason { get; set; }
        public string UserId { get; set; }
    }
}
