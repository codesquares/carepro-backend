using MongoDB.Bson;
using System;

namespace Domain.Entities
{
    public class VisitCheckin
    {
        public ObjectId Id { get; set; } = ObjectId.GenerateNewId();
        public string TaskSheetId { get; set; } = string.Empty;
        public string OrderId { get; set; } = string.Empty;
        public string CaregiverId { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double Accuracy { get; set; }
        public double? DistanceFromServiceAddress { get; set; }
        public DateTime CheckinTimestamp { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
