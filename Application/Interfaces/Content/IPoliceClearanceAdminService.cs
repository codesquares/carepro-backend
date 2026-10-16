using Application.DTOs;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    /// <summary>
    /// Staff review of police clearances. Callers (the admin controller) have already passed OperationsPolicy; the reviewer
    /// identity is the JWT's, never the request body's. Throws PoliceClearanceValidationException (400),
    /// PoliceClearanceConflictException (409), KeyNotFoundException (404).
    /// </summary>
    public interface IPoliceClearanceAdminService
    {
        Task<PaginatedResponse<AdminClearanceListItemDTO>> ListAsync(string? status, string? search, int page, int pageSize);
        Task<int> PendingCountAsync();
        Task<AdminClearanceDetailDTO> GetDetailAsync(string clearanceId);
        Task<AdminClearanceDetailDTO> ApproveAsync(string clearanceId, AdminIdentity admin, ApprovePoliceClearanceRequest request);
        Task<AdminClearanceDetailDTO> RejectAsync(string clearanceId, AdminIdentity admin, string? reason);
        Task<AdminClearanceDetailDTO> RevokeAsync(string clearanceId, AdminIdentity admin, string? reason);

        /// <summary>Writes the audit entry FIRST (fail closed), then streams the file.</summary>
        Task<ClearanceFileContent> GetFileForStaffAsync(string clearanceId, int fileIndex, AdminIdentity admin);
    }
}
