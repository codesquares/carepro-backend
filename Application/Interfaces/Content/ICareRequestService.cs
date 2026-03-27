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
        /// Cancels a care request (sets status to cancelled). Notifies pending responders.
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

        /// <summary>
        /// Pauses a care request — hides from caregiver browse, stops matching notifications.
        /// </summary>
        Task<CareRequestDTO> PauseCareRequestAsync(string requestId, string clientId);

        /// <summary>
        /// Re-opens a paused care request — makes it visible again.
        /// </summary>
        Task<CareRequestDTO> ReopenCareRequestAsync(string requestId, string clientId);

        /// <summary>
        /// Closes a care request — fulfilled/done. Notifies all pending responders.
        /// </summary>
        Task<CareRequestDTO> CloseCareRequestAsync(string requestId, string clientId);

        /// <summary>
        /// Soft-deletes a care request (sets DeletedAt). Only allowed if no active hires.
        /// </summary>
        Task SoftDeleteCareRequestAsync(string requestId, string clientId);
    }
}
