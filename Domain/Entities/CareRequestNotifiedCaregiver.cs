using MongoDB.Bson;
using System;

namespace Domain.Entities
{
    /// <summary>
    /// Tracks which caregivers were notified about a care request match,
    /// so we can avoid re-notifying and power the caregiver browse page.
    /// </summary>
    public class CareRequestNotifiedCaregiver
    {
        public ObjectId Id { get; set; }

        /// <summary>FK → CareRequest (ObjectId as string)</summary>
        public string CareRequestId { get; set; } = string.Empty;

        /// <summary>FK → Caregiver (ObjectId as string)</summary>
        public string CaregiverId { get; set; } = string.Empty;

        /// <summary>When the notification was sent</summary>
        public DateTime NotifiedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Match score at time of notification</summary>
        public double MatchScore { get; set; }
    }
}
