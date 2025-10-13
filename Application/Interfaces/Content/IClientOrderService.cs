using Application.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    public interface IClientOrderService
    {
        //Task<ClientOrderDTO> CreateClientOrderAsync(AddClientOrderRequest addClientOrderRequest);
        Task<Result<ClientOrderDTO>> CreateClientOrderAsync(AddClientOrderRequest addClientOrderRequest);


        Task<IEnumerable<ClientOrderResponse>> GetAllClientOrderAsync(string clientUserId);
        Task<IEnumerable<ClientOrderResponse>> GetCaregiverOrdersAsync(string caregiverId);
        Task<IEnumerable<ClientOrderResponse>> GetAllClientOrdersByGigIdAsync(string gigId);

        Task<ClientOrderResponse> GetClientOrderAsync(string orderId);

        Task<CaregiverClientOrdersSummaryResponse> GetAllCaregiverOrderAsync(string caregiverId);

        Task<string> UpdateClientOrderStatusAsync(string orderId, UpdateClientOrderStatusRequest  updateClientOrderStatusRequest);

        Task<string> UpdateOrderStatusToApproveAsync(string orderId );


        Task<string> UpdateClientOrderStatusHasDisputeAsync(string orderId, UpdateClientOrderStatusHasDisputeRequest updateClientOrderStatusDeclinedRequest);


    }
}
