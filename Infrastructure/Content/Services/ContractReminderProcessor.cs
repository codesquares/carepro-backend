using Application.Interfaces.Email;
using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Infrastructure.Content.Services
{
    public class ContractReminderProcessor : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ContractReminderProcessor> _logger;
        private readonly TimeSpan _interval = TimeSpan.FromHours(6); // Run every 6 hours

        public ContractReminderProcessor(
            IServiceScopeFactory scopeFactory,
            ILogger<ContractReminderProcessor> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("ContractReminderProcessor started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessContractRemindersAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred while processing contract reminders.");
                }

                // Wait for the next run (6 hours)
                await Task.Delay(_interval, stoppingToken);
            }
        }

        private async Task ProcessContractRemindersAsync(CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();

            var trackingService = scope.ServiceProvider.GetRequiredService<IEmailNotificationTrackingService>();
            var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
            var dbContext = scope.ServiceProvider.GetRequiredService<CareProDbContext>();

            try
            {
                var notifications = await trackingService.GetContractNotificationsForRemindersAsync();

                if (!notifications.Any())
                {
                    _logger.LogDebug("No contract notifications found for reminders.");
                    return;
                }

                _logger.LogInformation("Processing {Count} contract reminder notifications", notifications.Count);

                foreach (var notification in notifications)
                {
                    try
                    {
                        // Check user preferences
                        var shouldSendEmail = await trackingService.ShouldSendEmailToUserAsync(
                            notification.RecipientId, notification.Type);

                        if (!shouldSendEmail)
                        {
                            _logger.LogInformation("Skipping contract reminder for user {UserId} due to preferences", 
                                notification.RecipientId);
                            continue;
                        }

                        // Get user details
                        var recipient = await dbContext.AppUsers
                            .FirstOrDefaultAsync(u => u.AppUserId.ToString() == notification.RecipientId, stoppingToken);

                        if (recipient == null || string.IsNullOrEmpty(recipient.Email))
                        {
                            _logger.LogWarning("Recipient {RecipientId} not found or has no email", 
                                notification.RecipientId);
                            continue;
                        }

                        // Determine reminder level based on notification age
                        var reminderLevel = GetReminderLevel(notification.CreatedAt);
                        if (reminderLevel == null)
                        {
                            _logger.LogWarning("Unable to determine reminder level for notification {NotificationId}", 
                                notification.Id);
                            continue;
                        }

                        // Get contract details if available
                        var contractDetails = await GetContractDetailsAsync(
                            notification.RelatedEntityId, dbContext, stoppingToken);

                        // Send the reminder email
                        var emailSubject = GetReminderEmailSubject(reminderLevel.Value);
                        await SendContractReminderEmailAsync(
                            emailService, recipient, notification, reminderLevel.Value, contractDetails, emailSubject);

                        // Log the reminder email
                        var emailLog = new EmailNotificationLog
                        {
                            UserId = notification.RecipientId,
                            NotificationId = notification.Id.ToString(),
                            NotificationType = notification.Type,
                            EmailType = reminderLevel.Value,
                            EmailSubject = emailSubject,
                            RelatedEntityId = notification.RelatedEntityId,
                            Status = EmailStatus.Sent
                        };

                        await trackingService.LogEmailSentAsync(emailLog);

                        _logger.LogInformation("Contract reminder ({ReminderLevel}) sent to {Email} for notification {NotificationId}", 
                            reminderLevel.Value, recipient.Email, notification.Id);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error sending contract reminder for notification {NotificationId}", 
                            notification.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ProcessContractRemindersAsync");
                throw;
            }
        }

        private EmailType? GetReminderLevel(DateTime notificationCreatedAt)
        {
            var hoursSinceCreated = (DateTime.UtcNow - notificationCreatedAt).TotalHours;

            if (hoursSinceCreated >= 24 && hoursSinceCreated < 48)
                return EmailType.Reminder1; // 24 hour reminder

            if (hoursSinceCreated >= 72 && hoursSinceCreated < 96)
                return EmailType.Reminder2; // 72 hour reminder

            if (hoursSinceCreated >= 168 && hoursSinceCreated < 192)
                return EmailType.Final; // 7 day final reminder

            return null;
        }

        private string GetReminderEmailSubject(EmailType reminderLevel)
        {
            return reminderLevel switch
            {
                EmailType.Reminder1 => "Contract Response Reminder - CarePro",
                EmailType.Reminder2 => "Urgent: Contract Response Needed - CarePro",
                EmailType.Final => "Final Reminder: Contract Expiring Soon - CarePro",
                _ => "Contract Reminder - CarePro"
            };
        }

        private async Task<ContractDetails?> GetContractDetailsAsync(
            string contractId, CareProDbContext dbContext, CancellationToken stoppingToken)
        {
            try
            {
                if (string.IsNullOrEmpty(contractId))
                    return null;

                var contract = await dbContext.Contracts
                    .FirstOrDefaultAsync(c => c.Id == contractId, stoppingToken);

                if (contract == null)
                    return null;

                // Get client and gig details if available
                var client = await dbContext.Clients
                    .FirstOrDefaultAsync(c => c.Id.ToString() == contract.ClientId, stoppingToken);

                var gig = await dbContext.Gigs
                    .FirstOrDefaultAsync(g => g.Id.ToString() == contract.GigId, stoppingToken);

                return new ContractDetails
                {
                    ContractId = contract.Id,
                    ClientName = client != null ? $"{client.FirstName} {client.LastName}".Trim() : "Client",
                    GigTitle = gig?.Title ?? "Care Services",
                    CreatedAt = contract.CreatedAt,
                    ExpiryDate = null // TODO: Add ExpiryDate to Contract entity if needed
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error getting contract details for contract {ContractId}", contractId);
                return null;
            }
        }

        private async Task SendContractReminderEmailAsync(
            IEmailService emailService, 
            AppUser recipient, 
            Notification notification, 
            EmailType reminderLevel,
            ContractDetails? contractDetails,
            string subject)
        {
            var reminderMessage = GetReminderMessage(reminderLevel, contractDetails);
            
            await emailService.SendContractReminderEmailAsync(
                recipient.Email,
                recipient.FirstName ?? "User",
                subject,
                reminderMessage,
                contractDetails,
                reminderLevel);
        }

        private string GetReminderMessage(EmailType reminderLevel, ContractDetails? contractDetails)
        {
            var clientName = contractDetails?.ClientName ?? "a client";
            var gigTitle = contractDetails?.GigTitle ?? "care services";

            return reminderLevel switch
            {
                EmailType.Reminder1 => 
                    $"This is a friendly reminder that you have a care contract from {clientName} for {gigTitle} " +
                    "awaiting your response. Please review and respond at your earliest convenience.",

                EmailType.Reminder2 => 
                    $"You still have a pending care contract from {clientName} for {gigTitle}. " +
                    "Please respond soon to secure this opportunity.",

                EmailType.Final => 
                    $"Final reminder: Your care contract from {clientName} for {gigTitle} will expire soon. " +
                    "Please respond today to avoid missing this opportunity.",

                _ => "You have a pending care contract awaiting your response."
            };
        }
    }
}