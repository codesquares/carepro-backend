using Application.Interfaces.Email;
using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Infrastructure.Content.Services
{
    public class EmailNotificationTrackingService : IEmailNotificationTrackingService
    {
        private readonly CareProDbContext _dbContext;
        private readonly ILogger<EmailNotificationTrackingService> _logger;

        // Notification types that should only be sent once
        private readonly HashSet<string> _immediateOnceOnlyTypes = new()
        {
            "NewGig", "SystemNotice", "SystemAlert", "OrderReceived", "OrderConfirmation", 
            "OrderCompleted", "OrderCancelled", "WithdrawalCompleted", 
            "WithdrawalVerified", "WithdrawalRejected", "WithdrawalRequest",
            "PaymentReceived", "PaymentConfirmed", "EarningsAdded", "OrderPayment",
            "PaymentFailed", "RefundProcessed"
        };

        // Message-related notification types for batching
        private readonly HashSet<string> _messageTypes = new()
        {
            "Message", "MessageNotification"
        };

        // Contract-related notification types for reminders
        private readonly HashSet<string> _contractTypes = new()
        {
            "contract_received", "contract_reminder", "contract_accepted", 
            "contract_rejected", "contract_review_requested", "contract_response"
        };

        public EmailNotificationTrackingService(
            CareProDbContext dbContext,
            ILogger<EmailNotificationTrackingService> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        public async Task<bool> HasEmailBeenSentAsync(string notificationId, EmailType emailType)
        {
            try
            {
                return await _dbContext.EmailNotificationLogs
                    .AnyAsync(log => log.NotificationId == notificationId && log.EmailType == emailType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if email has been sent for notification {NotificationId}", notificationId);
                return false;
            }
        }

        public async Task<bool> HasBatchEmailBeenSentTodayAsync(string userId, string notificationType)
        {
            try
            {
                var today = DateTime.UtcNow.Date;
                return await _dbContext.EmailNotificationLogs
                    .AnyAsync(log => log.UserId == userId 
                                  && log.NotificationType == notificationType 
                                  && log.EmailType == EmailType.Batch
                                  && log.SentAt >= today);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking batch email for user {UserId}", userId);
                return false;
            }
        }

        public async Task<bool> HasContractReminderBeenSentAsync(string relatedEntityId, EmailType reminderLevel)
        {
            try
            {
                return await _dbContext.EmailNotificationLogs
                    .AnyAsync(log => log.RelatedEntityId == relatedEntityId 
                                  && log.EmailType == reminderLevel
                                  && _contractTypes.Contains(log.NotificationType));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking contract reminder for entity {EntityId}", relatedEntityId);
                return false;
            }
        }

        public async Task LogEmailSentAsync(EmailNotificationLog log)
        {
            try
            {
                log.Id = ObjectId.GenerateNewId();
                log.SentAt = DateTime.UtcNow;
                await _dbContext.EmailNotificationLogs.AddAsync(log);
                await _dbContext.SaveChangesAsync();
                
                _logger.LogInformation("Email log recorded for user {UserId}, type {NotificationType}", 
                    log.UserId, log.NotificationType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging email sent for user {UserId}", log.UserId);
                throw;
            }
        }

        public async Task LogBatchEmailSentAsync(string userId, string notificationType, 
            List<string> notificationIds, string emailSubject)
        {
            try
            {
                var log = new EmailNotificationLog
                {
                    UserId = userId,
                    NotificationType = notificationType,
                    EmailType = EmailType.Batch,
                    EmailSubject = emailSubject,
                    NotificationIds = notificationIds,
                    Status = EmailStatus.Sent
                };

                await LogEmailSentAsync(log);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging batch email for user {UserId}", userId);
                throw;
            }
        }

        public async Task<List<Notification>> GetNotificationsForImmediateEmailAsync()
        {
            try
            {
                var fiveMinutesAgo = DateTime.UtcNow.AddMinutes(-5);
                
                var notifications = await _dbContext.Notifications
                    .Where(n => _immediateOnceOnlyTypes.Contains(n.Type)
                             && n.CreatedAt >= fiveMinutesAgo
                             && !n.IsRead)
                    .ToListAsync();

                // Filter out notifications that already have emails sent
                var filteredNotifications = new List<Notification>();
                foreach (var notification in notifications)
                {
                    var hasEmailSent = await HasEmailBeenSentAsync(notification.Id.ToString(), EmailType.Immediate);
                    if (!hasEmailSent)
                    {
                        filteredNotifications.Add(notification);
                    }
                }

                return filteredNotifications;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting notifications for immediate email");
                return new List<Notification>();
            }
        }

        public async Task<Dictionary<string, List<Notification>>> GetNotificationsForDailyBatchAsync()
        {
            try
            {
                var twentyFourHoursAgo = DateTime.UtcNow.AddHours(-24);
                
                var unreadMessageNotifications = await _dbContext.Notifications
                    .Where(n => _messageTypes.Contains(n.Type)
                             && !n.IsRead
                             && n.CreatedAt <= twentyFourHoursAgo)
                    .ToListAsync();

                // Group by recipient and filter out users who already received batch email today
                var result = new Dictionary<string, List<Notification>>();
                var groupedByUser = unreadMessageNotifications.GroupBy(n => n.RecipientId);

                foreach (var userGroup in groupedByUser)
                {
                    var userId = userGroup.Key;
                    var hasBatchEmailToday = await HasBatchEmailBeenSentTodayAsync(userId, "Message");
                    
                    if (!hasBatchEmailToday)
                    {
                        result[userId] = userGroup.ToList();
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting notifications for daily batch");
                return new Dictionary<string, List<Notification>>();
            }
        }

        public async Task<List<Notification>> GetContractNotificationsForRemindersAsync()
        {
            try
            {
                var now = DateTime.UtcNow;
                var notifications = new List<Notification>();

                // Get contract notifications that need reminders
                var contractNotifications = await _dbContext.Notifications
                    .Where(n => _contractTypes.Contains(n.Type) && !n.IsRead)
                    .ToListAsync();

                foreach (var notification in contractNotifications)
                {
                    var hoursSinceCreated = (now - notification.CreatedAt).TotalHours;
                    
                    // 24 hour reminder
                    if (hoursSinceCreated >= 24 && hoursSinceCreated < 48)
                    {
                        var hasReminder1 = await HasContractReminderBeenSentAsync(notification.RelatedEntityId, EmailType.Reminder1);
                        if (!hasReminder1)
                        {
                            notifications.Add(notification);
                        }
                    }
                    // 72 hour reminder
                    else if (hoursSinceCreated >= 72 && hoursSinceCreated < 96)
                    {
                        var hasReminder2 = await HasContractReminderBeenSentAsync(notification.RelatedEntityId, EmailType.Reminder2);
                        if (!hasReminder2)
                        {
                            notifications.Add(notification);
                        }
                    }
                    // 7 day final reminder
                    else if (hoursSinceCreated >= 168 && hoursSinceCreated < 192) // 7 days
                    {
                        var hasFinalReminder = await HasContractReminderBeenSentAsync(notification.RelatedEntityId, EmailType.Final);
                        if (!hasFinalReminder)
                        {
                            notifications.Add(notification);
                        }
                    }
                }

                return notifications;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting contract notifications for reminders");
                return new List<Notification>();
            }
        }

        public async Task MarkEmailAsFailedAsync(string logId, string errorMessage)
        {
            try
            {
                var log = await _dbContext.EmailNotificationLogs.FindAsync(ObjectId.Parse(logId));
                if (log != null)
                {
                    log.Status = EmailStatus.Failed;
                    log.ErrorMessage = errorMessage;
                    log.RetryCount++;
                    _dbContext.EmailNotificationLogs.Update(log);
                    await _dbContext.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking email as failed for log {LogId}", logId);
            }
        }

        public async Task<List<EmailNotificationLog>> GetFailedEmailsForRetryAsync()
        {
            try
            {
                return await _dbContext.EmailNotificationLogs
                    .Where(log => log.Status == EmailStatus.Failed 
                               && log.RetryCount < 3
                               && log.SentAt >= DateTime.UtcNow.AddDays(-7)) // Only retry for last 7 days
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting failed emails for retry");
                return new List<EmailNotificationLog>();
            }
        }

        public async Task<bool> ShouldSendEmailToUserAsync(string userId, string notificationType)
        {
            try
            {
                var user = await _dbContext.AppUsers.FirstOrDefaultAsync(u => u.AppUserId.ToString() == userId);
                
                // Check if user exists and has a valid email address
                // Note: User notification preferences can be implemented via NotificationPreferences entity in future
                if (user == null || string.IsNullOrEmpty(user.Email))
                {
                    return false;
                }

                // Default behavior: send emails to all users with valid email addresses
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking email preferences for user {UserId}", userId);
                return true; // Default to sending if we can't check preferences
            }
        }
    }
}