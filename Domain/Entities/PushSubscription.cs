using MongoDB.Bson;

namespace Domain.Entities
{
    /// <summary>
    /// Stores a browser Web Push subscription for a user.
    /// One user can have multiple subscriptions (multiple devices/browsers).
    /// Endpoint is the unique key — upserted on subscribe, pruned on 404/410.
    /// </summary>
    public class PushSubscription
    {
        public ObjectId Id { get; set; }

        /// <summary>User who owns this push subscription.</summary>
        public string UserId { get; set; } = string.Empty;

        /// <summary>The push service endpoint URL provided by the browser.</summary>
        public string Endpoint { get; set; } = string.Empty;

        /// <summary>Browser-generated P256DH public key (Base64URL).</summary>
        public string P256dh { get; set; } = string.Empty;

        /// <summary>Browser-generated authentication secret (Base64URL).</summary>
        public string Auth { get; set; } = string.Empty;

        /// <summary>Optional user-agent string for device identification.</summary>
        public string? UserAgent { get; set; }

        /// <summary>Optional platform hint (e.g. "android", "ios", "desktop").</summary>
        public string? Platform { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Updated whenever a push is successfully sent to this subscription.</summary>
        public DateTime LastUsedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Set to true when the push service returns 404 or 410 (subscription expired/revoked).
        /// Disabled subscriptions are skipped and periodically cleaned up.
        /// </summary>
        public bool Disabled { get; set; } = false;
    }
}
