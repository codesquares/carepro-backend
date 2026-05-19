namespace Application.DTOs
{
    // -----------------------------------------------------------------------
    // Settings (bound from appsettings / env vars)
    // -----------------------------------------------------------------------

    public class WebPushSettings
    {
        public string? VapidPublicKey { get; set; }
        public string? VapidPrivateKey { get; set; }

        /// <summary>
        /// VAPID subject — must be a mailto: or https: URL.
        /// Identifies the application server to the push service.
        /// </summary>
        public string Subject { get; set; } = "mailto:admin@oncarepro.com";

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(VapidPublicKey) &&
            !string.IsNullOrWhiteSpace(VapidPrivateKey);
    }

    // -----------------------------------------------------------------------
    // Controller request / response DTOs
    // -----------------------------------------------------------------------

    public class VapidPublicKeyResponse
    {
        public string PublicKey { get; set; } = string.Empty;
    }

    public class SubscribePushRequest
    {
        public string Endpoint { get; set; } = string.Empty;
        public string P256dh { get; set; } = string.Empty;
        public string Auth { get; set; } = string.Empty;
        public string? UserAgent { get; set; }
        public string? Platform { get; set; }
    }

    public class UnsubscribePushRequest
    {
        public string Endpoint { get; set; } = string.Empty;
    }

    // -----------------------------------------------------------------------
    // Push payload — must match the SW push event handler contract exactly
    // -----------------------------------------------------------------------

    public class PushAction
    {
        public string Action { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
    }

    public class PushPayload
    {
        /// <summary>Notification title shown in the OS notification tray.</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>Notification body text.</summary>
        public string Body { get; set; } = string.Empty;

        /// <summary>Relative path the SW navigates to when the notification is clicked.</summary>
        public string Url { get; set; } = "/notifications";

        /// <summary>
        /// Tag used for notification deduplication / replacement.
        /// Uses the notification type so a second "order_received" replaces the first.
        /// </summary>
        public string Tag { get; set; } = string.Empty;

        /// <summary>
        /// When true a new notification with the same tag re-alerts the user
        /// even if a previous one is still visible.
        /// </summary>
        public bool Renotify { get; set; } = true;

        /// <summary>The persisted notification ID for deep-linking from the SW.</summary>
        public string NotificationId { get; set; } = string.Empty;

        /// <summary>Optional action buttons shown in the OS notification.</summary>
        public List<PushAction> Actions { get; set; } = new();
    }

    // -----------------------------------------------------------------------
    // Internal channel job (not exposed to API consumers)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Enqueued to the background Channel so push delivery is fire-and-forget
    /// from the perspective of the request pipeline.
    /// </summary>
    public record PushJob(string UserId, PushPayload Payload);
}
