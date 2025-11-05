using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Entities
{
    public class ClientPreference
    {
        [BsonId]
        public ObjectId Id { get; set; }

        [BsonElement("clientId")]
        public string ClientId { get; set; } = string.Empty;

        [BsonElement("data")]
        public List<string> Data { get; set; } = new();

        [BsonElement("notificationPreferences")]
        public NotificationPreferences? NotificationPreferences { get; set; }

        [BsonElement("createdAt")]
        public DateTime CreatedAt { get; set; }

        [BsonElement("updatedOn")]
        public DateTime? UpdatedOn { get; set; }
    }
}
