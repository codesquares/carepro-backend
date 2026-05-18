using Application.DTOs;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    /// <summary>
    /// Admin-only operations against caregiver records that need stricter
    /// auditing than the regular caregiver self-service surface. Kept on a
    /// separate interface so the existing <see cref="ICareGiverService"/>
    /// contract is not changed.
    /// </summary>
    public interface IAdminCaregiverService
    {
        /// <summary>
        /// Updates a caregiver's legal name (FirstName / MiddleName / LastName)
        /// after an admin has manually confirmed the change. Also propagates
        /// FirstName / LastName to the matching AppUser record (AppUser does
        /// not have a MiddleName field). Records the before/after snapshot in
        /// the AdminAuditLogs collection.
        /// </summary>
        Task<AdminUpdateCaregiverNameResponse> UpdateCaregiverLegalNameAsync(
            string caregiverId,
            AdminUpdateCaregiverNameRequest request);

        Task<AdminBulkClearMiddleNameResponse> BulkClearCaregiverMiddleNameAsync(
            AdminBulkClearMiddleNameRequest request);
    }
}
