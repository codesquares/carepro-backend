using Domain.Entities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Application.Interfaces.Email
{
    public interface IEmailNotificationTrackingService
    {
        /// <summary>
        /// Check if an email has already been sent for a specific notification
        /// </summary>
        Task<bool> HasEmailBeenSentAsync(string notificationId, EmailType emailType);

        /// <summary>
        /// Check if a user has received a batch email for a specific notification type today
        /// </summary>
        Task<bool> HasBatchEmailBeenSentTodayAsync(string userId, string notificationType);

        /// <summary>
        /// Check if a contract reminder has been sent at a specific level
        /// </summary>
        Task<bool> HasContractReminderBeenSentAsync(string relatedEntityId, EmailType reminderLevel);

        /// <summary>
        /// Log that an email has been sent
        /// </summary>
        Task LogEmailSentAsync(EmailNotificationLog log);

        /// <summary>
        /// Log a batch email with multiple notification IDs
        /// </summary>
        Task LogBatchEmailSentAsync(string userId, string notificationType, List<string> notificationIds, string emailSubject);

        /// <summary>
        /// Get notifications that need immediate email sending (NewGig, System updates)
        /// </summary>
        Task<List<Notification>> GetNotificationsForImmediateEmailAsync();

        /// <summary>
        /// Get unread message notifications older than 24 hours that haven't had batch email sent
        /// </summary>
        Task<Dictionary<string, List<Notification>>> GetNotificationsForDailyBatchAsync();

        /// <summary>
        /// Get contract notifications that need reminders based on industry standard timing
        /// </summary>
        Task<List<Notification>> GetContractNotificationsForRemindersAsync();

        /// <summary>
        /// Mark email log as failed with error message
        /// </summary>
        Task MarkEmailAsFailedAsync(string logId, string errorMessage);

        /// <summary>
        /// Get retry candidates for failed emails
        /// </summary>
        Task<List<EmailNotificationLog>> GetFailedEmailsForRetryAsync();

        /// <summary>
        /// Check user's email notification preferences
        /// </summary>
        Task<bool> ShouldSendEmailToUserAsync(string userId, string notificationType);
    }
}