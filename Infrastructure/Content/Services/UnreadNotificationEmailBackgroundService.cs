using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Application.Interfaces.Email;
using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

public class UnreadNotificationEmailBackgroundService : BackgroundService
{
    private readonly CareProDbContext _dbContext;
    private readonly IEmailService _emailService;
    private readonly ILogger<UnreadNotificationEmailBackgroundService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromHours(2);

    public UnreadNotificationEmailBackgroundService(
        CareProDbContext dbContext,
        IEmailService emailService,
        ILogger<UnreadNotificationEmailBackgroundService> logger)
    {
        _dbContext = dbContext;
        _emailService = emailService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("UnreadNotificationEmailBackgroundService started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAndSendUnreadNotificationEmailsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while sending unread notification emails.");
            }

            // Wait for the next run (2 hours)
            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task CheckAndSendUnreadNotificationEmailsAsync(CancellationToken stoppingToken)
    {
        var cutoffTime = DateTime.UtcNow.AddHours(-2);

        // Fetch unread notifications older than 2 hours
        var filter = Builders<Notification>.Filter.And(
            Builders<Notification>.Filter.Eq(n => n.IsRead, false),
            Builders<Notification>.Filter.Lte(n => n.CreatedAt, cutoffTime)
        );
        var unreadNotifications = await _dbContext.Notifications
                .Where(x => x.IsRead == false && x.CreatedAt <= cutoffTime)
                .OrderBy(x => x.CreatedAt)
                .ToListAsync();

        
        if (!unreadNotifications.Any())
        {
            _logger.LogInformation("No unread notifications found older than 2 hours.");
            return;
        }

        // Group by recipient
        var groupedByRecipient = unreadNotifications.GroupBy(n => n.RecipientId);

        foreach (var group in groupedByRecipient)
        {
            var recipientId = group.Key;
            var messageCount = group.Count();

            try
            {
                var recipient = await _dbContext.AppUsers
                    .Where(x => x.AppUserId.ToString() == recipientId)
                    .FirstOrDefaultAsync(stoppingToken);

                if (recipient == null || string.IsNullOrEmpty(recipient.Email))
                {
                    _logger.LogWarning($"Recipient with ID {recipientId} not found or has no email.");
                    continue;
                }
                                

                await _emailService.SendNotificationEmailAsync(recipient.Email, recipient.FirstName, messageCount);

                _logger.LogInformation($"Sent unread notification reminder to {recipient.Email}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error sending email for recipient {recipientId}");
            }
        }
    }
}
