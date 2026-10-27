using Application.DTOs;
using Domain.Entities;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    /// <summary>
    /// Caregiver side of address proof (utility bill for the CURRENT address). Reuses the police-clearance validation/conflict
    /// exceptions so the controllers map errors the same way. The caregiver id always comes from the JWT.
    /// </summary>
    public interface IAddressProofService
    {
        Task<AddressProofInfoDTO> SubmitAsync(string caregiverId, string addressId, SubmitAddressProofRequest request);

        /// <summary>The caller's own bill. Someone else's record is UnauthorizedAccessException (403).</summary>
        Task<ClearanceFileContent> GetOwnFileAsync(string caregiverId, string addressId);
    }

    public interface IAddressProofAdminService
    {
        Task<PaginatedResponse<AdminAddressProofListItemDTO>> ListAsync(string? status, string? search, int page, int pageSize);
        Task<int> PendingCountAsync();
        Task<AdminAddressProofDetailDTO> GetDetailAsync(string addressId);
        Task<AdminAddressProofDetailDTO> ApproveAsync(string addressId, AdminIdentity admin, ApproveAddressProofRequest request);
        Task<AdminAddressProofDetailDTO> RejectAsync(string addressId, AdminIdentity admin, string? reason);
        Task<ClearanceFileContent> GetFileForStaffAsync(string addressId, AdminIdentity admin);
    }

    /// <summary>Best-effort notifications: a delivery failure is logged, never thrown.</summary>
    public interface IAddressProofNotifier
    {
        /// <summary>To Approved admins, in-app only.</summary>
        Task NotifySubmittedAsync(CaregiverAddressHistory record, string caregiverName);
        /// <summary>To the caregiver: in-app + email.</summary>
        Task NotifyApprovedAsync(string caregiverId, string addressId);
        /// <summary>To the caregiver: in-app + email, both carrying the reason.</summary>
        Task NotifyRejectedAsync(string caregiverId, string addressId, string reason);
    }
}
