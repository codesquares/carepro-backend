using Application.Interfaces.Content;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Content.Services
{
    /// <summary>
    /// Phase 10 — background sweep for recurring Package billing. Structurally cloned from
    /// <see cref="RecurringBillingService"/> (the Gig renewal poller), repointed at
    /// <see cref="IPackageSubscriptionService"/> instead of <see cref="ISubscriptionService"/>.
    /// No pending-cancellation finalization step yet — Package recurring cancellation isn't
    /// built in this phase, so there's nothing to sweep for it.
    /// </summary>
    public class PackageRecurringBillingService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<PackageRecurringBillingService> _logger;

        private static readonly System.TimeSpan _checkInterval = System.TimeSpan.FromMinutes(5);

        public PackageRecurringBillingService(
            IServiceScopeFactory scopeFactory,
            ILogger<PackageRecurringBillingService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async System.Threading.Tasks.Task ExecuteAsync(System.Threading.CancellationToken stoppingToken)
        {
            _logger.LogInformation("PackageRecurringBillingService started. Check interval: {Interval}", _checkInterval);

            await System.Threading.Tasks.Task.Delay(System.TimeSpan.FromSeconds(30), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessDueChargesAsync();
                }
                catch (System.Exception ex)
                {
                    _logger.LogError(ex, "Error in PackageRecurringBillingService processing cycle");
                }

                await System.Threading.Tasks.Task.Delay(_checkInterval, stoppingToken);
            }

            _logger.LogInformation("PackageRecurringBillingService stopped");
        }

        private async System.Threading.Tasks.Task ProcessDueChargesAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var packageSubscriptionService = scope.ServiceProvider.GetRequiredService<IPackageSubscriptionService>();

            var dueSubscriptions = await packageSubscriptionService.GetPackageSubscriptionsDueForBillingAsync();
            if (dueSubscriptions.Count == 0) return;

            _logger.LogInformation(
                "PackageRecurringBillingService: Found {Count} package subscription(s) due for billing",
                dueSubscriptions.Count);

            foreach (var subscription in dueSubscriptions)
            {
                try
                {
                    var result = await packageSubscriptionService.ProcessRecurringPackageChargeAsync(subscription.Id.ToString());

                    if (result.IsSuccess)
                    {
                        _logger.LogInformation(
                            "Recurring package charge SUCCESSFUL for PackageSubscription {Id}. TxRef: {TxRef}",
                            subscription.Id, result.Value?.TransactionReference);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Recurring package charge FAILED for PackageSubscription {Id}. Errors: {Errors}",
                            subscription.Id, string.Join(", ", result.Errors));
                    }
                }
                catch (System.Exception ex)
                {
                    _logger.LogError(ex,
                        "Unhandled error processing recurring package charge for PackageSubscription {Id}",
                        subscription.Id);
                }

                await System.Threading.Tasks.Task.Delay(System.TimeSpan.FromSeconds(2));
            }
        }
    }
}
