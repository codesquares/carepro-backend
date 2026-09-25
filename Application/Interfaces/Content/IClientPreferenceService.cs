using Application.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    public interface IClientPreferenceService
    {
        Task<string> CreateClientPreferenceAsync(AddClientPreferenceRequest addClientPreferenceRequest);

        Task<ClientPreferenceDTO> GetClientPreferenceAsync(string clientId);

        Task<string> UpdateClientPreferenceAsync(string preferenceId, UpdateClientPreferenceRequest updateClientPreferenceRequest);

        /// <summary>Owning client id of a preference record, or null if there is no such record. Throws ArgumentException for a malformed id. Used to authorize writes by record id.</summary>
        Task<string?> GetPreferenceOwnerIdAsync(string preferenceId);

        // Notification Preferences Methods
        Task<NotificationPreferencesDTO> GetNotificationPreferencesAsync(string clientId);

        Task<NotificationPreferencesDTO> UpdateNotificationPreferencesAsync(string clientId, UpdateNotificationPreferencesRequest updateRequest);
    }
}
