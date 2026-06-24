using Application.Interfaces.Email;
using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Infrastructure.Content.Services
{
    /// <summary>
    /// Sends outreach emails to caregivers when a new care request is posted.
    /// Uses per-caregiver logs for idempotency and bounded retries.
    /// </summary>
    public class CareRequestPostedEmailProcessor : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<CareRequestPostedEmailProcessor> _logger;
        private readonly TimeSpan _interval = TimeSpan.FromMinutes(5);
        private const int MaxRetriesPerCaregiver = 3;

        public CareRequestPostedEmailProcessor(
            IServiceScopeFactory scopeFactory,
            ILogger<CareRequestPostedEmailProcessor> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("CareRequestPostedEmailProcessor started.");

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        await ProcessPendingOutreachAsync(stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error occurred while processing care request outreach emails.");
                    }

                    await Task.Delay(_interval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown
            }

            _logger.LogInformation("CareRequestPostedEmailProcessor: Stopped");
        }

        private async Task ProcessPendingOutreachAsync(CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();

            var dbContext = scope.ServiceProvider.GetRequiredService<CareProDbContext>();
            var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

            var cutoff = DateTime.UtcNow.AddSeconds(-30);
            var pendingRequests = await dbContext.CareRequests
                .Where(cr => cr.DeletedAt == null
                             && cr.CreatedAt < cutoff
                             && cr.AllCaregiversEmailSentAt == null)
                .OrderBy(cr => cr.CreatedAt)
                .Take(20)
                .ToListAsync(stoppingToken);

            if (pendingRequests.Count == 0)
            {
                return;
            }

            var caregivers = await dbContext.CareGivers
                .Where(c => !c.IsDeleted && c.Status && c.IsAvailable && !string.IsNullOrEmpty(c.Email))
                .Select(c => new
                {
                    Id = c.Id.ToString(),
                    c.FirstName,
                    c.Email
                })
                .ToListAsync(stoppingToken);

            if (caregivers.Count == 0)
            {
                _logger.LogWarning("Care request outreach skipped: no eligible caregivers with email found.");
                return;
            }

            foreach (var request in pendingRequests)
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                var requestId = request.Id.ToString();
                var existingLogs = await dbContext.CareRequestBroadcastEmailLogs
                    .Where(l => l.CareRequestId == requestId)
                    .ToListAsync(stoppingToken);

                var logByCaregiver = existingLogs
                    .GroupBy(l => l.CaregiverId)
                    .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.LastAttemptAt).First());

                var sentOrExhausted = 0;

                foreach (var caregiver in caregivers)
                {
                    if (logByCaregiver.TryGetValue(caregiver.Id, out var existing))
                    {
                        if (existing.IsSent || existing.AttemptCount >= MaxRetriesPerCaregiver)
                        {
                            sentOrExhausted++;
                            continue;
                        }
                    }

                    var subject = $"New Care Request Posted - {request.ServiceCategory}";
                    var html = $@"
                        <h3>Hi {caregiver.FirstName},</h3>
                        <p>A client posted a new care request.</p>
                        <div style='background-color:#f8f9fa;padding:15px;border-radius:5px;margin:20px 0;'>
                            <p><strong>Request:</strong> {request.Title}</p>
                            <p><strong>Category:</strong> {request.ServiceCategory}</p>
                            <p><strong>Urgency:</strong> {request.Urgency}</p>
                            <p><strong>Location:</strong> {request.Location ?? "Not specified"}</p>
                            <p><strong>Budget:</strong> {request.Budget ?? "Not specified"}</p>
                        </div>
                        <p>Log in to your dashboard to review and respond if you're interested.</p>
                        <p>- The CarePro Team</p>";

                    var log = existing ?? new CareRequestBroadcastEmailLog
                    {
                        Id = ObjectId.GenerateNewId(),
                        CareRequestId = requestId,
                        CaregiverId = caregiver.Id
                    };

                    try
                    {
                        await emailService.SendGenericNotificationEmailAsync(
                            caregiver.Email,
                            caregiver.FirstName,
                            subject,
                            html);

                        log.IsSent = true;
                        log.SentAt = DateTime.UtcNow;
                        log.LastAttemptAt = DateTime.UtcNow;
                        log.AttemptCount += 1;
                        log.LastError = null;
                        sentOrExhausted++;
                    }
                    catch (Exception ex)
                    {
                        log.IsSent = false;
                        log.LastAttemptAt = DateTime.UtcNow;
                        log.AttemptCount += 1;
                        log.LastError = ex.Message;

                        if (log.AttemptCount >= MaxRetriesPerCaregiver)
                        {
                            sentOrExhausted++;
                        }

                        _logger.LogWarning(
                            ex,
                            "Failed outreach email for CareRequest {CareRequestId} to caregiver {CaregiverId} (attempt {Attempt}).",
                            requestId,
                            caregiver.Id,
                            log.AttemptCount);
                    }

                    if (existing == null)
                    {
                        await dbContext.CareRequestBroadcastEmailLogs.AddAsync(log, stoppingToken);
                    }
                    else
                    {
                        dbContext.CareRequestBroadcastEmailLogs.Update(log);
                    }
                }

                if (sentOrExhausted >= caregivers.Count)
                {
                    request.AllCaregiversEmailSentAt = DateTime.UtcNow;
                    request.UpdatedAt = DateTime.UtcNow;
                    dbContext.CareRequests.Update(request);
                }

                await dbContext.SaveChangesAsync(stoppingToken);
            }
        }
    }
}
