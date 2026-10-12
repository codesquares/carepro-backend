using System.Globalization;
using System.Net;
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

namespace Infrastructure.Content.Services
{
    public class VisitAbsenceService : IVisitAbsenceService
    {
        private const int MaxAdvanceDays = 60;
        private const int MaxReasonLength = 500;
        private static readonly TimeZoneInfo Lagos = TimeZoneInfo.FindSystemTimeZoneById("Africa/Lagos");

        private readonly CareProDbContext _db;
        private readonly IMediator _mediator;
        private readonly IEmailService _email;
        private readonly ILogger<VisitAbsenceService> _logger;

        public VisitAbsenceService(CareProDbContext db, IMediator mediator, IEmailService email, ILogger<VisitAbsenceService> logger)
        {
            _db = db;
            _mediator = mediator;
            _email = email;
            _logger = logger;
        }

        public async Task<VisitAbsenceNoticeDTO> CreateAsync(string assignmentId, CreateVisitAbsenceRequest request, string caregiverId)
        {
            if (!ObjectId.TryParse(assignmentId, out var assignmentOid))
                throw new ArgumentException("Invalid assignment ID format.");

            var reason = (request.Reason ?? string.Empty).Trim();
            if (reason.Length == 0)
                throw new ArgumentException("A reason is required.");
            if (reason.Length > MaxReasonLength)
                throw new ArgumentException($"Reason must not exceed {MaxReasonLength} characters.");

            if (!DateTime.TryParseExact(request.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                throw new ArgumentException("Date must be in yyyy-MM-dd format.");

            var nowLagos = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Lagos);
            if (day.Date < nowLagos.Date)
                throw new ArgumentException("The date cannot be in the past.");
            if (day.Date > nowLagos.Date.AddDays(MaxAdvanceDays))
                throw new ArgumentException($"The date cannot be more than {MaxAdvanceDays} days ahead.");

            var assignment = await _db.Assignments.FirstOrDefaultAsync(a => a.Id == assignmentOid)
                ?? throw new KeyNotFoundException($"Assignment '{assignmentId}' not found.");

            if (assignment.CaregiverId != caregiverId)
                throw new UnauthorizedAccessException("You are not authorized to give notice for this assignment.");

            if (assignment.Status != AssignmentStatuses.Accepted)
                throw new InvalidOperationException("This assignment is not active.");

            var dayStart = day.Date;
            if (await _db.VisitAbsenceNotices.AnyAsync(n => n.AssignmentId == assignmentId && n.AbsenceDate == dayStart))
                throw new InvalidOperationException("You have already given notice for that day.");

            // A visit already submitted for that day means care happened — not an absence.
            var sheetForDay = await _db.TaskSheets.AnyAsync(ts => ts.AssignmentId == assignmentId
                && ts.ScheduledDate == dayStart && ts.Status == "submitted");
            if (sheetForDay)
                throw new InvalidOperationException("A visit for that day has already been submitted.");

            // Same 24h convention as the order flow's caregiver cancellation request: measured to 09:00 Lagos.
            var hoursAhead = (dayStart.AddHours(9) - nowLagos).TotalHours;
            var shortNotice = hoursAhead < 24;

            var notice = new VisitAbsenceNotice
            {
                Id = ObjectId.GenerateNewId(),
                AssignmentId = assignmentId,
                PackageRequestId = assignment.PackageRequestId,
                CaregiverId = caregiverId,
                AbsenceDate = dayStart,
                Reason = reason,
                ShortNotice = shortNotice,
                CreatedAt = DateTime.UtcNow
            };
            _db.VisitAbsenceNotices.Add(notice);
            await _db.SaveChangesAsync();

            _logger.LogInformation("Absence notice {NoticeId}: caregiver {CaregiverId}, assignment {AssignmentId}, {Date}, short notice: {Short}",
                notice.Id, caregiverId, assignmentId, request.Date, shortNotice);

            await NotifyAsync(notice, assignment);
            return Map(notice);
        }

        public async Task<List<VisitAbsenceNoticeDTO>> GetForAssignmentAsync(string assignmentId, string caregiverId)
        {
            if (!ObjectId.TryParse(assignmentId, out var oid))
                throw new ArgumentException("Invalid assignment ID format.");
            var assignment = await _db.Assignments.FirstOrDefaultAsync(a => a.Id == oid)
                ?? throw new KeyNotFoundException($"Assignment '{assignmentId}' not found.");
            if (assignment.CaregiverId != caregiverId)
                throw new UnauthorizedAccessException("You are not authorized to view notices for this assignment.");

            var rows = await _db.VisitAbsenceNotices.Where(n => n.AssignmentId == assignmentId).ToListAsync();
            return rows.OrderByDescending(n => n.AbsenceDate).Select(Map).ToList();
        }

        public async Task<List<VisitAbsenceNoticeDTO>> GetForPackageRequestAsClientAsync(string packageRequestId, string clientId)
        {
            if (!ObjectId.TryParse(packageRequestId, out var oid))
                throw new ArgumentException("Invalid package request ID format.");
            var request = await _db.PackageRequests.FirstOrDefaultAsync(r => r.Id == oid)
                ?? throw new KeyNotFoundException($"Package request '{packageRequestId}' not found.");
            if (request.ClientId != clientId)
                throw new UnauthorizedAccessException("This package request doesn't belong to you.");

            var rows = await _db.VisitAbsenceNotices.Where(n => n.PackageRequestId == packageRequestId).ToListAsync();
            return rows.OrderByDescending(n => n.AbsenceDate).Select(Map).ToList();
        }

        private async Task NotifyAsync(VisitAbsenceNotice notice, Assignment assignment)
        {
            var dateText = notice.AbsenceDate.ToString("dddd, d MMMM yyyy", CultureInfo.InvariantCulture);
            string caregiverName = "Your caregiver";
            try
            {
                var caregiver = await _db.CareGivers.FirstOrDefaultAsync(c => c.Id.ToString() == notice.CaregiverId);
                if (caregiver != null) caregiverName = $"{caregiver.FirstName} {caregiver.LastName}".Trim();

                var clientId = await PackageReportAccess.GetClientIdAsync(_db, notice.PackageRequestId);
                if (!string.IsNullOrEmpty(clientId))
                {
                    var inApp = notice.ShortNotice
                        ? $"{caregiverName} has told us they cannot attend on {dateText} (short notice). Reason: {notice.Reason}. CarePro will review this and be in touch."
                        : $"{caregiverName} has told us they cannot attend on {dateText}. Reason: {notice.Reason}. Nothing has been charged or refunded for this.";
                    await _mediator.Send(new SendNotificationCommand(
                        clientId, notice.CaregiverId, NotificationTypes.VisitAbsenceNotified, inApp,
                        "Caregiver Unable to Attend", notice.PackageRequestId));

                    var client = await _db.Clients.FirstOrDefaultAsync(c => c.Id.ToString() == clientId);
                    if (client != null && !string.IsNullOrEmpty(client.Email))
                    {
                        var safeReason = WebUtility.HtmlEncode(notice.Reason);
                        await _email.SendGenericNotificationEmailAsync(
                            client.Email, client.FirstName, "Your caregiver can't attend on " + dateText + " - CarePro",
                            $"<p>{WebUtility.HtmlEncode(caregiverName)} has told us they cannot attend on <strong>{dateText}</strong>.</p>" +
                            $"<p><strong>Reason:</strong> {safeReason}</p>" +
                            (notice.ShortNotice
                                ? "<p>This was given with less than 24 hours' notice. CarePro will review it and be in touch.</p>"
                                : "<p>No charge or refund results from this notice. Our team will be in touch if anything needs arranging.</p>"));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to notify client of absence notice {NoticeId}", notice.Id);
            }

            try
            {
                var admins = await AdminRecipients.GetApprovedAsync(_db);
                var content = $"{caregiverName} cannot attend on {dateText} (assignment {notice.AssignmentId}). Reason: {notice.Reason}." +
                              (notice.ShortNotice ? " Given with less than 24 hours' notice — review needed." : string.Empty);
                foreach (var admin in admins)
                {
                    await _mediator.Send(new SendNotificationCommand(
                        admin.Id.ToString(), notice.CaregiverId, NotificationTypes.VisitAbsenceNotified, content,
                        notice.ShortNotice ? "Caregiver Absence — Short Notice" : "Caregiver Absence Notice", notice.PackageRequestId));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to notify admins of absence notice {NoticeId}", notice.Id);
            }
        }

        private static VisitAbsenceNoticeDTO Map(VisitAbsenceNotice n) => new()
        {
            Id = n.Id.ToString(),
            AssignmentId = n.AssignmentId,
            PackageRequestId = n.PackageRequestId,
            Date = n.AbsenceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Reason = n.Reason,
            ShortNotice = n.ShortNotice,
            CreatedAt = n.CreatedAt
        };
    }
}
