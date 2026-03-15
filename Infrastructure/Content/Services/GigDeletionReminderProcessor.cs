using Application.DTOs;
using Application.Interfaces.Content;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Content.Services
{
    /// <summary>
    /// Background service that sends reminder notifications to caregivers
    /// when their soft-deleted gigs are approaching the 30-day permanent deletion deadline.
    ///
    /// Runs once every 24 hours. Sends a reminder at 25 days (5 days remaining)
    /// and a final warning at 29 days (1 day remaining).
    /// Also sends a confirmation notification once a gig is permanently deleted
    /// by the GigHardDeleteProcessor.
    /// </summary>
    public class GigDeletionReminderProcessor : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<GigDeletionReminderProcessor> _logger;

        private static readonly TimeSpan RunInterval = TimeSpan.FromHours(24);
        private const int FirstReminderDay = 25;  // 5 days remaining
        private const int FinalReminderDay = 29;   // 1 day remaining
        private const int GracePeriodDays = 30;

        public GigDeletionReminderProcessor(IServiceScopeFactory scopeFactory, ILogger<GigDeletionReminderProcessor> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Let the app fully start before running
            await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("GigDeletionReminderProcessor: Starting reminder cycle");

                try
                {
                    await ProcessRemindersAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "GigDeletionReminderProcessor: Unhandled error during reminder cycle");
                }

                await Task.Delay(RunInterval, stoppingToken);
            }
        }

        private async Task ProcessRemindersAsync(CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<CareProDbContext>();
            var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

            var now = DateTime.UtcNow;

            // Find all soft-deleted gigs still within the grace period
            var softDeletedGigs = await dbContext.Gigs
                .IgnoreQueryFilters()
                .Where(g => g.IsDeleted == true && g.DeletedOn != null)
                .ToListAsync(stoppingToken);

            if (!softDeletedGigs.Any())
            {
                _logger.LogDebug("GigDeletionReminderProcessor: No soft-deleted gigs found");
                return;
            }

            int remindersSent = 0;

            foreach (var gig in softDeletedGigs)
            {
                if (stoppingToken.IsCancellationRequested) break;

                try
                {
                    var daysSinceDeletion = (int)(now - gig.DeletedOn!.Value).TotalDays;
                    var daysRemaining = GracePeriodDays - daysSinceDeletion;
                    var gigId = gig.Id.ToString();

                    // Send first reminder at day 25 (5 days remaining)
                    if (daysSinceDeletion == FirstReminderDay)
                    {
                        await notificationService.CreateNotificationAsync(
                            gig.CaregiverId,
                            "system",
                            NotificationTypes.GigDeletionReminder,
                            $"Your gig \"{gig.Title}\" will be permanently deleted in {daysRemaining} days. Restore it now if you want to keep it.",
                            "Gig Deletion Reminder",
                            gigId);

                        remindersSent++;
                        _logger.LogInformation(
                            "GigDeletionReminderProcessor: Sent 5-day reminder for gig {GigId} to caregiver {CaregiverId}",
                            gigId, gig.CaregiverId);
                    }
                    // Send final warning at day 29 (1 day remaining)
                    else if (daysSinceDeletion == FinalReminderDay)
                    {
                        await notificationService.CreateNotificationAsync(
                            gig.CaregiverId,
                            "system",
                            NotificationTypes.GigDeletionReminder,
                            $"FINAL WARNING: Your gig \"{gig.Title}\" will be permanently deleted tomorrow. This is your last chance to restore it.",
                            "Final Gig Deletion Warning",
                            gigId);

                        remindersSent++;
                        _logger.LogInformation(
                            "GigDeletionReminderProcessor: Sent final warning for gig {GigId} to caregiver {CaregiverId}",
                            gigId, gig.CaregiverId);
                    }
                    // Send permanent deletion confirmation after grace period
                    else if (daysSinceDeletion >= GracePeriodDays)
                    {
                        await notificationService.CreateNotificationAsync(
                            gig.CaregiverId,
                            "system",
                            NotificationTypes.GigPermanentlyDeleted,
                            $"Your gig \"{gig.Title}\" has been permanently deleted as the 30-day restoration period has expired.",
                            "Gig Permanently Deleted",
                            gigId);

                        remindersSent++;
                        _logger.LogInformation(
                            "GigDeletionReminderProcessor: Sent permanent deletion notice for gig {GigId} to caregiver {CaregiverId}",
                            gigId, gig.CaregiverId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "GigDeletionReminderProcessor: Failed to process reminder for gig {GigId}", gig.Id);
                }
            }

            _logger.LogInformation("GigDeletionReminderProcessor: Cycle complete. Reminders sent: {Count}", remindersSent);
        }
    }
}
