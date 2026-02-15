using Application.Interfaces.Content;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Content.Services
{
    /// <summary>
    /// Background service that processes recurring subscription charges.
    /// Runs every 5 minutes and handles:
    /// 1. Charging subscriptions whose billing period has ended
    /// 2. Finalizing cancellations for subscriptions past their period end
    /// 3. Retrying failed charges on schedule
    /// </summary>
    public class RecurringBillingService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<RecurringBillingService> _logger;

        // How often the billing processor runs
        private static readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(5);

        public RecurringBillingService(
            IServiceScopeFactory scopeFactory,
            ILogger<RecurringBillingService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("RecurringBillingService started. Check interval: {Interval}", _checkInterval);

            // Initial delay to let the app fully start
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessBillingCycleAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in RecurringBillingService processing cycle");
                }

                await Task.Delay(_checkInterval, stoppingToken);
            }

            _logger.LogInformation("RecurringBillingService stopped");
        }

        private async Task ProcessBillingCycleAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var subscriptionService = scope.ServiceProvider.GetRequiredService<ISubscriptionService>();

            // ── Step 1: Process due charges ──
            await ProcessDueChargesAsync(subscriptionService);

            // ── Step 2: Finalize pending cancellations ──
            await FinalizePendingCancellationsAsync(subscriptionService);
        }

        private async Task ProcessDueChargesAsync(ISubscriptionService subscriptionService)
        {
            var dueSubscriptions = await subscriptionService.GetSubscriptionsDueForBillingAsync();

            if (dueSubscriptions.Count == 0) return;

            _logger.LogInformation(
                "RecurringBillingService: Found {Count} subscription(s) due for billing",
                dueSubscriptions.Count);

            foreach (var subscription in dueSubscriptions)
            {
                try
                {
                    _logger.LogInformation(
                        "Processing recurring charge for subscription {SubscriptionId} " +
                        "(Client: {ClientId}, Amount: {Amount} {Currency}, Cycle #{Cycle})",
                        subscription.Id, subscription.ClientId,
                        subscription.RecurringAmount, subscription.Currency,
                        subscription.BillingCyclesCompleted + 1);

                    var result = await subscriptionService.ProcessRecurringChargeAsync(subscription.Id);

                    if (result.IsSuccess)
                    {
                        _logger.LogInformation(
                            "Recurring charge SUCCESSFUL for subscription {SubscriptionId}. " +
                            "TxRef: {TxRef}, OrderId: {OrderId}",
                            subscription.Id,
                            result.Value?.TransactionReference,
                            result.Value?.ClientOrderId);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Recurring charge FAILED for subscription {SubscriptionId}. Errors: {Errors}",
                            subscription.Id, string.Join(", ", result.Errors));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Unhandled error processing recurring charge for subscription {SubscriptionId}",
                        subscription.Id);
                }

                // Small delay between charges to avoid overwhelming the payment gateway
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }

        private async Task FinalizePendingCancellationsAsync(ISubscriptionService subscriptionService)
        {
            var pendingCancellations = await subscriptionService.GetSubscriptionsPendingFinalCancellationAsync();

            if (pendingCancellations.Count == 0) return;

            _logger.LogInformation(
                "RecurringBillingService: Finalizing {Count} pending cancellation(s)",
                pendingCancellations.Count);

            foreach (var subscription in pendingCancellations)
            {
                try
                {
                    await subscriptionService.FinalizeCancellationAsync(subscription.Id);
                    _logger.LogInformation(
                        "Subscription {SubscriptionId} cancellation finalized",
                        subscription.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Error finalizing cancellation for subscription {SubscriptionId}",
                        subscription.Id);
                }
            }
        }
    }
}
