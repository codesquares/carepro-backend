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
    /// <summary>
    /// "Caregiver can't attend a day" notices for package assignments, and the staff workflow that follows them.
    /// NOTHING in here moves money, touches payroll, a task sheet, an assignment, or a matching score — statuses are
    /// records for staff, and any real credit/cover is arranged manually for now. The caregiver's free-text note is
    /// staff-only: it is never put in a client-facing payload, notification or email, and never logged.
    /// </summary>
    public class VisitAbsenceService : IVisitAbsenceService
    {
        private const int MaxAdvanceDays = 60;
        private const int MaxNoteLength = 500;
        private const double AssumedStartHourLagos = 9; // package visits carry no scheduled start time (see report) — keep the order-flow 09:00 convention
        private static readonly TimeZoneInfo Lagos = TimeZoneInfo.FindSystemTimeZoneById("Africa/Lagos");

        private readonly CareProDbContext _db;
        private readonly IMediator _mediator;
        private readonly IEmailService _email;
        private readonly ILogger<VisitAbsenceService> _logger;
        private readonly TimeProvider _clock;

        public VisitAbsenceService(CareProDbContext db, IMediator mediator, IEmailService email, ILogger<VisitAbsenceService> logger, TimeProvider? clock = null)
        {
            _db = db;
            _mediator = mediator;
            _email = email;
            _logger = logger;
            _clock = clock ?? TimeProvider.System;
        }

        private DateTime NowUtc => _clock.GetUtcNow().UtcDateTime;
        private DateTime NowLagos => TimeZoneInfo.ConvertTimeFromUtc(NowUtc, Lagos);
        private static bool IsShortNotice(DateTime day, DateTime nowLagos) => (day.Date.AddHours(AssumedStartHourLagos) - nowLagos).TotalHours < 24;

        // ───────────────────────── caregiver ─────────────────────────

        public async Task<VisitAbsenceNoticeDTO> CreateAsync(string assignmentId, CreateVisitAbsenceRequest request, string caregiverId)
        {
            if (!ObjectId.TryParse(assignmentId, out var assignmentOid))
                throw new ArgumentException("Invalid assignment ID format.");

            var category = (request.Category ?? string.Empty).Trim().ToLowerInvariant();
            if (category.Length == 0)
                throw new ArgumentException("Please choose a reason category.");
            if (Array.IndexOf(AbsenceCategories.All, category) < 0)
                throw new ArgumentException($"Invalid category. Must be one of: {string.Join(", ", AbsenceCategories.All)}");

            var note = (request.Note ?? string.Empty).Trim();
            if (note.Length > MaxNoteLength)
                throw new ArgumentException($"Note must not exceed {MaxNoteLength} characters.");

            if (!DateTime.TryParseExact(request.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                throw new ArgumentException("Date must be in yyyy-MM-dd format.");

            // "Today" is the LAGOS calendar date, never the server's UTC date (23:00 UTC is already tomorrow in Lagos).
            var nowLagos = NowLagos;
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
            // A withdrawn notice frees its day (that is how a wrongly dated notice gets corrected).
            var existing = await _db.VisitAbsenceNotices.Where(n => n.AssignmentId == assignmentId && n.AbsenceDate == dayStart).ToListAsync();
            if (existing.Any(n => n.Status != AbsenceStatuses.Withdrawn))
                throw new InvalidOperationException("You have already given notice for that day.");

            // A visit already submitted for that day means care happened — not an absence.
            var sheetForDay = await _db.TaskSheets.AnyAsync(ts => ts.AssignmentId == assignmentId
                && ts.ScheduledDate == dayStart && ts.Status == "submitted");
            if (sheetForDay)
                throw new InvalidOperationException("A visit for that day has already been submitted.");

            var notice = new VisitAbsenceNotice
            {
                Id = ObjectId.GenerateNewId(),
                AssignmentId = assignmentId,
                PackageRequestId = assignment.PackageRequestId,
                CaregiverId = caregiverId,
                AbsenceDate = dayStart,
                Category = category,
                Reason = note,
                ShortNotice = IsShortNotice(dayStart, nowLagos), // FLAG ONLY
                Status = AbsenceStatuses.Open,
                CreatedAt = NowUtc
            };
            _db.VisitAbsenceNotices.Add(notice);
            await _db.SaveChangesAsync();

            // Deliberately no note text in the log line.
            _logger.LogInformation("Absence notice {NoticeId}: caregiver {CaregiverId}, assignment {AssignmentId}, {Date}, category {Category}, short notice: {Short}",
                notice.Id, caregiverId, assignmentId, request.Date, category, notice.ShortNotice);

            await NotifyCreatedAsync(notice);
            return MapCaregiver(notice, nowLagos);
        }

        public async Task<List<VisitAbsenceNoticeDTO>> GetForAssignmentAsync(string assignmentId, string caregiverId)
        {
            var assignment = await LoadOwnedAssignmentAsync(assignmentId, caregiverId);
            var nowLagos = NowLagos;
            var rows = await _db.VisitAbsenceNotices.Where(n => n.AssignmentId == assignmentId).ToListAsync();
            return rows.OrderByDescending(n => n.AbsenceDate).Select(n => MapCaregiver(n, nowLagos)).ToList();
        }

        public async Task<VisitAbsenceNoticeDTO> WithdrawAsync(string assignmentId, string noticeId, string caregiverId)
        {
            await LoadOwnedAssignmentAsync(assignmentId, caregiverId); // 403 for anyone but the giving caregiver
            var notice = await LoadNoticeAsync(noticeId);
            if (notice.AssignmentId != assignmentId)
                throw new KeyNotFoundException($"Notice '{noticeId}' not found.");
            // Belt and braces: the assignment's caregiver must also be the one who gave this notice.
            if (notice.CaregiverId != caregiverId)
                throw new UnauthorizedAccessException("You are not authorized to withdraw this notice.");

            var nowLagos = NowLagos;
            var current = AbsenceStatuses.Normalize(notice.Status);
            if (current == AbsenceStatuses.Withdrawn)
                throw new InvalidOperationException("This notice has already been withdrawn.");
            if (!CaregiverMayWithdraw(notice, nowLagos))
                throw new InvalidOperationException(
                    current == AbsenceStatuses.CoverArranged || current == AbsenceStatuses.Credited
                        ? "CarePro has already acted on this notice, so it can only be withdrawn by staff. Please contact CarePro."
                        : "A notice can only be withdrawn before the day itself. Please contact CarePro.");

            notice.Status = AbsenceStatuses.Withdrawn; // Status is a concurrency token: if staff changed it first, our update matches nothing
            notice.WithdrawnAt = NowUtc;
            notice.WithdrawnById = caregiverId;
            await SaveStatusTransitionAsync(notice);

            _db.VisitAbsenceNoticeEvents.Add(new VisitAbsenceNoticeEvent
            {
                NoticeId = noticeId, Kind = "withdrawn", FromStatus = current, ToStatus = AbsenceStatuses.Withdrawn,
                ActorType = "caregiver", ActorId = caregiverId, At = NowUtc
            });
            await _db.SaveChangesAsync();

            // Only the request that won the race reaches here, so the client and admins are told exactly once.
            await NotifyWithdrawnAsync(notice, notifyAdmins: true);
            return MapCaregiver(notice, nowLagos);
        }

        public async Task<List<ClientVisitAbsenceNoticeDTO>> GetForPackageRequestAsClientAsync(string packageRequestId, string clientId)
        {
            if (!ObjectId.TryParse(packageRequestId, out var oid))
                throw new ArgumentException("Invalid package request ID format.");
            var request = await _db.PackageRequests.FirstOrDefaultAsync(r => r.Id == oid)
                ?? throw new KeyNotFoundException($"Package request '{packageRequestId}' not found.");
            if (request.ClientId != clientId)
                throw new UnauthorizedAccessException("This package request doesn't belong to you.");

            var rows = await _db.VisitAbsenceNotices.Where(n => n.PackageRequestId == packageRequestId).ToListAsync();
            return rows.OrderByDescending(n => n.AbsenceDate).Select(MapClient).ToList();
        }

        // ───────────────────────── staff ─────────────────────────

        public async Task<List<AdminVisitAbsenceNoticeDTO>> ListForAdminAsync(string? status, bool? shortNotice)
        {
            var rows = await _db.VisitAbsenceNotices.ToListAsync();
            if (!string.IsNullOrWhiteSpace(status))
            {
                if (Array.IndexOf(AbsenceStatuses.All, status) < 0)
                    throw new ArgumentException($"Invalid status. Must be one of: {string.Join(", ", AbsenceStatuses.All)}");
                rows = rows.Where(n => AbsenceStatuses.Normalize(n.Status) == status).ToList();
            }
            if (shortNotice.HasValue)
                rows = rows.Where(n => n.ShortNotice == shortNotice.Value).ToList();

            // Open first, then short-notice first, then soonest day first.
            rows = rows
                .OrderBy(n => AbsenceStatuses.Normalize(n.Status) == AbsenceStatuses.Open ? 0 : 1)
                .ThenBy(n => n.ShortNotice ? 0 : 1)
                .ThenBy(n => n.AbsenceDate)
                .ToList();

            return await MapAdminAsync(rows, includeHistory: false);
        }

        public async Task<AdminVisitAbsenceNoticeDTO> GetForAdminAsync(string noticeId)
        {
            var notice = await LoadNoticeAsync(noticeId);
            return (await MapAdminAsync(new List<VisitAbsenceNotice> { notice }, includeHistory: true)).Single();
        }

        public async Task<AdminVisitAbsenceNoticeDTO> SetStatusAsync(string noticeId, SetAbsenceStatusRequest request, string adminId)
        {
            if (string.IsNullOrWhiteSpace(adminId))
                throw new UnauthorizedAccessException("Admin identity not found.");
            var target = (request.Status ?? string.Empty).Trim().ToLowerInvariant();
            if (Array.IndexOf(AbsenceStatuses.All, target) < 0)
                throw new ArgumentException($"Invalid status. Must be one of: {string.Join(", ", AbsenceStatuses.All)}");
            var note = (request.Note ?? string.Empty).Trim();
            if (note.Length == 0)
                throw new ArgumentException("A note is required.");
            if (note.Length > MaxNoteLength)
                throw new ArgumentException($"Note must not exceed {MaxNoteLength} characters.");

            var notice = await LoadNoticeAsync(noticeId);
            var current = AbsenceStatuses.Normalize(notice.Status);
            if (current == target)
                throw new InvalidOperationException($"This notice is already '{target}'.");
            if (current == AbsenceStatuses.Withdrawn)
                throw new InvalidOperationException("A withdrawn notice can't be reopened. The caregiver can give a new notice for that day.");

            notice.Status = target;
            notice.StatusChangedByAdminId = adminId;
            notice.StatusChangedAt = NowUtc;
            notice.StatusNote = note;
            if (target == AbsenceStatuses.Withdrawn)
            {
                notice.WithdrawnAt = NowUtc;
                notice.WithdrawnById = adminId;
            }
            await SaveStatusTransitionAsync(notice);

            _db.VisitAbsenceNoticeEvents.Add(new VisitAbsenceNoticeEvent
            {
                NoticeId = noticeId, Kind = target == AbsenceStatuses.Withdrawn ? "withdrawn" : "status",
                FromStatus = current, ToStatus = target, ActorType = "admin", ActorId = adminId, Note = note, At = NowUtc
            });
            await _db.SaveChangesAsync();

            _logger.LogInformation("Absence notice {NoticeId}: {From} -> {To} by admin {AdminId}", noticeId, current, target, adminId);

            // closed / no_action_needed / open: nothing is sent automatically.
            if (target == AbsenceStatuses.CoverArranged || target == AbsenceStatuses.Credited)
                await NotifyClientOutcomeAsync(notice, target);
            else if (target == AbsenceStatuses.Withdrawn)
                await NotifyWithdrawnAsync(notice, notifyAdmins: false);

            return await GetForAdminAsync(noticeId);
        }

        public async Task<int> EscalateDueAsync()
        {
            var nowUtc = NowUtc;
            var nowLagos = NowLagos;
            var candidates = (await _db.VisitAbsenceNotices.Where(n => n.EscalatedAt == null).ToListAsync())
                .Where(n => AbsenceStatuses.Normalize(n.Status) == AbsenceStatuses.Open)
                .Where(n => n.AbsenceDate.Date.AddDays(1) > nowLagos                                         // the day isn't over
                         && (n.AbsenceDate.Date.AddHours(AssumedStartHourLagos) - nowLagos).TotalHours <= 24) // and it starts within 24h
                .ToList();

            var claimed = 0;
            foreach (var notice in candidates)
            {
                // Once-only claim: EscalatedAt is a concurrency token, so when two API instances race, exactly one
                // SaveChanges matches the row (EscalatedAt still null) — the other throws and sends nothing.
                notice.EscalatedAt = nowUtc;
                try { await _db.SaveChangesAsync(); }
                catch (DbUpdateConcurrencyException)
                {
                    _db.Entry(notice).State = EntityState.Detached;
                    continue;
                }
                claimed++;

                _db.VisitAbsenceNoticeEvents.Add(new VisitAbsenceNoticeEvent
                {
                    NoticeId = notice.Id.ToString(), Kind = "escalated", FromStatus = AbsenceStatuses.Open, ToStatus = AbsenceStatuses.Open,
                    ActorType = "system", At = nowUtc
                });
                await _db.SaveChangesAsync();
                await NotifyAdminsAsync(notice,
                    "Absence Notice Still Open",
                    $"A caregiver notice for {FormatDay(notice.AbsenceDate)} ({AbsenceCategories.Label(notice.Category)}) is still OPEN and the day is within 24 hours. Someone needs to decide what CarePro does about it.");
            }
            return claimed;
        }

        // ───────────────────────── helpers ─────────────────────────

        private static bool CaregiverMayWithdraw(VisitAbsenceNotice n, DateTime nowLagos)
        {
            var s = AbsenceStatuses.Normalize(n.Status);
            var statusOk = s == AbsenceStatuses.Open || s == AbsenceStatuses.NoActionNeeded || s == AbsenceStatuses.Closed;
            return statusOk && n.AbsenceDate.Date > nowLagos.Date; // strictly before the visit date
        }

        private async Task SaveStatusTransitionAsync(VisitAbsenceNotice notice)
        {
            try { await _db.SaveChangesAsync(); }
            catch (DbUpdateConcurrencyException)
            {
                _db.Entry(notice).State = EntityState.Detached;
                throw new InvalidOperationException("This notice was just changed by someone else. Please refresh and check its current state.");
            }
        }

        private async Task<Assignment> LoadOwnedAssignmentAsync(string assignmentId, string caregiverId)
        {
            if (!ObjectId.TryParse(assignmentId, out var oid))
                throw new ArgumentException("Invalid assignment ID format.");
            var assignment = await _db.Assignments.FirstOrDefaultAsync(a => a.Id == oid)
                ?? throw new KeyNotFoundException($"Assignment '{assignmentId}' not found.");
            if (assignment.CaregiverId != caregiverId)
                throw new UnauthorizedAccessException("You are not authorized to view or change notices for this assignment.");
            return assignment;
        }

        private async Task<VisitAbsenceNotice> LoadNoticeAsync(string noticeId)
        {
            if (!ObjectId.TryParse(noticeId, out var oid))
                throw new ArgumentException("Invalid notice ID format.");
            return await _db.VisitAbsenceNotices.FirstOrDefaultAsync(n => n.Id == oid)
                ?? throw new KeyNotFoundException($"Notice '{noticeId}' not found.");
        }

        private static string FormatDay(DateTime d) => d.ToString("dddd, d MMMM yyyy", CultureInfo.InvariantCulture);

        private async Task<string> CaregiverNameAsync(string caregiverId)
        {
            var cg = await _db.CareGivers.FirstOrDefaultAsync(c => c.Id.ToString() == caregiverId);
            return cg != null ? $"{cg.FirstName} {cg.LastName}".Trim() : "Your caregiver";
        }

        // ── notifications. Client-facing text carries date + category ONLY (never the note). ──

        private async Task NotifyCreatedAsync(VisitAbsenceNotice notice)
        {
            var dateText = FormatDay(notice.AbsenceDate);
            var cat = AbsenceCategories.Label(notice.Category);
            var name = await CaregiverNameAsync(notice.CaregiverId);
            var inApp = notice.ShortNotice
                ? $"{name} has told us they cannot attend on {dateText} (short notice). Reason: {cat}. CarePro will review this and be in touch."
                : $"{name} has told us they cannot attend on {dateText}. Reason: {cat}. Nothing has been charged or refunded for this.";
            var emailBody = $"<p>{WebUtility.HtmlEncode(name)} has told us they cannot attend on <strong>{dateText}</strong>.</p>" +
                            $"<p><strong>Reason:</strong> {WebUtility.HtmlEncode(cat)}</p>" +
                            (notice.ShortNotice
                                ? "<p>This was given with less than 24 hours' notice. CarePro will review it and be in touch.</p>"
                                : "<p>No charge or refund results from this notice. Our team will be in touch if anything needs arranging.</p>");
            await NotifyClientAsync(notice, "Caregiver Unable to Attend", inApp, "Your caregiver can't attend on " + dateText + " - CarePro", emailBody);

            await NotifyAdminsAsync(notice,
                notice.ShortNotice ? "Caregiver Absence — Short Notice" : "Caregiver Absence Notice",
                $"{name} cannot attend on {dateText} ({cat}). Open the notice to see their note and record what CarePro does." +
                (notice.ShortNotice ? " Given with less than 24 hours' notice — review needed." : string.Empty));
        }

        private async Task NotifyClientOutcomeAsync(VisitAbsenceNotice notice, string status)
        {
            var dateText = FormatDay(notice.AbsenceDate);
            var (title, text) = status == AbsenceStatuses.CoverArranged
                ? ("Cover Arranged", $"CarePro is arranging cover for {dateText}, when your caregiver cannot attend. Our team will be in touch with the details.")
                : ("Credit Noted", $"CarePro has noted a credit for {dateText}, when your caregiver cannot attend. Our team will be in touch to confirm how it is applied.");
            await NotifyClientAsync(notice, title, text, title + " - CarePro", $"<p>{WebUtility.HtmlEncode(text)}</p>");
        }

        private async Task NotifyWithdrawnAsync(VisitAbsenceNotice notice, bool notifyAdmins)
        {
            var dateText = FormatDay(notice.AbsenceDate);
            var text = $"The notice that your caregiver could not attend on {dateText} has been withdrawn. Your care for that day goes ahead as normal unless our team tells you otherwise.";
            await NotifyClientAsync(notice, "Notice Withdrawn", text, "Caregiver notice withdrawn - CarePro", $"<p>{WebUtility.HtmlEncode(text)}</p>");
            if (notifyAdmins)
            {
                var name = await CaregiverNameAsync(notice.CaregiverId);
                await NotifyAdminsAsync(notice, "Caregiver Absence Notice Withdrawn", $"{name} withdrew their notice for {dateText}.");
            }
        }

        private async Task NotifyClientAsync(VisitAbsenceNotice notice, string title, string inApp, string emailSubject, string emailHtml)
        {
            try
            {
                var clientId = await PackageReportAccess.GetClientIdAsync(_db, notice.PackageRequestId);
                if (string.IsNullOrEmpty(clientId)) return;
                await _mediator.Send(new SendNotificationCommand(
                    clientId, notice.CaregiverId, NotificationTypes.VisitAbsenceNotified, inApp, title, notice.PackageRequestId));

                var client = await _db.Clients.FirstOrDefaultAsync(c => c.Id.ToString() == clientId);
                if (client != null && !string.IsNullOrEmpty(client.Email))
                    await _email.SendGenericNotificationEmailAsync(client.Email, client.FirstName, emailSubject, emailHtml);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to notify client for absence notice {NoticeId}", notice.Id);
            }
        }

        private async Task NotifyAdminsAsync(VisitAbsenceNotice notice, string title, string content)
        {
            try
            {
                var admins = await AdminRecipients.GetApprovedAsync(_db);
                foreach (var admin in admins)
                {
                    // Admin alerts carry the NOTICE id so the deep link opens that notice on the staff screen.
                    await _mediator.Send(new SendNotificationCommand(
                        admin.Id.ToString(), notice.CaregiverId, NotificationTypes.VisitAbsenceNotified, content, title, notice.Id.ToString()));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to notify admins for absence notice {NoticeId}", notice.Id);
            }
        }

        // ── mapping ──

        private static VisitAbsenceNoticeDTO MapCaregiver(VisitAbsenceNotice n, DateTime nowLagos) => new()
        {
            Id = n.Id.ToString(),
            AssignmentId = n.AssignmentId,
            PackageRequestId = n.PackageRequestId,
            Date = n.AbsenceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Category = AbsenceCategories.Normalize(n.Category),
            CategoryLabel = AbsenceCategories.Label(n.Category),
            Note = string.IsNullOrWhiteSpace(n.Reason) ? null : n.Reason,
            ShortNotice = n.ShortNotice,
            Status = AbsenceStatuses.Normalize(n.Status),
            CanWithdraw = CaregiverMayWithdraw(n, nowLagos),
            CreatedAt = n.CreatedAt
        };

        private static ClientVisitAbsenceNoticeDTO MapClient(VisitAbsenceNotice n)
        {
            var s = AbsenceStatuses.Normalize(n.Status);
            return new ClientVisitAbsenceNoticeDTO
            {
                Id = n.Id.ToString(),
                PackageRequestId = n.PackageRequestId,
                Date = n.AbsenceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Category = AbsenceCategories.Normalize(n.Category),
                CategoryLabel = AbsenceCategories.Label(n.Category),
                ShortNotice = n.ShortNotice,
                Outcome = s == AbsenceStatuses.CoverArranged || s == AbsenceStatuses.Credited || s == AbsenceStatuses.Withdrawn ? s : null,
                CreatedAt = n.CreatedAt
            };
        }

        private async Task<List<AdminVisitAbsenceNoticeDTO>> MapAdminAsync(List<VisitAbsenceNotice> rows, bool includeHistory)
        {
            var requestIds = rows.Select(r => r.PackageRequestId).Distinct().Where(i => ObjectId.TryParse(i, out _)).Select(ObjectId.Parse).ToList();
            var requests = requestIds.Count == 0 ? new List<PackageRequest>()
                : await _db.PackageRequests.Where(pr => requestIds.Contains(pr.Id)).ToListAsync();
            var clientIds = requests.Select(r => r.ClientId).Distinct().ToList();
            var clients = (await _db.Clients.ToListAsync()).Where(c => clientIds.Contains(c.Id.ToString())).ToDictionary(c => c.Id.ToString());
            var cgIds = rows.Select(r => r.CaregiverId).Distinct().ToList();
            var caregivers = (await _db.CareGivers.ToListAsync()).Where(c => cgIds.Contains(c.Id.ToString())).ToDictionary(c => c.Id.ToString());

            var nowUtc = NowUtc;
            var result = new List<AdminVisitAbsenceNoticeDTO>();
            foreach (var n in rows)
            {
                var pr = requests.FirstOrDefault(r => r.Id.ToString() == n.PackageRequestId);
                clients.TryGetValue(pr?.ClientId ?? string.Empty, out var client);
                caregivers.TryGetValue(n.CaregiverId, out var cg);
                var dto = new AdminVisitAbsenceNoticeDTO
                {
                    Id = n.Id.ToString(),
                    AssignmentId = n.AssignmentId,
                    PackageRequestId = n.PackageRequestId,
                    Date = n.AbsenceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    PackageName = pr == null ? "—" : $"{pr.PackageCategory} — {pr.PackageTierLabel}".Trim(' ', '—'),
                    ClientFirstName = client?.FirstName ?? "—",
                    CaregiverId = n.CaregiverId,
                    CaregiverName = cg != null ? $"{cg.FirstName} {cg.LastName}".Trim() : "—",
                    Category = AbsenceCategories.Normalize(n.Category),
                    CategoryLabel = AbsenceCategories.Label(n.Category),
                    Note = n.Reason ?? string.Empty,
                    ShortNotice = n.ShortNotice,
                    Status = AbsenceStatuses.Normalize(n.Status),
                    StatusNote = n.StatusNote,
                    StatusChangedByAdminId = n.StatusChangedByAdminId,
                    StatusChangedAt = n.StatusChangedAt,
                    WithdrawnAt = n.WithdrawnAt,
                    WithdrawnById = n.WithdrawnById,
                    EscalatedAt = n.EscalatedAt,
                    CreatedAt = n.CreatedAt,
                    AgeHours = Math.Round((nowUtc - n.CreatedAt).TotalHours, 1)
                };
                if (includeHistory)
                {
                    var id = n.Id.ToString();
                    var events = (await _db.VisitAbsenceNoticeEvents.Where(e => e.NoticeId == id).ToListAsync()).OrderBy(e => e.At);
                    dto.History = events.Select(e => new AdminVisitAbsenceEventDTO
                    {
                        Kind = e.Kind, FromStatus = e.FromStatus, ToStatus = e.ToStatus, ActorType = e.ActorType, ActorId = e.ActorId, Note = e.Note, At = e.At
                    }).ToList();
                }
                result.Add(dto);
            }
            return result;
        }
    }
}
