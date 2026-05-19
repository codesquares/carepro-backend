using Application.DTOs;

namespace Application.Interfaces.Content
{
    public interface IPushService
    {
        /// <summary>
        /// Returns the VAPID public key to be served to browsers.
        /// Returns null if VAPID keys are not configured.
        /// </summary>
        string? GetVapidPublicKey();

        /// <summary>
        /// Writes a push job to the background channel.
        /// Returns immediately — actual delivery is handled by PushBackgroundConsumer.
        /// </summary>
        ValueTask EnqueueAsync(string userId, PushPayload payload);

        /// <summary>
        /// Upserts a push subscription for a user (idempotent on Endpoint).
        /// </summary>
        Task SubscribeAsync(string userId, SubscribePushRequest request);

        /// <summary>
        /// Removes a push subscription by Endpoint for a user.
        /// </summary>
        Task UnsubscribeAsync(string userId, string endpoint);
    }
}
