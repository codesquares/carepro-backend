using Application.DTOs;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text.Json;
using System.Threading.Channels;

namespace Infrastructure.Content.Services
{
    /// <summary>
    /// Singleton hosted service that consumes PushJobs from the background channel
    /// and delivers them via the Web Push Protocol using native .NET 8 crypto.
    ///
    /// Uses IServiceScopeFactory to resolve scoped CareProDbContext per job.
    /// Prunes subscriptions that return 404 or 410 (expired / revoked by the browser).
    /// </summary>
    public class PushBackgroundConsumer : BackgroundService
    {
        private readonly Channel<PushJob> _channel;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly WebPushSettings _settings;
        private readonly ILogger<PushBackgroundConsumer> _logger;
        private readonly HttpClient _http;

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public PushBackgroundConsumer(
            Channel<PushJob> channel,
            IServiceScopeFactory scopeFactory,
            IOptions<WebPushSettings> settings,
            IHttpClientFactory httpClientFactory,
            ILogger<PushBackgroundConsumer> logger)
        {
            _channel = channel;
            _scopeFactory = scopeFactory;
            _settings = settings.Value;
            _logger = logger;
            _http = httpClientFactory.CreateClient("WebPush");

            if (!_settings.IsConfigured)
            {
                _logger.LogWarning(
                    "PushBackgroundConsumer: VAPID keys not configured — web push delivery is disabled.");
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (var job in _channel.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await DeliverToUserAsync(job, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Unhandled error processing push job for user {UserId}", job.UserId);
                }
            }
        }

        private async Task DeliverToUserAsync(PushJob job, CancellationToken ct)
        {
            if (!_settings.IsConfigured)
                return;

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<CareProDbContext>();

            var subscriptions = await dbContext.PushSubscriptions
                .Where(s => s.UserId == job.UserId && !s.Disabled)
                .ToListAsync(ct);

            if (subscriptions.Count == 0)
                return;

            var payloadJson = JsonSerializer.Serialize(job.Payload, _jsonOptions);
            var toDisable = new List<string>();

            foreach (var sub in subscriptions)
            {
                try
                {
                    await WebPushHelper.SendAsync(
                        _http,
                        sub.Endpoint,
                        sub.P256dh,
                        sub.Auth,
                        _settings.VapidPublicKey!,
                        _settings.VapidPrivateKey!,
                        _settings.Subject,
                        payloadJson,
                        ct);

                    sub.LastUsedAt = DateTime.UtcNow;
                    dbContext.PushSubscriptions.Update(sub);
                }
                catch (WebPushDeliveryException wpEx) when (
                    wpEx.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                {
                    _logger.LogInformation(
                        "Push subscription expired (HTTP {Status}) for user {UserId} — disabling",
                        (int)wpEx.StatusCode, job.UserId);
                    toDisable.Add(sub.Endpoint);
                }
                catch (WebPushDeliveryException wpEx)
                {
                    _logger.LogWarning(
                        "WebPush delivery failed (HTTP {Status}) for user {UserId}: {Message}",
                        (int)wpEx.StatusCode, job.UserId, wpEx.Message);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "WebPush delivery error for user {UserId}", job.UserId);
                }
            }

            if (toDisable.Count > 0)
            {
                var expired = await dbContext.PushSubscriptions
                    .Where(s => toDisable.Contains(s.Endpoint))
                    .ToListAsync(ct);

                foreach (var s in expired)
                    s.Disabled = true;
            }

            await dbContext.SaveChangesAsync(ct);
        }
    }
}

