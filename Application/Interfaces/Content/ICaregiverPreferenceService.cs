using Application.DTOs;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    public interface ICaregiverPreferenceService
    {
        Task<CaregiverNotificationPreferencesDTO> GetNotificationPreferencesAsync(string caregiverId);

        Task<CaregiverNotificationPreferencesDTO> UpdateNotificationPreferencesAsync(
            string caregiverId,
            UpdateCaregiverNotificationPreferencesRequest updateRequest);

        Task UpsertFromSignupConsentAsync(string caregiverId, bool marketingConsent);
    }
}
