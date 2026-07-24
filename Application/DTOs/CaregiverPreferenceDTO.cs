using System;
using System.Collections.Generic;

namespace Application.DTOs
{
    public class CaregiverPreferenceDTO
    {
        public string? Id { get; set; }
        public string? CaregiverId { get; set; }
        public List<string>? Data { get; set; }
        public CaregiverNotificationPreferencesDTO? NotificationPreferences { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedOn { get; set; }
    }

    public class CaregiverNotificationPreferencesDTO
    {
        public bool EmailNotifications { get; set; }
        public bool SmsNotifications { get; set; }
        public bool MarketingEmails { get; set; }
        public bool NewGig { get; set; }
        public bool CareRequestUpdates { get; set; }
    }

    public class UpdateCaregiverNotificationPreferencesRequest
    {
        public bool EmailNotifications { get; set; }
        public bool SmsNotifications { get; set; }
        public bool MarketingEmails { get; set; }
        public bool NewGig { get; set; }
        public bool CareRequestUpdates { get; set; }
    }

    public class CaregiverNotificationPreferencesResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public CaregiverNotificationPreferencesDTO? Data { get; set; }
    }
}
