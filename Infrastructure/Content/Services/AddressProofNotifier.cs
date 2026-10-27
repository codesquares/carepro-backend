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
using System.Threading.Tasks;

namespace Infrastructure.Content.Services
{
    public class AddressProofNotifier : IAddressProofNotifier
    {
        private readonly CareProDbContext _db;
        private readonly IMediator _mediator;
        private readonly IEmailService _email;
        private readonly IOpsAlertService _ops;
        private readonly ILogger<AddressProofNotifier> _logger;

        public AddressProofNotifier(CareProDbContext db, IMediator mediator, IEmailService email, IOpsAlertService ops,
            ILogger<AddressProofNotifier> logger)
        {
            _db = db; _mediator = mediator; _email = email; _ops = ops; _logger = logger;
        }

        public static (string Subject, string Content) BuildEmail(string kind, string? reason)
        {
            switch (kind)
            {
                case "approved":
                    return ("Your address bill has been approved — CarePro",
                        "Good news — our team has reviewed the power-holding (NEPA) bill you uploaded and approved it.<br /><br />" +
                        "No further action is needed for your address history.");
                case "rejected":
                    return ("Action needed: your address bill could not be approved — CarePro",
                        "We reviewed the power-holding (NEPA) bill you uploaded but could not approve it.<br /><br />" +
                        $"Reason: {PoliceClearanceNotifier.EncodeForEmail(reason)}<br /><br />" +
                        "Please upload a new, clear copy from the Address History page in your dashboard so we can review it again.");
                default:
                    throw new ArgumentException($"Unknown address proof email kind '{kind}'.", nameof(kind));
            }
        }

        public async Task NotifySubmittedAsync(CaregiverAddressHistory record, string caregiverName)
        {
            try
            {
                await _ops.NotifyAdminsAsync(
                    NotificationTypes.AddressProofSubmitted,
                    "Address bill submitted for review",
                    $"{caregiverName} submitted a power-holding bill to confirm their current address.",
                    record.Id.ToString(),
                    alsoEmail: false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to alert admins about address proof {AddressId}", record.Id);
            }
        }

        public Task NotifyApprovedAsync(string caregiverId, string addressId) =>
            NotifyCaregiverAsync(caregiverId, addressId, "approved", null,
                NotificationTypes.AddressProofApproved, "Address bill approved",
                "The bill you uploaded for your current address was reviewed and approved.");

        public Task NotifyRejectedAsync(string caregiverId, string addressId, string reason) =>
            NotifyCaregiverAsync(caregiverId, addressId, "rejected", reason,
                NotificationTypes.AddressProofRejected, "Address bill not approved",
                $"The bill you uploaded could not be approved. Reason: {reason} Please upload a new copy from the Address History page.");

        private async Task NotifyCaregiverAsync(string caregiverId, string addressId, string emailKind, string? reason,
            string type, string title, string inAppContent)
        {
            try
            {
                await _mediator.Send(new SendNotificationCommand(caregiverId, "system", type, inAppContent, title, addressId));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Address proof in-app notification '{Type}' failed for caregiver {CaregiverId}", type, caregiverId);
            }

            try
            {
                if (!ObjectId.TryParse(caregiverId, out var oid)) return;
                var caregiver = await _db.CareGivers.FirstOrDefaultAsync(c => c.Id == oid);
                if (caregiver == null || string.IsNullOrWhiteSpace(caregiver.Email)) return;
                var (subject, content) = BuildEmail(emailKind, reason);
                await _email.SendGenericNotificationEmailAsync(caregiver.Email, caregiver.FirstName ?? "there", subject, content);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Address proof email '{Kind}' failed for caregiver {CaregiverId}", emailKind, caregiverId);
            }
        }
    }
}
