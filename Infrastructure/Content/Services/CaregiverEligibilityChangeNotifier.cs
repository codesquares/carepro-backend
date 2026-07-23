using Application.Commands;
using Application.DTOs;
using Application.Interfaces.Content;
using Application.Interfaces.Email;
using Infrastructure.Content.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using System;
using System.Linq;
using System.Threading.Tasks;
using CareRequestResponseEntity = Domain.Entities.CareRequestResponse;
using GigPriceNegotiationStatus = Domain.Entities.GigPriceNegotiationStatus;

namespace Infrastructure.Content.Services
{
    public class CaregiverEligibilityChangeNotifier : ICaregiverEligibilityChangeNotifier
    {
        private static readonly GigPriceNegotiationStatus[] TerminalNegotiationStatuses = new[]
        {
            GigPriceNegotiationStatus.Agreed,
            GigPriceNegotiationStatus.Rejected,
            GigPriceNegotiationStatus.Expired
        };

        private readonly CareProDbContext _dbContext;
        private readonly IMediator _mediator;
        private readonly IEmailService _emailService;
        private readonly IMarketingSyncService _marketingSyncService;
        private readonly ILogger<CaregiverEligibilityChangeNotifier> _logger;

        public CaregiverEligibilityChangeNotifier(
            CareProDbContext dbContext,
            IMediator mediator,
            IEmailService emailService,
            IMarketingSyncService marketingSyncService,
            ILogger<CaregiverEligibilityChangeNotifier> logger)
        {
            _dbContext = dbContext;
            _mediator = mediator;
            _emailService = emailService;
            _marketingSyncService = marketingSyncService;
            _logger = logger;
        }

        public async Task NotifyIfActiveHireAffectedAsync(string caregiverId)
        {
            // A readiness change is itself marketing-relevant (e.g. nudge to redo
            // verification) independent of whether a hire happens to be in flight.
            await _marketingSyncService.EnqueueCaregiverSyncAsync(caregiverId);

            try
            {
                var activeHires = await _dbContext.CareRequestResponses
                    .Where(r => r.CaregiverId == caregiverId && r.Status == "hired")
                    .ToListAsync();

                if (activeHires.Count == 0) return;

                foreach (var response in activeHires)
                {
                    var negotiation = await _dbContext.GigPriceNegotiations
                        .Where(n => n.CareRequestResponseId == response.Id.ToString())
                        .OrderByDescending(n => n.CreatedAt)
                        .FirstOrDefaultAsync();

                    // No negotiation, or it already reached a terminal state — nothing in flight to warn about.
                    if (negotiation == null || TerminalNegotiationStatuses.Contains(negotiation.Status))
                        continue;

                    if (!ObjectId.TryParse(response.CareRequestId, out var requestOid)) continue;
                    var careRequest = await _dbContext.CareRequests.FindAsync(requestOid);
                    if (careRequest == null) continue;

                    await _mediator.Send(new SendNotificationCommand(
                        RecipientId: careRequest.ClientId,
                        SenderId: "system",
                        Type: NotificationTypes.CaregiverBecameIneligible,
                        Content: $"The caregiver you hired for \"{careRequest.Title}\" is no longer available for this request. You may want to browse other caregivers.",
                        Title: "Caregiver No Longer Available",
                        RelatedEntityId: response.CareRequestId));

                    try
                    {
                        if (ObjectId.TryParse(careRequest.ClientId, out var clientOid))
                        {
                            var client = await _dbContext.Clients.FindAsync(clientOid);
                            if (client != null)
                            {
                                var subject = $"Update needed: \"{careRequest.Title}\"";
                                var html = $@"
                                    <h3>Hi {client.FirstName},</h3>
                                    <p>The caregiver you hired for <strong>{careRequest.Title}</strong> is no longer available for this request.</p>
                                    <p>Log in to browse other caregivers who match your request, or contact support if you have questions about a refund.</p>
                                    <p>— The CarePro Team</p>";
                                await _emailService.SendGenericNotificationEmailAsync(client.Email, client.FirstName, subject, html, preferenceGated: true);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to send caregiver-ineligible email for CareRequest {CareRequestId}", response.CareRequestId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check active-hire impact for caregiver {CaregiverId}", caregiverId);
                // Non-critical — don't let this block the caller's primary write (e.g. verification update).
            }
        }
    }
}
