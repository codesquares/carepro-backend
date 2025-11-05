using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Entities
{
    public class NotificationPreferences
    {
        [BsonElement("emailNotifications")]
        public bool EmailNotifications { get; set; } = true;

        [BsonElement("smsNotifications")]
        public bool SmsNotifications { get; set; } = true;

        [BsonElement("marketingEmails")]
        public bool MarketingEmails { get; set; } = false;

        [BsonElement("orderUpdates")]
        public bool OrderUpdates { get; set; } = true;

        [BsonElement("serviceUpdates")]
        public bool ServiceUpdates { get; set; } = true;

        [BsonElement("promotions")]
        public bool Promotions { get; set; } = false;
    }
}