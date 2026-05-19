using Application.DTOs;
using Application.Interfaces.Content;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using System.Threading.Channels;

namespace Infrastructure.Content.Services
{
    /// <summary>
    /// Scoped service that handles subscribe / unsubscribe operations against MongoDB
    /// and enqueues outbound push jobs to the singleton background channel.
    /// </summary>
    public class WebPushService : IPushService
    {
        private readonly CareProDbContext _dbContext;
        private readonly Channel<PushJob> _channel;
        private readonly WebPushSettings _settings;
        private readonly ILogger<WebPushService> _logger;

        public WebPushService(
            CareProDbContext dbContext,
            Channel<PushJob> channel,
            IOptions<WebPushSettings> settings,
            ILogger<WebPushService> logger)
        {
            _dbContext = dbContext;
            _channel = channel;
            _settings = settings.Value;
            _logger = logger;
        }

        public string? GetVapidPublicKey() => _settings.VapidPublicKey;

        public ValueTask EnqueueAsync(string userId, PushPayload payload)
        {
            if (!_settings.IsConfigured)
            {
                _logger.LogDebug("WebPush VAPID keys not configured — skipping push for user {UserId}", userId);
                return ValueTask.CompletedTask;
            }

            return _channel.Writer.WriteAsync(new PushJob(userId, payload));
        }

        public async Task SubscribeAsync(string userId, SubscribePushRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Endpoint))
                throw new ArgumentException("Endpoint is required.");

            var existing = await _dbContext.PushSubscriptions
                .FirstOrDefaultAsync(s => s.Endpoint == request.Endpoint);

            if (existing != null)
            {
                // Upsert: update keys and re-enable in case it was previously disabled
                existing.UserId = userId;
                existing.P256dh = request.P256dh;
                existing.Auth = request.Auth;
                existing.UserAgent = request.UserAgent;
                existing.Platform = request.Platform;
                existing.LastUsedAt = DateTime.UtcNow;
                existing.Disabled = false;

                _dbContext.PushSubscriptions.Update(existing);
            }
            else
            {
                var subscription = new Domain.Entities.PushSubscription
                {
                    Id = ObjectId.GenerateNewId(),
                    UserId = userId,
                    Endpoint = request.Endpoint,
                    P256dh = request.P256dh,
                    Auth = request.Auth,
                    UserAgent = request.UserAgent,
                    Platform = request.Platform,
                    CreatedAt = DateTime.UtcNow,
                    LastUsedAt = DateTime.UtcNow,
                    Disabled = false
                };

                await _dbContext.PushSubscriptions.AddAsync(subscription);
            }

            await _dbContext.SaveChangesAsync();
        }

        public async Task UnsubscribeAsync(string userId, string endpoint)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
                throw new ArgumentException("Endpoint is required.");

            var subscription = await _dbContext.PushSubscriptions
                .FirstOrDefaultAsync(s => s.Endpoint == endpoint && s.UserId == userId);

            if (subscription != null)
            {
                _dbContext.PushSubscriptions.Remove(subscription);
                await _dbContext.SaveChangesAsync();
            }
        }
    }
}
