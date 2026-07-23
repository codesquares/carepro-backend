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

        /// <summary>
        /// Closes out the immediately-prior billing cycle's order for a subscription once a
        /// renewal's replacement order has been created: releases any submitted-but-unapproved
        /// visits to the caregiver, cancels remaining pending task sheets, and marks the previous
        /// order "Superseded". No-op (logs only) if no supersedable previous order is found —
        /// this is expected for a subscription's first renewal after this feature ships, since
        /// historical orders were never linked to their subscription.
        /// </summary>
        Task SupersedeOrderForRenewalAsync(string subscriptionId, int newCycleNumber);

        /// <summary>
        /// Preventive check, called BEFORE a renewal creates its new order: returns true if
        /// more than one non-terminal order already exists for this subscription. In healthy
        /// operation this is always false, because each prior renewal supersedes its
        /// predecessor before returning. If it's ever true, renewal must refuse to add a third
        /// order on top rather than compounding the anomaly — this is what makes accumulating
        /// simultaneous active orders structurally impossible, not just logged after the fact.
        /// </summary>
        Task<bool> HasConflictingActiveOrdersAsync(string subscriptionId);

    }
}
