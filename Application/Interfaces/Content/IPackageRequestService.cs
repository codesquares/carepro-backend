using Application.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    /// <summary>
    /// A client's request for a specific pre-priced package (Phase 4). Kept minimal —
    /// fulfilment happens through internal assignment (<see cref="IAssignmentService"/>).
    /// </summary>
    public interface IPackageRequestService
    {
        Task<PackageRequestDTO> CreateAsync(string clientId, CreatePackageRequestRequest request);

        /// <summary>
        /// The client's view. <see cref="PackageRequestDTO.ConfirmedCaregiver"/> is populated
        /// only once an assignment has been accepted — before then the client sees no caregiver.
        /// </summary>
        Task<PackageRequestDTO> GetForClientAsync(string clientId, string packageRequestId);

        /// <summary>
        /// All of the caller's own package requests, newest first — same per-request shape and
        /// <see cref="PackageRequestDTO.ConfirmedCaregiver"/> visibility rule as <see cref="GetForClientAsync"/>.
        /// Takes only the caller's own client id (resolved from the JWT by the controller), never
        /// an arbitrary id — there is no way to list another client's requests through this method.
        /// </summary>
        Task<List<PackageRequestDTO>> GetAllForClientAsync(string clientId);

        /// <summary>
        /// Staff-facing picker for the Assignment Console — every non-deleted request,
        /// optionally filtered by status, oldest first. Resolves client names server-side.
        /// </summary>
        Task<List<AdminPackageRequestDTO>> GetForAdminAsync(string? status);
    }
}
