using Application.DTOs;
using Application.Interfaces.Content;
using Domain.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Infrastructure.Content.Services
{
    public class MarketingSyncService : IMarketingSyncService
    {
        private readonly Channel<BrevoSyncJob> _channel;
        private readonly BrevoSettings _settings;
        private readonly ILogger<MarketingSyncService> _logger;

        public MarketingSyncService(
            Channel<BrevoSyncJob> channel,
            IOptions<BrevoSettings> settings,
            ILogger<MarketingSyncService> logger)
        {
            _channel = channel;
            _settings = settings.Value;
            _logger = logger;
        }

        public ValueTask EnqueueCaregiverSyncAsync(string caregiverId)
        {
            if (!_settings.IsConfigured)
            {
                _logger.LogDebug("Brevo not configured — skipping caregiver sync for {CaregiverId}", caregiverId);
                return ValueTask.CompletedTask;
            }

            return _channel.Writer.WriteAsync(new BrevoSyncJob(caregiverId, "Caregiver"));
        }

        public ValueTask EnqueueClientSyncAsync(string clientId)
        {
            if (!_settings.IsConfigured)
            {
                _logger.LogDebug("Brevo not configured — skipping client sync for {ClientId}", clientId);
                return ValueTask.CompletedTask;
            }

            return _channel.Writer.WriteAsync(new BrevoSyncJob(clientId, "Client"));
        }
    }
}
