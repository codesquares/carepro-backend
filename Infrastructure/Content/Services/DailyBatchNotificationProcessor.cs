using Application.Interfaces.Email;
using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Infrastructure.Content.Services
{
    public class DailyBatchNotificationProcessor : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<DailyBatchNotificationProcessor> _logger;
        private readonly TimeSpan _interval = TimeSpan.FromHours(1); // Check every hour, but only send once per day

        public DailyBatchNotificationProcessor(
            IServiceScopeFactory scopeFactory,
            ILogger<DailyBatchNotificationProcessor> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("DailyBatchNotificationProcessor started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Only process during optimal hours (8 AM to 8 PM UTC)
                    var currentHour = DateTime.UtcNow.Hour;
                    if (currentHour >= 8 && currentHour <= 20)
                    {
                        await ProcessDailyBatchNotificationsAsync(stoppingToken);
                    }
                    else
                    {
                        _logger.LogDebug("Skipping batch processing outside optimal hours");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred while processing daily batch notifications.");
                }

                // Wait for the next run (1 hour)
                await Task.Delay(_interval, stoppingToken);
            }
        }

        private async Task ProcessDailyBatchNotificationsAsync(CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();

            var trackingService = scope.ServiceProvider.GetRequiredService<IEmailNotificationTrackingService>();
            var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
            var dbContext = scope.ServiceProvider.GetRequiredService<CareProDbContext>();

            try
            {
                var userNotificationsMap = await trackingService.GetNotificationsForDailyBatchAsync();

                if (!userNotificationsMap.Any())
                {
                    _logger.LogDebug("No message notifications found for daily batch processing.");
                    return;
                }

                _logger.LogInformation("Processing daily batch for {UserCount} users", userNotificationsMap.Count);

                foreach (var userNotifications in userNotificationsMap)
                {
                    var userId = userNotifications.Key;
                    var notifications = userNotifications.Value;

                    try
                    {
                        // Check user preferences
                        var shouldSendEmail = await trackingService.ShouldSendEmailToUserAsync(userId, "Message");
                        if (!shouldSendEmail)
                        {
                            _logger.LogInformation("Skipping batch email for user {UserId} due to preferences", userId);
                            continue;
                        }

                        // Get user details
                        var recipient = await dbContext.AppUsers
                            .FirstOrDefaultAsync(u => u.AppUserId.ToString() == userId, stoppingToken);

                        if (recipient == null || string.IsNullOrEmpty(recipient.Email))
                        {
                            _logger.LogWarning("Recipient {UserId} not found or has no email", userId);
                            continue;
                        }

                        // Group notifications by sender to create meaningful batches
                        var messagesByConversation = await GroupMessagesByConversationAsync(
                            notifications, dbContext, stoppingToken);

                        // Send batch email
                        var emailSubject = $"You have {notifications.Count} unread message{(notifications.Count > 1 ? "s" : "")} - CarePro";
                        
                        await emailService.SendBatchMessageNotificationEmailAsync(
                            recipient.Email, 
                            recipient.FirstName ?? "User", 
                            notifications.Count,
                            messagesByConversation);

                        // Log the batch email
                        var notificationIds = notifications.Select(n => n.Id.ToString()).ToList();
                        await trackingService.LogBatchEmailSentAsync(userId, "Message", notificationIds, emailSubject);

                        _logger.LogInformation("Batch email sent to {Email} for {Count} unread messages", 
                            recipient.Email, notifications.Count);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error sending batch email for user {UserId}", userId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ProcessDailyBatchNotificationsAsync");
                throw;
            }
        }

        private async Task<Dictionary<string, ConversationSummary>> GroupMessagesByConversationAsync(
            List<Notification> notifications, CareProDbContext dbContext, CancellationToken stoppingToken)
        {
            var conversationSummaries = new Dictionary<string, ConversationSummary>();

            foreach (var notification in notifications)
            {
                try
                {
                    // Get the sender information
                    var sender = await dbContext.AppUsers
                        .FirstOrDefaultAsync(u => u.AppUserId.ToString() == notification.SenderId, stoppingToken);

                    if (sender != null)
                    {
                        var senderName = $"{sender.FirstName} {sender.LastName}".Trim();
                        
                        if (conversationSummaries.ContainsKey(notification.SenderId))
                        {
                            conversationSummaries[notification.SenderId].MessageCount++;
                            conversationSummaries[notification.SenderId].LatestMessageTime = 
                                conversationSummaries[notification.SenderId].LatestMessageTime > notification.CreatedAt 
                                    ? conversationSummaries[notification.SenderId].LatestMessageTime 
                                    : notification.CreatedAt;
                        }
                        else
                        {
                            conversationSummaries[notification.SenderId] = new ConversationSummary
                            {
                                SenderName = senderName,
                                MessageCount = 1,
                                LatestMessageTime = notification.CreatedAt
                            };
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error processing notification {NotificationId} for conversation grouping", 
                        notification.Id);
                }
            }

            return conversationSummaries;
        }
    }
}