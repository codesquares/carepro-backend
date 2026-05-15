using Application.Interfaces.Content;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Content.Services
{
    /// <summary>
    /// Background service that rebuilds CaregiverJourneySnapshot for every active
    /// caregiver every 15 minutes. Follows the same BackgroundService pattern as
    /// ImmediateNotificationProcessor and RecurringBillingService.
    /// The snapshot is always rebuilt from scratch from the source collections —
    /// no event wiring required. All journey signals stay fresh automatically.
    /// </summary>
    public class CaregiverSnapshotProcessor : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<CaregiverSnapshotProcessor> _logger;
        private static readonly TimeSpan _interval = TimeSpan.FromMinutes(15);

        public CaregiverSnapshotProcessor(
            IServiceScopeFactory scopeFactory,
            ILogger<CaregiverSnapshotProcessor> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("CaregiverSnapshotProcessor started. Interval: {Interval}", _interval);

            // Allow the app to fully start before the first run
            await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken);

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        await RebuildAsync(stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "CaregiverSnapshotProcessor: Unhandled error during rebuild cycle");
                    }

                    await Task.Delay(_interval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown
            }

            _logger.LogInformation("CaregiverSnapshotProcessor stopped");
        }

        private async Task RebuildAsync(CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var snapshotService = scope.ServiceProvider
                .GetRequiredService<ICaregiverSnapshotService>();

            await snapshotService.RebuildAllSnapshotsAsync();
        }
    }
}
