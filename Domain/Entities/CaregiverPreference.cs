using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;

namespace Domain.Entities
{
    public class CaregiverPreference
    {
        [BsonId]
        public ObjectId Id { get; set; }

        [BsonElement("caregiverId")]
        public string CaregiverId { get; set; } = string.Empty;

        [BsonElement("data")]
        public List<string> Data { get; set; } = new();

        [BsonElement("notificationPreferences")]
        public CaregiverNotificationPreferences? NotificationPreferences { get; set; }

        [BsonElement("createdAt")]
        public DateTime CreatedAt { get; set; }

        [BsonElement("updatedOn")]
        public DateTime? UpdatedOn { get; set; }
    }

    public class CaregiverNotificationPreferences
    {
        [BsonElement("emailNotifications")]
        public bool EmailNotifications { get; set; } = true;

        [BsonElement("smsNotifications")]
        public bool SmsNotifications { get; set; } = true;

        [BsonElement("marketingEmails")]
        public bool MarketingEmails { get; set; } = false;

        [BsonElement("promotions")]
        public bool Promotions { get; set; } = false;

        // Maps to notification type: new_gig
        [BsonElement("newGig")]
        public bool NewGig { get; set; } = false;

        // Maps to caregiver-facing care_request_* notification set.
        [BsonElement("careRequestUpdates")]
        public bool CareRequestUpdates { get; set; } = false;
    }
}
