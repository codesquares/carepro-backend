using Application.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    public interface IDisputeService
    {
        /// <summary>
        /// Client raises a new dispute (visit-level or order-level).
        /// Notifies admin, caregiver, and client.
        /// </summary>
        Task<DisputeResponse> RaiseDisputeAsync(RaiseDisputeRequest request, string raisedByUserId);

        /// <summary>
        /// Admin resolves an open dispute with action, notes, and summary.
        /// Notifies caregiver and client of resolution.
        /// </summary>
        Task<DisputeResponse> ResolveDisputeAsync(string disputeId, ResolveDisputeRequest request, string adminUserId);

        /// <summary>
        /// Admin moves a dispute to UnderReview status.
        /// </summary>
        Task<DisputeResponse> MarkUnderReviewAsync(string disputeId, string adminUserId);

        /// <summary>
        /// Admin dismisses a dispute (invalid/duplicate).
        /// </summary>
        Task<DisputeResponse> DismissDisputeAsync(string disputeId, ResolveDisputeRequest request, string adminUserId);

        /// <summary>
        /// Get a single dispute by ID.
        /// </summary>
        Task<DisputeResponse> GetDisputeByIdAsync(string disputeId);

        /// <summary>
        /// Get all disputes for an order.
        /// </summary>
        Task<List<DisputeResponse>> GetDisputesByOrderIdAsync(string orderId);

        /// <summary>
        /// Get all disputes for a specific task sheet (visit).
        /// </summary>
        Task<List<DisputeResponse>> GetDisputesByTaskSheetIdAsync(string taskSheetId);

        /// <summary>
        /// Admin: get all disputes, optionally filtered by status.
        /// </summary>
        Task<PaginatedResponse<DisputeResponse>> GetAllDisputesAsync(int page = 1, int pageSize = 20, string? status = null, string? disputeType = null);

        /// <summary>
        /// Client reviews a visit (task sheet): approve or dispute.
        /// If disputed, automatically creates a Dispute record.
        /// </summary>
        Task<DisputeResponse?> ReviewVisitAsync(string taskSheetId, ReviewVisitRequest request, string clientUserId);
    }
}
