using Application.DTOs;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    public interface ICareRequestResponseService
    {
        /// <summary>
        /// Caregiver responds (shows interest) to a care request.
        /// </summary>
        Task<CareRequestRespondResult> RespondToRequestAsync(string careRequestId, string caregiverId, RespondToCareRequestDTO dto);

        /// <summary>
        /// Get full client-side request detail with responders grouped by status.
        /// </summary>
        Task<CareRequestDetailDTO> GetRequestDetailForClientAsync(string careRequestId, string clientId);

        /// <summary>
        /// Client shortlists a responder.
        /// </summary>
        Task<ShortlistResult> ShortlistResponseAsync(string careRequestId, string responseId, string clientId);

        /// <summary>
        /// Client removes a responder from shortlist (back to pending).
        /// </summary>
        Task<ShortlistResult> RemoveShortlistAsync(string careRequestId, string responseId, string clientId);

        /// <summary>
        /// Client hires a responder — generates a special gig scoped to client+caregiver.
        /// </summary>
        Task<HireResult> HireResponderAsync(string careRequestId, string responseId, string clientId);

        /// <summary>
        /// Get paginated care requests matching a caregiver's profile (browse page).
        /// </summary>
        Task<CaregiverMatchedRequestsResponse> GetMatchedRequestsForCaregiverAsync(
            string caregiverId, string? serviceType, decimal? budgetMin, decimal? budgetMax,
            string? location, int page, int pageSize);

        /// <summary>
        /// Get a single care request detail from caregiver's perspective (anonymized client).
        /// </summary>
        Task<CaregiverRequestDetailDTO> GetCaregiverViewAsync(string careRequestId, string caregiverId);
    }
}
