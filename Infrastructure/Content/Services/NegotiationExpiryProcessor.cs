using Application.Commands;
using Application.DTOs;
using Application.Interfaces.Email;
using Domain.Entities;
using Infrastructure.Content.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Content.Services
{
    /// <summary>
    /// Background processor that runs every 4 hours and expires stale price negotiations.
    /// A negotiation expires when no action has been taken for 48 hours (based on UpdatedAt).
    ///
    /// NOTE: Booking commitment fees are intentionally NOT expired. Once a client pays the
    /// ₦5,000 commitment fee, access to that gig is retained indefinitely until used.
    /// </summary>
    public class NegotiationExpiryProcessor : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<NegotiationExpiryProcessor> _logger;
        private readonly TimeSpan _interval = TimeSpan.FromHours(4);

        public NegotiationExpiryProcessor(
            IServiceScopeFactory scopeFactory,
            ILogger<NegotiationExpiryProcessor> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("NegotiationExpiryProcessor started.");

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        await ExpireStaleNegotiationsAsync(stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error occurred while expiring stale negotiations.");
                    }

                    await Task.Delay(_interval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown — do not propagate
            }

            _logger.LogInformation("NegotiationExpiryProcessor stopped.");
        }

        private async Task ExpireStaleNegotiationsAsync(CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CareProDbContext>();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

            var nonTerminalStatuses = new[]
            {
                GigPriceNegotiationStatus.Initiated,
                GigPriceNegotiationStatus.ClientProposed,
                GigPriceNegotiationStatus.CaregiverCountered
            };

            var cutoff = DateTime.UtcNow;

            // Find all negotiations whose ExpiresAt has passed and are not yet terminal
            var stale = await db.GigPriceNegotiations
                .Where(n => nonTerminalStatuses.Contains(n.Status) && n.ExpiresAt <= cutoff)
                .ToListAsync(stoppingToken);

            if (stale.Count == 0)
            {
                _logger.LogDebug("NegotiationExpiryProcessor: No stale negotiations found.");
                return;
            }

            _logger.LogInformation(
                "NegotiationExpiryProcessor: Found {Count} stale negotiations to expire.", stale.Count);

            foreach (var negotiation in stale)
            {
                negotiation.Status = GigPriceNegotiationStatus.Expired;
                negotiation.ExpiredAt = DateTime.UtcNow;
                negotiation.UpdatedAt = DateTime.UtcNow;
                negotiation.Version++;

                // Notify client
                await TrySendNotificationAsync(mediator, _logger,
                    recipientId: negotiation.ClientId,
                    senderId: "system",
                    type: NotificationTypes.PriceNegotiationExpired,
                    content: $"Your price negotiation for \"{negotiation.GigTitleSnapshot}\" expired after 48 hours of inactivity. You can start a new negotiation or pay the original price.",
                    title: "Price Negotiation Expired",
                    relatedEntityId: negotiation.Id.ToString());

                // Notify caregiver
                await TrySendNotificationAsync(mediator, _logger,
                    recipientId: negotiation.CaregiverId,
                    senderId: "system",
                    type: NotificationTypes.PriceNegotiationExpired,
                    content: $"Your price negotiation for \"{negotiation.GigTitleSnapshot}\" expired after 48 hours of inactivity.",
                    title: "Price Negotiation Expired",
                    relatedEntityId: negotiation.Id.ToString());

                // Send email to client
                await TrySendExpiryEmailToClientAsync(db, emailService, negotiation, _logger);

                // Send email to caregiver
                await TrySendExpiryEmailToCaregiverAsync(db, emailService, negotiation, _logger);

                _logger.LogInformation(
                    "Negotiation {NegotiationId} expired. ClientId: {ClientId}, CaregiverId: {CaregiverId}",
                    negotiation.Id, negotiation.ClientId, negotiation.CaregiverId);
            }

            await db.SaveChangesAsync(stoppingToken);

            _logger.LogInformation(
                "NegotiationExpiryProcessor: Expired {Count} negotiations.", stale.Count);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Helpers
        // ─────────────────────────────────────────────────────────────────────

        private static async Task TrySendNotificationAsync(
            IMediator mediator,
            ILogger logger,
            string recipientId,
            string senderId,
            string type,
            string content,
            string title,
            string relatedEntityId)
        {
            try
            {
                await mediator.Send(new SendNotificationCommand(
                    RecipientId: recipientId,
                    SenderId: senderId,
                    Type: type,
                    Content: content,
                    Title: title,
                    RelatedEntityId: relatedEntityId));
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Failed to send {Type} notification to {RecipientId}.", type, recipientId);
            }
        }

        private static async Task TrySendExpiryEmailToClientAsync(
            CareProDbContext db,
            IEmailService emailService,
            GigPriceNegotiation negotiation,
            ILogger logger)
        {
            try
            {
                if (!MongoDB.Bson.ObjectId.TryParse(negotiation.ClientId, out var clientOid)) return;
                var client = await db.Clients.FindAsync(clientOid);
                if (client == null) return;

                var subject = $"Your price negotiation for \"{negotiation.GigTitleSnapshot}\" has expired";
                var html = $@"
                    <h3>Price Negotiation Expired</h3>
                    <p>Hello {client.FirstName},</p>
                    <p>Your price negotiation for <strong>{negotiation.GigTitleSnapshot}</strong> has expired
                    because there was no activity for 48 hours.</p>
                    <p>You can start a new negotiation or proceed to pay the original listed price.</p>
                    <p>Your ₦5,000 commitment fee (if applicable) is still valid and will be deducted when you pay.</p>
                    <p>— The CarePro Team</p>";

                await emailService.SendGenericNotificationEmailAsync(client.Email, client.FirstName, subject, html);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Failed to send expiry email to client {ClientId} for negotiation {NegotiationId}.",
                    negotiation.ClientId, negotiation.Id);
            }
        }

        private static async Task TrySendExpiryEmailToCaregiverAsync(
            CareProDbContext db,
            IEmailService emailService,
            GigPriceNegotiation negotiation,
            ILogger logger)
        {
            try
            {
                if (!MongoDB.Bson.ObjectId.TryParse(negotiation.CaregiverId, out var cgOid)) return;
                var caregiver = await db.CareGivers.FindAsync(cgOid);
                if (caregiver == null) return;

                var subject = $"Price negotiation for \"{negotiation.GigTitleSnapshot}\" has expired";
                var html = $@"
                    <h3>Price Negotiation Expired</h3>
                    <p>Hello {caregiver.FirstName},</p>
                    <p>A price negotiation for your gig <strong>{negotiation.GigTitleSnapshot}</strong>
                    has expired after 48 hours of inactivity.</p>
                    <p>The client may choose to initiate a new negotiation or pay the original price.</p>
                    <p>— The CarePro Team</p>";

                await emailService.SendGenericNotificationEmailAsync(caregiver.Email, caregiver.FirstName, subject, html);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Failed to send expiry email to caregiver {CaregiverId} for negotiation {NegotiationId}.",
                    negotiation.CaregiverId, negotiation.Id);
            }
        }
    }
}
