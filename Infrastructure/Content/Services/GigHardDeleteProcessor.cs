using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Content.Services
{
    /// <summary>
    /// GDPR-compliant background service that permanently deletes or anonymizes
    /// gig data after the 30-day grace period following soft-deletion.
    ///
    /// Runs once every 24 hours. For each gig where IsDeleted == true and
    /// DeletedOn is 30+ days ago:
    ///
    /// 1. Hard-deletes: VisitCheckins, Notifications, PendingPayments, BookingCommitments
    /// 2. Anonymizes: Reviews, Contracts (PII fields), TaskSheets, ObservationReports
    /// 3. Retains as-is: BillingRecords (tax law), IncidentReports (legal liability), ClientOrders (financial audit)
    /// 4. Hard-deletes the Gig record itself
    ///
    /// All operations are logged for audit compliance. Each gig is processed
    /// independently so a failure on one does not block others.
    /// </summary>
    public class GigHardDeleteProcessor : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<GigHardDeleteProcessor> _logger;

        private static readonly TimeSpan RunInterval = TimeSpan.FromHours(24);
        private const int GracePeriodDays = 30;
        private const string RedactedMarker = "[REDACTED]";

        public GigHardDeleteProcessor(IServiceScopeFactory scopeFactory, ILogger<GigHardDeleteProcessor> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                // Let the app fully start before running
                await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

                while (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogInformation("GigHardDeleteProcessor: Starting GDPR hard-delete cycle");

                    try
                    {
                        await ProcessExpiredGigsAsync(stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "GigHardDeleteProcessor: Unhandled error during hard-delete cycle");
                    }

                    await Task.Delay(RunInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown — do not propagate
            }

            _logger.LogInformation("GigHardDeleteProcessor: Stopped");
        }

        private async Task ProcessExpiredGigsAsync(CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<CareProDbContext>();

            var cutoffDate = DateTime.UtcNow.AddDays(-GracePeriodDays);

            // IgnoreQueryFilters bypasses the global IsDeleted filter so we can find soft-deleted gigs
            var expiredGigs = await dbContext.Gigs
                .IgnoreQueryFilters()
                .Where(g => g.IsDeleted == true && g.DeletedOn != null && g.DeletedOn <= cutoffDate)
                .ToListAsync(stoppingToken);

            if (!expiredGigs.Any())
            {
                _logger.LogDebug("GigHardDeleteProcessor: No gigs past grace period");
                return;
            }

            _logger.LogInformation("GigHardDeleteProcessor: Found {Count} gig(s) past {Days}-day grace period", expiredGigs.Count, GracePeriodDays);

            int processed = 0;
            int failed = 0;

            foreach (var gig in expiredGigs)
            {
                if (stoppingToken.IsCancellationRequested) break;

                try
                {
                    await ProcessSingleGigDeletionAsync(dbContext, gig);
                    processed++;
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogError(ex, "GigHardDeleteProcessor: Failed to process gig {GigId}", gig.Id);
                }
            }

            _logger.LogInformation(
                "GigHardDeleteProcessor: Cycle complete. Processed: {Processed}, Failed: {Failed}",
                processed, failed);
        }

        private async Task ProcessSingleGigDeletionAsync(CareProDbContext dbContext, Gig gig)
        {
            var gigId = gig.Id.ToString();

            _logger.LogInformation("GigHardDeleteProcessor: Processing gig {GigId} (deleted on {DeletedOn})", gigId, gig.DeletedOn);

            // ── 1. Find all related ClientOrders for this gig (needed for indirect relations) ──
            var relatedOrderIds = await dbContext.ClientOrders
                .Where(o => o.GigId == gigId)
                .Select(o => o.Id.ToString())
                .ToListAsync();

            // ── 2. Hard-delete: VisitCheckins (GPS data is sensitive PII) ──
            if (relatedOrderIds.Any())
            {
                var visitCheckins = await dbContext.VisitCheckins
                    .Where(vc => relatedOrderIds.Contains(vc.OrderId))
                    .ToListAsync();
                if (visitCheckins.Any())
                {
                    dbContext.VisitCheckins.RemoveRange(visitCheckins);
                    _logger.LogInformation("GigHardDeleteProcessor: Gig {GigId} - Deleting {Count} visit check-ins", gigId, visitCheckins.Count);
                }
            }

            // ── 3. Hard-delete: Notifications referencing this gig ──
            var notifications = await dbContext.Notifications
                .Where(n => n.RelatedEntityId == gigId)
                .ToListAsync();
            if (notifications.Any())
            {
                dbContext.Notifications.RemoveRange(notifications);
                _logger.LogInformation("GigHardDeleteProcessor: Gig {GigId} - Deleting {Count} notifications", gigId, notifications.Count);
            }

            // ── 4. Hard-delete: PendingPayments (operational data, no retention value) ──
            var pendingPayments = await dbContext.PendingPayments
                .Where(pp => pp.GigId == gigId)
                .ToListAsync();
            if (pendingPayments.Any())
            {
                dbContext.PendingPayments.RemoveRange(pendingPayments);
                _logger.LogInformation("GigHardDeleteProcessor: Gig {GigId} - Deleting {Count} pending payments", gigId, pendingPayments.Count);
            }

            // ── 5. Hard-delete: BookingCommitments (operational data) ──
            var bookingCommitments = await dbContext.BookingCommitments
                .Where(bc => bc.GigId == gigId)
                .ToListAsync();
            if (bookingCommitments.Any())
            {
                dbContext.BookingCommitments.RemoveRange(bookingCommitments);
                _logger.LogInformation("GigHardDeleteProcessor: Gig {GigId} - Deleting {Count} booking commitments", gigId, bookingCommitments.Count);
            }

            // ── 6. Hard-delete: OrderTasks (task drafts, no financial value) ──
            var orderTasks = await dbContext.OrderTasks
                .Where(ot => ot.GigId == gigId)
                .ToListAsync();
            if (orderTasks.Any())
            {
                dbContext.OrderTasks.RemoveRange(orderTasks);
                _logger.LogInformation("GigHardDeleteProcessor: Gig {GigId} - Deleting {Count} order tasks", gigId, orderTasks.Count);
            }

            // ── 7. Anonymize: Reviews (keep rating for analytics, strip PII) ──
            var reviews = await dbContext.Reviews
                .Where(r => r.GigId == gigId)
                .ToListAsync();
            foreach (var review in reviews)
            {
                review.Message = RedactedMarker;
                review.ClientId = RedactedMarker;
                dbContext.Reviews.Update(review);
            }
            if (reviews.Any())
            {
                _logger.LogInformation("GigHardDeleteProcessor: Gig {GigId} - Anonymized {Count} reviews", gigId, reviews.Count);
            }

            // ── 8. Anonymize: Contracts (strip PII fields, keep structure + financial terms) ──
            var contracts = await dbContext.Contracts
                .Where(c => c.GigId == gigId)
                .ToListAsync();
            foreach (var contract in contracts)
            {
                contract.ServiceAddress = null;
                contract.ServiceLatitude = null;
                contract.ServiceLongitude = null;
                contract.SpecialClientRequirements = null;
                contract.AccessInstructions = null;
                contract.CaregiverAdditionalNotes = null;
                contract.ClientReviewComments = null;
                dbContext.Contracts.Update(contract);
            }
            if (contracts.Any())
            {
                _logger.LogInformation("GigHardDeleteProcessor: Gig {GigId} - Anonymized {Count} contracts", gigId, contracts.Count);
            }

            // ── 9. Anonymize: TaskSheets (strip signature + dispute reason, keep task structure) ──
            if (relatedOrderIds.Any())
            {
                var taskSheets = await dbContext.TaskSheets
                    .Where(ts => relatedOrderIds.Contains(ts.OrderId))
                    .ToListAsync();
                foreach (var ts in taskSheets)
                {
                    ts.ClientSignatureUrl = null;
                    ts.ClientDisputeReason = null;
                    dbContext.TaskSheets.Update(ts);
                }
                if (taskSheets.Any())
                {
                    _logger.LogInformation("GigHardDeleteProcessor: Gig {GigId} - Anonymized {Count} task sheets", gigId, taskSheets.Count);
                }

                // ── 10. Anonymize: ObservationReports (strip description + photos, keep category/severity) ──
                var observationReports = await dbContext.ObservationReports
                    .Where(or => relatedOrderIds.Contains(or.OrderId))
                    .ToListAsync();
                foreach (var report in observationReports)
                {
                    report.Description = RedactedMarker;
                    report.PhotoUrls = new List<string>();
                    dbContext.ObservationReports.Update(report);
                }
                if (observationReports.Any())
                {
                    _logger.LogInformation("GigHardDeleteProcessor: Gig {GigId} - Anonymized {Count} observation reports", gigId, observationReports.Count);
                }
            }

            // ── 11. Terminate expired subscriptions (mark as Terminated if not already) ──
            var subscriptions = await dbContext.Subscriptions
                .Where(s => s.GigId == gigId
                    && s.Status != SubscriptionStatus.Cancelled
                    && s.Status != SubscriptionStatus.Terminated
                    && s.Status != SubscriptionStatus.Expired)
                .ToListAsync();
            foreach (var sub in subscriptions)
            {
                sub.Status = SubscriptionStatus.Terminated;
                sub.TerminatedAt = DateTime.UtcNow;
                dbContext.Subscriptions.Update(sub);
            }
            if (subscriptions.Any())
            {
                _logger.LogInformation("GigHardDeleteProcessor: Gig {GigId} - Terminated {Count} subscriptions", gigId, subscriptions.Count);
            }

            // ── RETAINED AS-IS (legal/financial obligation): ──
            // - BillingRecords: Tax law requires 5-7 year retention
            // - IncidentReports: Legal liability retention
            // - ClientOrders: Financial audit trail
            // - Disputes: Legal record

            // ── 12. Hard-delete the Gig record itself ──
            dbContext.Gigs.Remove(gig);

            await dbContext.SaveChangesAsync();

            _logger.LogInformation(
                "GigHardDeleteProcessor: Gig {GigId} permanently deleted. GDPR hard-delete complete for caregiver {CaregiverId}",
                gigId, gig.CaregiverId);
        }
    }
}
