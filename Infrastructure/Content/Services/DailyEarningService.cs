using Application.Interfaces.Content;
using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Content.Services
{
    /// <summary>
    /// Background service that auto-releases pending funds for one-time orders
    /// after 7 days if the client has not approved or disputed.
    /// 
    /// Runs every hour. Only affects one-time service orders where:
    /// - Status is "Completed"
    /// - Client has NOT approved (IsOrderStatusApproved == false)
    /// - No active dispute (HasDispute != true)
    /// - Completion date is 7+ days ago
    /// 
    /// For recurring/monthly orders, funds are released immediately at payment time,
    /// so this service does not process them.
    /// </summary>
    public class DailyEarningService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<DailyEarningService> _logger;

        // Run every hour instead of every 10 minutes — auto-release is not time-critical
        private static readonly TimeSpan RunInterval = TimeSpan.FromHours(1);

        // Days after completion before auto-releasing funds
        private const int AutoReleaseDays = 7;

        public DailyEarningService(IServiceScopeFactory scopeFactory, ILogger<DailyEarningService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Initial delay to let the app fully start
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("DailyEarningService: Starting auto-release check");

                try
                {
                    await ProcessAutoReleaseAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "DailyEarningService: Error during auto-release processing");
                }

                await Task.Delay(RunInterval, stoppingToken);
            }
        }

        /// <summary>
        /// Finds one-time orders eligible for auto-release and releases their pending funds.
        /// </summary>
        private async Task ProcessAutoReleaseAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<CareProDbContext>();
            var walletService = scope.ServiceProvider.GetRequiredService<ICaregiverWalletService>();
            var ledgerService = scope.ServiceProvider.GetRequiredService<IEarningsLedgerService>();

            var cutoffDate = DateTime.UtcNow.AddDays(-AutoReleaseDays);

            // Find one-time orders that are completed, not approved by client, not disputed,
            // and have been sitting for 7+ days
            var eligibleOrders = await dbContext.ClientOrders
                .Where(o =>
                    o.ClientOrderStatus == "Completed" &&
                    !o.IsOrderStatusApproved &&
                    o.HasDispute != true &&
                    o.PaymentOption == "one-time" &&
                    o.OrderUpdatedOn <= cutoffDate)
                .ToListAsync();

            if (!eligibleOrders.Any())
            {
                _logger.LogDebug("DailyEarningService: No orders eligible for auto-release");
                return;
            }

            _logger.LogInformation("DailyEarningService: Found {Count} orders eligible for auto-release", eligibleOrders.Count);

            int released = 0;
            int skipped = 0;

            foreach (var order in eligibleOrders)
            {
                try
                {
                    // Check if funds have already been released for this order (idempotency)
                    var alreadyReleased = await ledgerService.HasFundsBeenReleasedForOrderAsync(order.Id.ToString());
                    if (alreadyReleased)
                    {
                        skipped++;
                        continue;
                    }

                    // Release pending funds to withdrawable
                    var orderFeeAmount = (decimal)order.Amount;
                    await walletService.ReleasePendingFundsAsync(order.CaregiverId, orderFeeAmount);
                    await ledgerService.RecordFundsReleasedAsync(
                        order.CaregiverId,
                        orderFeeAmount,
                        order.Id.ToString(),
                        null,  // subscriptionId (one-time orders have no subscription)
                        null,  // billingCycleNumber
                        order.PaymentOption ?? "one-time",
                        "AutoReleased",
                        $"Auto-released after {AutoReleaseDays} days without client action. Order completed on {order.OrderUpdatedOn:yyyy-MM-dd}");

                    released++;

                    _logger.LogInformation(
                        "DailyEarningService: Auto-released {Amount} for CaregiverId {CaregiverId}, OrderId {OrderId}",
                        orderFeeAmount, order.CaregiverId, order.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "DailyEarningService: Failed to auto-release for OrderId {OrderId}, CaregiverId {CaregiverId}",
                        order.Id, order.CaregiverId);
                }
            }

            _logger.LogInformation(
                "DailyEarningService: Auto-release complete. Released: {Released}, Skipped (already released): {Skipped}, Total eligible: {Total}",
                released, skipped, eligibleOrders.Count);
        }
    }
}
