using Application.DTOs;
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
    }
}
