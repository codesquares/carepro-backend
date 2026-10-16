using Application.Commands;
using Application.DTOs;
using Application.Interfaces.Content;
using Application.Interfaces.Email;
using Domain.Entities;
using Infrastructure.Content.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace Infrastructure.Content.Services
{
    public class PoliceClearanceNotifier : IPoliceClearanceNotifier
    {
        private readonly CareProDbContext _db;
        private readonly IMediator _mediator;
        private readonly IEmailService _email;
        private readonly IOpsAlertService _ops;
        private readonly ILogger<PoliceClearanceNotifier> _logger;

        public PoliceClearanceNotifier(CareProDbContext db, IMediator mediator, IEmailService email, IOpsAlertService ops,
            ILogger<PoliceClearanceNotifier> logger)
        {
            _db = db; _mediator = mediator; _email = email; _ops = ops; _logger = logger;
        }

        // The generic email wrapper renders `content` as HTML inside a <p>, so a "\n" collapses to a space.
        // These emails therefore use real <br /> breaks, and any staff-typed text is HTML-encoded first.
        public static string EncodeForEmail(string? text)
            => WebUtility.HtmlEncode(text ?? string.Empty).Replace("\r\n", "\n").Replace("\n", "<br />");

        public static (string Subject, string Content) BuildEmail(string kind, string? reason)
        {
            switch (kind)
            {
                case "approved":
                    return ("Your police clearance has been approved — CarePro",
                        "Good news — our team has reviewed your police clearance and approved it.<br /><br />" +
                        "No further action is needed for this step.");
                case "rejected":
                    return ("Action needed: your police clearance could not be approved — CarePro",
                        "We reviewed your police clearance but could not approve it.<br /><br />" +
                        $"Reason: {EncodeForEmail(reason)}<br /><br />" +
                        "Please upload a new, clear copy from the Vetting page in your dashboard so we can review it again.");
                case "revoked":
                    return ("Action needed: your police clearance approval was withdrawn — CarePro",
                        "After a further review, we have withdrawn the earlier approval of your police clearance.<br /><br />" +
                        $"Reason: {EncodeForEmail(reason)}<br /><br />" +
                        "Please upload a new police clearance from the Vetting page so we can review it again.");
                default:
                    throw new ArgumentException($"Unknown clearance email kind '{kind}'.", nameof(kind));
            }
        }

        public async Task NotifySubmittedAsync(PoliceClearance clearance, string caregiverName)
        {
            try
            {
                await _ops.NotifyAdminsAsync(
                    NotificationTypes.PoliceClearanceSubmitted,
                    "Police clearance submitted for review",
                    $"{caregiverName} submitted a police clearance for review.",
                    clearance.Id.ToString(),
                    alsoEmail: false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to alert admins about police clearance {ClearanceId}", clearance.Id);
            }
        }

        public Task NotifyApprovedAsync(string caregiverId, string clearanceId) =>
            NotifyCaregiverAsync(caregiverId, clearanceId, "approved", null,
                NotificationTypes.PoliceClearanceApproved, "Police clearance approved",
                "Your police clearance was reviewed and approved.");

        public Task NotifyRejectedAsync(string caregiverId, string clearanceId, string reason) =>
            NotifyCaregiverAsync(caregiverId, clearanceId, "rejected", reason,
                NotificationTypes.PoliceClearanceRejected, "Police clearance not approved",
                $"Your police clearance could not be approved. Reason: {reason} Please upload a new copy from the Vetting page.");

        public Task NotifyRevokedAsync(string caregiverId, string clearanceId, string reason) =>
            NotifyCaregiverAsync(caregiverId, clearanceId, "revoked", reason,
                NotificationTypes.PoliceClearanceRevoked, "Police clearance approval withdrawn",
                $"The approval of your police clearance was withdrawn. Reason: {reason} Please upload a new copy from the Vetting page.");

        private async Task NotifyCaregiverAsync(string caregiverId, string clearanceId, string emailKind, string? reason,
            string type, string title, string inAppContent)
        {
            try
            {
                await _mediator.Send(new SendNotificationCommand(caregiverId, "system", type, inAppContent, title, clearanceId));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Police clearance in-app notification '{Type}' failed for caregiver {CaregiverId}", type, caregiverId);
            }

            try
            {
                if (!ObjectId.TryParse(caregiverId, out var oid)) return;
                var caregiver = await _db.CareGivers.FirstOrDefaultAsync(c => c.Id == oid);
                if (caregiver == null || string.IsNullOrWhiteSpace(caregiver.Email)) return;
                var (subject, content) = BuildEmail(emailKind, reason);
                // Always-Send operational notice (policy rows #41-#43): no marketing gate, no unsubscribe header.
                await _email.SendGenericNotificationEmailAsync(caregiver.Email, caregiver.FirstName ?? "there", subject, content);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Police clearance email '{Kind}' failed for caregiver {CaregiverId}", emailKind, caregiverId);
            }
        }

        public async Task NotifyAssignmentsAtRiskAsync(string caregiverId, string caregiverName, string trigger, string clearanceId,
            IReadOnlyList<string> assignmentLines)
        {
            try
            {
                var content = $"{caregiverName}'s police clearance was {trigger} and they have {assignmentLines.Count} active assignment(s): "
                              + string.Join("; ", assignmentLines)
                              + ". Nothing was cancelled automatically — please decide whether to keep or reassign.";
                await _ops.NotifyAdminsAsync(NotificationTypes.PoliceClearanceAssignmentsAtRisk,
                    "Active assignments need a decision", content, clearanceId, alsoEmail: false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to alert admins about at-risk assignments for caregiver {CaregiverId}", caregiverId);
            }
        }
    }
}
