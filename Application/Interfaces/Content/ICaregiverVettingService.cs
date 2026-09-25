using Application.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    /// <summary>
    /// Phase 2 caregiver-vetting data capture: professional classification
    /// (type + specialty). Additional vetting sections (guarantors, address
    /// history, social media handles) are added to this service incrementally.
    /// All writes take the caregiver id from the JWT, never the request body.
    /// </summary>
    public interface ICaregiverVettingService
    {
        // 2.1 — Classification
        Task<CaregiverClassificationResponse> GetClassificationAsync(string caregiverId);
        Task<CaregiverClassificationResponse> SetClassificationAsync(string caregiverId, SetCaregiverClassificationRequest request);

        // 9.1 — Experience tier (payroll rate lookup). Admin-driven: caller passes the
        // target caregiverId explicitly, it is not sourced from the JWT.
        Task<CaregiverExperienceTierResponse> GetExperienceTierAsync(string caregiverId);
        Task<CaregiverExperienceTierResponse> SetExperienceTierAsync(string caregiverId, SetCaregiverExperienceTierRequest request);

        // 2.3 — Address history
        Task<IEnumerable<CaregiverAddressHistoryResponse>> GetAddressHistoryAsync(string caregiverId);
        Task<CaregiverAddressHistoryResponse> AddAddressHistoryAsync(string caregiverId, AddCaregiverAddressHistoryRequest request);
        Task<CaregiverAddressHistoryResponse> UpdateAddressHistoryAsync(string caregiverId, string id, UpdateCaregiverAddressHistoryRequest request);
        Task DeleteAddressHistoryAsync(string caregiverId, string id);
        Task<CaregiverAddressHistoryCoverageResponse> GetAddressHistoryCoverageAsync(string caregiverId);

        // 2.4 — Social media handles (self-declared, no verification)
        Task<IEnumerable<CaregiverSocialMediaHandleResponse>> GetSocialMediaHandlesAsync(string caregiverId);
        Task<CaregiverSocialMediaHandleResponse> AddSocialMediaHandleAsync(string caregiverId, AddCaregiverSocialMediaHandleRequest request);
        Task<CaregiverSocialMediaHandleResponse> UpdateSocialMediaHandleAsync(string caregiverId, string id, UpdateCaregiverSocialMediaHandleRequest request);
        Task DeleteSocialMediaHandleAsync(string caregiverId, string id);
    }
}
