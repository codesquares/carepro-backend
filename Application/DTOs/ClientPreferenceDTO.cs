using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.DTOs
{
    public class ClientPreferenceDTO
    {
        public string? Id { get; set; }
        public string? ClientId { get; set; }
        public List<string>? Data { get; set; }
        public NotificationPreferencesDTO? NotificationPreferences { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedOn { get; set; }
    }

    public class AddClientPreferenceRequest
    {
        public string? ClientId { get; set; }
        public List<string>? Data { get; set; }
    }

    public class UpdateClientPreferenceRequest
    {
        public List<string>? Data { get; set; }
    }

    public class NotificationPreferencesDTO
    {
        public bool EmailNotifications { get; set; }
        public bool SmsNotifications { get; set; }
        public bool MarketingEmails { get; set; }
        public bool OrderUpdates { get; set; }
        public bool ServiceUpdates { get; set; }
        public bool Promotions { get; set; }
    }

    public class UpdateNotificationPreferencesRequest
    {
        public bool EmailNotifications { get; set; }
        public bool SmsNotifications { get; set; }
        public bool MarketingEmails { get; set; }
        public bool OrderUpdates { get; set; }
        public bool ServiceUpdates { get; set; }
        public bool Promotions { get; set; }
    }

    public class NotificationPreferencesResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public NotificationPreferencesDTO? Data { get; set; }
    }
}
