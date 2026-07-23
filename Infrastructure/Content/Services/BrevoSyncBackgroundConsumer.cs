using Application.DTOs;
using Application.Interfaces.Content;
using Application.Interfaces.Email;
using Domain.Entities;
using Domain.Settings;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Infrastructure.Content.Services
{
    /// <summary>
    /// Singleton hosted service that consumes BrevoSyncJobs and upserts the corresponding
    /// contact + list membership in Brevo. Mirrors PushBackgroundConsumer's shape.
    /// Attribute/list mapping reuses ICaregiverReadinessService rather than duplicating
    /// readiness logic.
    /// </summary>
    public class BrevoSyncBackgroundConsumer : BackgroundService
    {
        private readonly Channel<BrevoSyncJob> _channel;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly BrevoSettings _settings;
        private readonly ILogger<BrevoSyncBackgroundConsumer> _logger;

        public BrevoSyncBackgroundConsumer(
            Channel<BrevoSyncJob> channel,
            IServiceScopeFactory scopeFactory,
            IOptions<BrevoSettings> settings,
            ILogger<BrevoSyncBackgroundConsumer> logger)
        {
            _channel = channel;
            _scopeFactory = scopeFactory;
            _settings = settings.Value;
            _logger = logger;

            if (!_settings.IsConfigured)
            {
                _logger.LogWarning("BrevoSyncBackgroundConsumer: API key not configured — Brevo sync is disabled.");
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (var job in _channel.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    if (job.UserType == "Caregiver")
                        await SyncCaregiverAsync(job.UserId, stoppingToken);
                    else
                        await SyncClientAsync(job.UserId, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled error processing Brevo sync job for {UserType} {UserId}", job.UserType, job.UserId);
                }
            }
        }

        private async Task SyncCaregiverAsync(string caregiverId, CancellationToken ct)
        {
            if (!_settings.IsConfigured || !ObjectId.TryParse(caregiverId, out var oid)) return;

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<CareProDbContext>();
            var readinessService = scope.ServiceProvider.GetRequiredService<ICaregiverReadinessService>();
            var brevoService = scope.ServiceProvider.GetRequiredService<IBrevoService>();
            var trackingService = scope.ServiceProvider.GetRequiredService<IEmailNotificationTrackingService>();

            var caregiver = await dbContext.CareGivers.FindAsync(new object?[] { oid }, ct);
            if (caregiver == null || string.IsNullOrWhiteSpace(caregiver.Email)) return;

            var preference = await dbContext.CaregiverPreferences
                .FirstOrDefaultAsync(p => p.CaregiverId == caregiverId, ct);

            // Reuses the exact same field-level consent evaluation as the transactional email
            // path (single source of truth — see IEmailNotificationTrackingService.HasGeneralMarketingConsent)
            // but applies a deliberately different default when no preference record exists at
            // all: the transactional path defaults to opt-in for legacy compatibility, but data
            // shared with a third party (Brevo) defaults to opt-out until a record exists proving
            // consent. This divergence is intentional, not an oversight — see Brevo pipeline notes.
            if (preference?.NotificationPreferences == null) return;
            if (!trackingService.HasGeneralMarketingConsent(preference.NotificationPreferences)) return;

            // Category-agnostic readiness: general verification + "has any active gig" signal
            // for segmentation purposes (category-specific gating already happens at
            // matching/browse/hire time via ICaregiverReadinessService directly).
            var readiness = await readinessService.GetReadinessAsync(caregiverId, category: null);

            var attributes = new Dictionary<string, object>
            {
                ["IS_VERIFIED"] = readiness.IsIdentityVerified,
                ["READY_FOR_HIRE"] = readiness.IsReady,
                ["HAS_ACTIVE_GIG"] = readiness.HasActiveGig,
                ["FIRSTNAME"] = caregiver.FirstName ?? string.Empty,
                ["LASTNAME"] = caregiver.LastName ?? string.Empty,
            };

            var addToLists = new List<int>();
            var removeFromLists = new List<int>();

            void Toggle(int? listId, bool shouldBeOnList)
            {
                if (listId == null) return;
                if (shouldBeOnList) addToLists.Add(listId.Value);
                else removeFromLists.Add(listId.Value);
            }

            Toggle(_settings.CaregiversUnverifiedListId, !readiness.IsIdentityVerified);
            Toggle(_settings.CaregiversNoGigListId, !readiness.HasActiveGig);

            await brevoService.UpsertContactAsync(caregiver.Email, attributes, addToLists, removeFromLists);

            if (preference != null)
            {
                preference.BrevoLastSyncedAt = DateTime.UtcNow;
                dbContext.CaregiverPreferences.Update(preference);
                await dbContext.SaveChangesAsync(ct);
            }
        }

        private async Task SyncClientAsync(string clientId, CancellationToken ct)
        {
            if (!_settings.IsConfigured || !ObjectId.TryParse(clientId, out var oid)) return;

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<CareProDbContext>();
            var brevoService = scope.ServiceProvider.GetRequiredService<IBrevoService>();
            var trackingService = scope.ServiceProvider.GetRequiredService<IEmailNotificationTrackingService>();

            var client = await dbContext.Clients.FindAsync(new object?[] { oid }, ct);
            if (client == null || string.IsNullOrWhiteSpace(client.Email)) return;

            var preference = await dbContext.ClientPreferences
                .FirstOrDefaultAsync(p => p.ClientId == clientId, ct);

            // See SyncCaregiverAsync — same shared consent evaluation, same deliberate
            // opt-out-by-default divergence for missing preference records.
            if (preference?.NotificationPreferences == null) return;
            if (!trackingService.HasGeneralMarketingConsent(preference.NotificationPreferences)) return;

            var latestRequest = await dbContext.CareRequests
                .Where(r => r.ClientId == clientId && r.DeletedAt == null)
                .OrderByDescending(r => r.CreatedAt)
                .FirstOrDefaultAsync(ct);

            var hasPendingCommitment = await dbContext.BookingCommitments
                .AnyAsync(c => c.ClientId == clientId && c.Status == BookingCommitmentStatus.Pending, ct);

            var lastStatus = latestRequest?.Status?.ToLowerInvariant() ?? "none";

            var attributes = new Dictionary<string, object>
            {
                ["LAST_CARE_REQUEST_STATUS"] = lastStatus,
                ["HAS_PENDING_COMMITMENT_PAYMENT"] = hasPendingCommitment,
                ["FIRSTNAME"] = client.FirstName ?? string.Empty,
                ["LASTNAME"] = client.LastName ?? string.Empty,
            };

            var addToLists = new List<int>();
            var removeFromLists = new List<int>();

            void Toggle(int? listId, bool shouldBeOnList)
            {
                if (listId == null) return;
                if (shouldBeOnList) addToLists.Add(listId.Value);
                else removeFromLists.Add(listId.Value);
            }

            var isAbandoned = lastStatus is "unmatched" or "escalated";
            Toggle(_settings.ClientsAbandonedCareRequestListId, isAbandoned);
            Toggle(_settings.ClientsPendingCommitmentPaymentListId, hasPendingCommitment);

            await brevoService.UpsertContactAsync(client.Email, attributes, addToLists, removeFromLists);

            if (preference != null)
            {
                preference.BrevoLastSyncedAt = DateTime.UtcNow;
                dbContext.ClientPreferences.Update(preference);
                await dbContext.SaveChangesAsync(ct);
            }
        }
    }
}
