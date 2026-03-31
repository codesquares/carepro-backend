using Application.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    public interface IRefundRequestService
    {
        /// <summary>
        /// Client submits a refund request to withdraw funds from their wallet.
        /// </summary>
        Task<Result<RefundRequestResponse>> CreateRefundRequestAsync(CreateRefundRequestDTO request, string clientId);

        /// <summary>
        /// Gets all refund requests for a specific client.
        /// </summary>
        Task<List<RefundRequestResponse>> GetClientRefundRequestsAsync(string clientId);

        /// <summary>
        /// Gets a single refund request by ID (verifies ownership for clients).
        /// </summary>
        Task<RefundRequestResponse> GetRefundRequestAsync(string requestId, string? clientId = null);

        /// <summary>
        /// Gets all refund requests (admin view) with optional status filter.
        /// </summary>
        Task<List<RefundRequestResponse>> GetAllRefundRequestsAsync(string? status = null);

        /// <summary>
        /// Admin reviews a refund request (approve or reject).
        /// On approval, debits the client wallet.
        /// </summary>
        Task<Result<RefundRequestResponse>> ReviewRefundRequestAsync(string requestId, ReviewRefundRequestDTO review, string adminId);

        /// <summary>
        /// Admin marks an approved refund as completed (after bank transfer is done).
        /// </summary>
        Task<Result<RefundRequestResponse>> CompleteRefundRequestAsync(string requestId, string adminId);
    }
}
