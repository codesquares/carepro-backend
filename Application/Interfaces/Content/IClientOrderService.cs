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
        Task<IEnumerable<ClientOrderResponse>> GetAllOrdersAsync();

        Task<PaginatedResponse<ClientOrderResponse>> GetAllOrdersPaginatedAsync(int page = 1, int pageSize = 20, string? status = null, string? search = null);

        Task<ClientOrderResponse> GetClientOrderAsync(string orderId);

        Task<CaregiverClientOrdersSummaryResponse> GetAllCaregiverOrderAsync(string caregiverId);

        Task<string> UpdateClientOrderStatusAsync(string orderId, UpdateClientOrderStatusRequest updateClientOrderStatusRequest);

        Task<string> UpdateOrderStatusToApproveAsync(string orderId);

        Task<string> ReleaseFundsAsync(string orderId, string clientUserId);

        Task<string> UpdateClientOrderStatusHasDisputeAsync(string orderId, UpdateClientOrderStatusHasDisputeRequest updateClientOrderStatusDeclinedRequest);

        /// <summary>
        /// Cancels an order: invalidates booking commitment, debits unreleased earnings from caregiver,
        /// cancels future task sheets, and sends notifications to both client and caregiver.
        /// </summary>
        Task<Result<string>> CancelOrderAsync(string orderId, string clientUserId, string? reason = null);

    }
}
