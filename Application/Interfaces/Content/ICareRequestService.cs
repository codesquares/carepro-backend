using Application.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    public interface ICareRequestService
    {
        /// <summary>
        /// Creates a new care request
        /// </summary>
        Task<CareRequestDTO> CreateCareRequestAsync(CreateCareRequestDTO createCareRequestDTO);

        /// <summary>
        /// Gets all care requests for a specific client
        /// </summary>
        Task<List<CareRequestDTO>> GetCareRequestsByClientIdAsync(string clientId);

        /// <summary>
        /// Gets a single care request by ID
        /// </summary>
        Task<CareRequestDTO> GetCareRequestByIdAsync(string requestId);

        /// <summary>
        /// Updates a care request
        /// </summary>
        Task<CareRequestDTO> UpdateCareRequestAsync(string requestId, UpdateCareRequestDTO updateCareRequestDTO);

        /// <summary>
        /// Cancels a care request (sets status to cancelled)
        /// </summary>
        Task<CareRequestDTO> CancelCareRequestAsync(string requestId);

        /// <summary>
        /// Gets all pending care requests (for admin/caregiver matching)
        /// </summary>
        Task<List<CareRequestDTO>> GetPendingCareRequestsAsync();

        /// <summary>
        /// Updates the status of a care request
        /// </summary>
        Task<CareRequestDTO> UpdateCareRequestStatusAsync(string requestId, string status);
    }
}
