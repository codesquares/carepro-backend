using MongoDB.Bson;
using System;

namespace Domain.Entities
{
    /// <summary>
    /// Tracks caregiver outreach email attempts for a care request post event.
    /// Prevents duplicate sends and supports controlled retries.
    /// </summary>
    public class CareRequestBroadcastEmailLog
    {
        public ObjectId Id { get; set; }

        public string CareRequestId { get; set; } = string.Empty;

        public string CaregiverId { get; set; } = string.Empty;

        public bool IsSent { get; set; }

        public DateTime? SentAt { get; set; }

        public DateTime LastAttemptAt { get; set; } = DateTime.UtcNow;

        public int AttemptCount { get; set; }

        public string? LastError { get; set; }
    }
}
