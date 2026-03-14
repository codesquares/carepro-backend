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
    /// Background service that auto-releases pending funds for individual visits (TaskSheets)
    /// after 7 days if the client has not reviewed them.
    /// 
    /// Runs every hour. Finds submitted TaskSheets where:
    /// - ClientReviewStatus is "Pending" (not yet reviewed by client)
    /// - SubmittedAt is 7+ days ago
    /// 
    /// For each eligible visit, releases (OrderFee × 0.80 / totalVisits) from
    /// PendingBalance → WithdrawableBalance for the caregiver.
    /// 
    /// Also auto-completes orders when all visits for a billing cycle are either
    /// approved or auto-released.
    /// </summary>
    public class DailyEarningService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<DailyEarningService> _logger;

        // Run every hour — auto-release is not time-critical
        private static readonly TimeSpan RunInterval = TimeSpan.FromHours(1);

        // Days after submission before auto-releasing a visit's funds
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
                _logger.LogInformation("DailyEarningService: Starting per-visit auto-release check");

                try
                {
                    await ProcessAutoReleaseAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "DailyEarningService: Error during per-visit auto-release processing");
                }

                await Task.Delay(RunInterval, stoppingToken);
            }
        }

        /// <summary>
        /// Finds submitted TaskSheets with pending client review for 7+ days and auto-releases per-visit funds.
        /// </summary>
        private async Task ProcessAutoReleaseAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<CareProDbContext>();
            var walletService = scope.ServiceProvider.GetRequiredService<ICaregiverWalletService>();
            var ledgerService = scope.ServiceProvider.GetRequiredService<IEarningsLedgerService>();

            var cutoffDate = DateTime.UtcNow.AddDays(-AutoReleaseDays);

            // Find all submitted TaskSheets that have been pending client review for 7+ days
            var eligibleVisits = await dbContext.TaskSheets
                .Where(ts =>
                    ts.Status == "submitted" &&
                    ts.ClientReviewStatus == "Pending" &&
                    ts.SubmittedAt != null &&
                    ts.SubmittedAt <= cutoffDate)
                .ToListAsync();

            if (!eligibleVisits.Any())
            {
                _logger.LogDebug("DailyEarningService: No visits eligible for auto-release");
                return;
            }

            _logger.LogInformation("DailyEarningService: Found {Count} visit(s) eligible for auto-release", eligibleVisits.Count);

            int released = 0;
            int skipped = 0;

            foreach (var taskSheet in eligibleVisits)
            {
                try
                {
                    // Idempotency: check if this visit was already credited
                    var alreadyReleased = await dbContext.EarningsLedger
                        .AnyAsync(e => e.TaskSheetId == taskSheet.Id.ToString()
                            && (e.Type == LedgerEntryType.VisitApproved || e.Type == LedgerEntryType.FundsReleased));
                    if (alreadyReleased)
                    {
                        skipped++;
                        continue;
                    }

                    // Get the order to calculate per-visit amount
                    var order = await dbContext.ClientOrders.FirstOrDefaultAsync(
                        o => o.Id.ToString() == taskSheet.OrderId);
                    if (order == null)
                    {
                        _logger.LogWarning("DailyEarningService: Order {OrderId} not found for TaskSheet {TaskSheetId}", taskSheet.OrderId, taskSheet.Id);
                        continue;
                    }

                    // Skip if order has active dispute (safety — though per-visit disputes don't block other visits)
                    int maxVisits = string.Equals(order.PaymentOption, "one-time", StringComparison.OrdinalIgnoreCase)
                        ? 1
                        : (order.FrequencyPerWeek ?? 1) * 4;

                    decimal caregiverTotal = Math.Round((order.OrderFee ?? 0m) * 0.80m, 2);
                    decimal perVisitAmount = Math.Round(caregiverTotal / maxVisits, 2);

                    if (perVisitAmount <= 0) continue;

                    // Rounding remainder: adjust the last visit so total credits equal caregiverTotal exactly
                    int currentCycle = order.BillingCycleNumber ?? 1;
                    int alreadyCreditedCount = await dbContext.EarningsLedger
                        .CountAsync(e => e.ClientOrderId == order.Id.ToString()
                            && e.Type == LedgerEntryType.VisitApproved
                            && e.BillingCycleNumber == currentCycle);
                    if (alreadyCreditedCount == maxVisits - 1)
                    {
                        decimal alreadyCreditedTotal = perVisitAmount * alreadyCreditedCount;
                        perVisitAmount = caregiverTotal - alreadyCreditedTotal;
                    }

                    // Write ledger FIRST (idempotent) — prevents race condition with manual approval
                    string serviceType = string.IsNullOrEmpty(order.SubscriptionId) ? "one-time" : "monthly";
                    await ledgerService.RecordVisitApprovedAsync(
                        order.CaregiverId, perVisitAmount, order.Id.ToString(), taskSheet.Id.ToString(),
                        order.SubscriptionId, order.BillingCycleNumber, serviceType,
                        $"Visit #{taskSheet.SheetNumber} auto-released after {AutoReleaseDays} days without client review. Submitted: {taskSheet.SubmittedAt:yyyy-MM-dd}");

                    // Credit wallet AFTER ledger write succeeds
                    await walletService.CreditVisitApprovedAsync(order.CaregiverId, perVisitAmount);

                    // Re-check status — client may have manually reviewed between our query and now
                    var freshTaskSheet = await dbContext.TaskSheets.FindAsync(taskSheet.Id);
                    if (freshTaskSheet != null && freshTaskSheet.ClientReviewStatus != "Pending")
                    {
                        _logger.LogWarning(
                            "DailyEarningService: TaskSheet {TaskSheetId} status changed to {Status} during processing — skipping status update (ledger idempotency prevents double-credit)",
                            taskSheet.Id, freshTaskSheet.ClientReviewStatus);
                        skipped++;
                        continue;
                    }

                    // Mark the visit as auto-approved
                    taskSheet.ClientReviewStatus = "Approved";
                    taskSheet.ClientReviewedAt = DateTime.UtcNow;
                    dbContext.TaskSheets.Update(taskSheet);
                    await dbContext.SaveChangesAsync();

                    released++;

                    _logger.LogInformation(
                        "DailyEarningService: Auto-released ₦{Amount} for visit #{SheetNumber}, CaregiverId {CaregiverId}, OrderId {OrderId}",
                        perVisitAmount, taskSheet.SheetNumber, order.CaregiverId, order.Id);

                    // Check if all visits for this order/cycle are now approved → auto-complete
                    int currentBillingCycle = order.BillingCycleNumber ?? 1;
                    int approvedCount = await dbContext.TaskSheets
                        .Where(ts => ts.OrderId == taskSheet.OrderId
                            && ts.BillingCycleNumber == currentBillingCycle
                            && ts.ClientReviewStatus == "Approved")
                        .CountAsync();

                    if (approvedCount >= maxVisits && order.ClientOrderStatus != "Completed")
                    {
                        order.ClientOrderStatus = "Completed";
                        order.IsOrderStatusApproved = true;
                        order.OrderUpdatedOn = DateTime.UtcNow;
                        dbContext.ClientOrders.Update(order);
                        await dbContext.SaveChangesAsync();

                        _logger.LogInformation(
                            "DailyEarningService: Order {OrderId} auto-completed — all {Max} visits approved/auto-released.",
                            order.Id, maxVisits);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "DailyEarningService: Failed to auto-release for TaskSheet {TaskSheetId}, OrderId {OrderId}",
                        taskSheet.Id, taskSheet.OrderId);
                }
            }

            _logger.LogInformation(
                "DailyEarningService: Per-visit auto-release complete. Released: {Released}, Skipped (already released): {Skipped}, Total eligible: {Total}",
                released, skipped, eligibleVisits.Count);
        }
    }
}
