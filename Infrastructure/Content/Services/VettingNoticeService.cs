using Application.Commands;
using Application.DTOs;
using Application.Interfaces.Content;
using Application.Interfaces.Email;
using Domain.Entities;
using Infrastructure.Content.Data;
using Infrastructure.Content.Helpers;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Infrastructure.Content.Services
{
    /// <summary>
    /// Admin-triggered enforcement notice. Nothing here runs on startup, on a timer or on a config change — it is only reached from
    /// <c>POST /api/admin/vetting-notice/send</c>. Config: VettingLockout:EnforcementDate, PoliceClearance:EnforcementDate,
    /// VettingNotice:BatchSize (20), VettingNotice:BatchDelayMs (1000).
    /// </summary>
    public class VettingNoticeService : IVettingNoticeService
    {
        private static readonly TimeSpan StalePending = TimeSpan.FromMinutes(15);

        private readonly CareProDbContext _db;
        private readonly IEmailService _email;
        private readonly IMediator _mediator;
        private readonly IConfiguration _configuration;
        private readonly ILogger<VettingNoticeService> _logger;
        private readonly TimeProvider _time;
        private readonly Func<TimeSpan, Task> _delay;

        public VettingNoticeService(CareProDbContext db, IEmailService email, IMediator mediator, IConfiguration configuration,
            ILogger<VettingNoticeService> logger, TimeProvider? time = null, Func<TimeSpan, Task>? delay = null)
        {
            _db = db; _email = email; _mediator = mediator; _configuration = configuration; _logger = logger;
            _time = time ?? TimeProvider.System;
            _delay = delay ?? (ts => Task.Delay(ts));
        }

        private DateTime NowUtc => _time.GetUtcNow().UtcDateTime;

        // ───────────────────────── plan ─────────────────────────

        private record Plan(DateTime? Lockout, DateTime? Clearance, DateTime? Deadline, string DateKey, string? Problem);
        private record Recipient(Caregiver Caregiver, List<string> Missing);

        private static DateTime? ParseDate(string? raw) =>
            !string.IsNullOrWhiteSpace(raw)
            && DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d) ? d : null;

        private Plan MakePlan()
        {
            var lockout = ParseDate(_configuration["VettingLockout:EnforcementDate"]);
            var clearance = PoliceClearanceSettings.Load(_configuration).EnforcementDateUtc;
            var key = $"L{(lockout.HasValue ? lockout.Value.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture) : "none")}-C{(clearance.HasValue ? clearance.Value.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture) : "none")}";
            if (!lockout.HasValue && !clearance.HasValue)
                return new Plan(null, null, null, key, "Neither VettingLockout:EnforcementDate nor PoliceClearance:EnforcementDate is set, so there is nothing to notify about.");
            var deadline = lockout ?? clearance;
            if (deadline!.Value <= NowUtc)
                return new Plan(lockout, clearance, deadline, key, "The enforcement date has already passed; a notice can no longer give advance warning.");
            return new Plan(lockout, clearance, deadline, key, null);
        }

        private static string NoticeId(string caregiverId, string type, string dateKey) => $"{caregiverId}|{type}|{dateKey}";

        // ───────────────────────── recipients ─────────────────────────

        /// <summary>
        /// Active (not deleted, not deactivated) caregivers with an email who are missing at least one currently-applicable step.
        /// Suspended/deactivated caregivers (Status=false) are excluded: they cannot work or log in, so a deadline notice is noise and
        /// the account may be on hold for reasons we shouldn't write to them about. There is no separate "unapproved caregiver" state in
        /// the data model (admin approval exists only for AdminUsers).
        /// The three profile steps apply only when the lockout date is set; the clearance step only when the clearance date is set.
        /// Address completeness uses the SERVER rule (CaregiverAddressHistoryCoverage.IsComplete), same call readiness makes.
        /// </summary>
        private async Task<List<Recipient>> EvaluateAsync(Plan plan)
        {
            var now = NowUtc;
            var caregivers = (await _db.CareGivers.Where(c => !c.IsDeleted && c.Status).ToListAsync())
                .Where(c => !string.IsNullOrWhiteSpace(c.Email) && !c.Email.EndsWith("@carepro.invalid", StringComparison.OrdinalIgnoreCase))
                .OrderBy(c => c.Id).ToList();
            if (caregivers.Count == 0) return new();
            var ids = caregivers.Select(c => c.Id.ToString()).ToList();

            var guarantorCounts = (await _db.Guarantors.Where(g => ids.Contains(g.CaregiverId)).ToListAsync())
                .GroupBy(g => g.CaregiverId).ToDictionary(g => g.Key, g => g.Count());
            var addresses = (await _db.CaregiverAddressHistories.Where(a => ids.Contains(a.CaregiverId)).ToListAsync())
                .GroupBy(a => a.CaregiverId).ToDictionary(g => g.Key, g => g.ToList());
            var clearances = plan.Clearance.HasValue
                ? (await _db.PoliceClearances.Where(c => ids.Contains(c.CaregiverId!)).ToListAsync())
                    .GroupBy(c => c.CaregiverId ?? string.Empty).ToDictionary(g => g.Key, g => PoliceClearanceLogic.StatusName(PoliceClearanceLogic.Latest(g)))
                : new Dictionary<string, string>();

            var result = new List<Recipient>();
            foreach (var c in caregivers)
            {
                var id = c.Id.ToString();
                var missing = MissingSteps(plan.Lockout.HasValue, plan.Clearance.HasValue, c.CaregiverType != null,
                    guarantorCounts.GetValueOrDefault(id), addresses.TryGetValue(id, out var a) ? a : new List<CaregiverAddressHistory>(),
                    clearances.GetValueOrDefault(id, "none"), now);
                if (missing.Count > 0) result.Add(new Recipient(c, missing));
            }
            return result;
        }

        /// <summary>Pure rule, unit-tested. Pending or approved clearance counts as submitted.</summary>
        public static List<string> MissingSteps(bool lockoutSet, bool clearanceSet, bool typeSet, int guarantorsAdded,
            IEnumerable<CaregiverAddressHistory> addressHistory, string clearanceStatus, DateTime now)
        {
            var missing = new List<string>();
            if (lockoutSet)
            {
                if (!typeSet) missing.Add(VettingSteps.Classification);
                if (guarantorsAdded < 2) missing.Add(VettingSteps.Guarantors);
                if (!CaregiverAddressHistoryCoverage.IsComplete(addressHistory, now)) missing.Add(VettingSteps.AddressHistory);
            }
            if (clearanceSet && clearanceStatus != "pending" && clearanceStatus != "approved") missing.Add(VettingSteps.PoliceClearance);
            return missing;
        }

        private async Task<(List<Recipient> toSend, int alreadySent, int failed, Dictionary<string, VettingNotice> existing)> SelectAsync(Plan plan, string type, bool retryFailed)
        {
            var all = await EvaluateAsync(plan);
            var existing = (await _db.VettingNotices.Where(n => n.NoticeType == type && n.DateKey == plan.DateKey).ToListAsync())
                .ToDictionary(n => n.CaregiverId ?? string.Empty);
            var toSend = new List<Recipient>(); int sent = 0, failed = 0;
            foreach (var r in all)
            {
                if (!existing.TryGetValue(r.Caregiver.Id.ToString(), out var n)) { toSend.Add(r); continue; }
                if (n.Result == VettingNoticeResults.Sent) { sent++; continue; }
                var stalePending = n.Result == VettingNoticeResults.Pending && NowUtc - (n.AttemptedAt ?? DateTime.MinValue) > StalePending;
                if (n.Result == VettingNoticeResults.Failed || stalePending)
                {
                    failed++;
                    if (retryFailed) toSend.Add(r);
                }
                else sent++; // a fresh pending claim is another sender's in-flight work: treat as handled
            }
            return (toSend, sent, failed, existing);
        }

        // ───────────────────────── preview ─────────────────────────

        public async Task<VettingNoticePreviewDTO> PreviewAsync(string type, bool retryFailed = false)
        {
            if (!VettingNoticeTypes.IsValid(type)) throw new VettingNoticeValidationException("type must be initial, reminder_7d or reminder_1d.");
            var plan = MakePlan();
            var dto = new VettingNoticePreviewDTO { Type = type, LockoutDate = plan.Lockout, ClearanceDate = plan.Clearance, Deadline = plan.Deadline, Note = plan.Problem, CanSend = plan.Problem == null };
            if (plan.Problem != null) return dto;

            var (toSend, alreadySent, failed, _) = await SelectAsync(plan, type, retryFailed);
            dto.RecipientCount = toSend.Count; dto.AlreadySentCount = alreadySent; dto.FailedCount = failed;
            foreach (var step in toSend.SelectMany(r => r.Missing)) dto.ByStep[step] = dto.ByStep.GetValueOrDefault(step) + 1;
            dto.Sample = toSend.Take(5).Select(r => new VettingNoticeSampleDTO { FirstName = r.Caregiver.FirstName ?? string.Empty, MissingSteps = r.Missing }).ToList();
            return dto;
        }

        // ───────────────────────── send ─────────────────────────

        public async Task<VettingNoticeSendResultDTO> SendAsync(string type, int confirmRecipientCount, bool retryFailed, AdminIdentity admin)
        {
            if (!VettingNoticeTypes.IsValid(type)) throw new VettingNoticeValidationException("type must be initial, reminder_7d or reminder_1d.");
            if (admin == null || string.IsNullOrWhiteSpace(admin.Id)) throw new UnauthorizedAccessException("Admin identity not found in token.");
            var plan = MakePlan();
            if (plan.Problem != null) throw new VettingNoticeConflictException(plan.Problem);

            // Re-evaluated NOW (reminders therefore skip anyone who has finished since the last notice).
            var (toSend, alreadySent, _, existing) = await SelectAsync(plan, type, retryFailed);
            if (toSend.Count != confirmRecipientCount)
                throw new VettingNoticeConflictException($"The recipient count has changed: confirmation said {confirmRecipientCount} but {toSend.Count} caregivers would be emailed now. Preview again and confirm the new number.");

            var settings = _configuration.GetSection("VettingNotice");
            var batchSize = int.TryParse(settings["BatchSize"], out var bs) && bs > 0 ? bs : 20;
            var delayMs = int.TryParse(settings["BatchDelayMs"], out var dm) && dm >= 0 ? dm : 1000;
            var hubUrl = $"{(_configuration["FrontendUrl"] ?? "https://oncarepro.com").TrimEnd('/')}/app/caregiver/vetting";
            var maxAge = PoliceClearanceSettings.Load(_configuration).MaxAgeMonths;
            DateTime? clearanceDeadline = plan.Clearance.HasValue ? (plan.Lockout.HasValue && plan.Lockout > plan.Clearance ? plan.Lockout : plan.Clearance) : null;

            var result = new VettingNoticeSendResultDTO { Type = type, Attempted = toSend.Count, Skipped = alreadySent };
            for (var i = 0; i < toSend.Count; i++)
            {
                if (i > 0 && i % batchSize == 0 && delayMs > 0) await _delay(TimeSpan.FromMilliseconds(delayMs));
                var r = toSend[i];
                var outcome = await SendOneAsync(r, type, plan, existing, clearanceDeadline, maxAge, hubUrl);
                if (outcome == VettingNoticeResults.Sent) result.Sent++;
                else if (outcome == VettingNoticeResults.Failed) result.Failed++;
                else { result.Skipped++; result.Attempted--; }   // lost the claim race to another sender
            }

            await _db.AdminAuditLogs.AddAsync(new AdminAuditLog
            {
                Id = ObjectId.GenerateNewId(), AdminId = admin.Id, AdminEmail = admin.Email,
                TargetEntityType = "VettingNotice", TargetEntityId = plan.DateKey, Action = "VettingNoticeSent",
                AfterJson = JsonSerializer.Serialize(new { type, attempted = result.Attempted, sent = result.Sent, failed = result.Failed, skipped = result.Skipped, confirmed = confirmRecipientCount, retryFailed }),
                Reason = $"Vetting enforcement notice ({type})", Timestamp = NowUtc
            });
            await _db.SaveChangesAsync();

            // Counts only — never addresses or names.
            _logger.LogInformation("Vetting notice {Type}: sent={Sent} skipped={Skipped} failed={Failed}", type, result.Sent, result.Skipped, result.Failed);
            return result;
        }

        private async Task<string> SendOneAsync(Recipient r, string type, Plan plan, Dictionary<string, VettingNotice> existing,
            DateTime? clearanceDeadline, int maxAge, string hubUrl)
        {
            var cgId = r.Caregiver.Id.ToString();
            VettingNotice entry;
            try
            {
                if (existing.TryGetValue(cgId, out var prior))
                {
                    // Explicit retry of a failed (or abandoned) claim — Result is a concurrency token, so a concurrent retry loses.
                    entry = await _db.VettingNotices.FirstAsync(n => n.Id == prior.Id);
                    entry.Result = VettingNoticeResults.Pending; entry.AttemptedAt = NowUtc; entry.Attempts = (entry.Attempts ?? 0) + 1; entry.Error = null;
                }
                else
                {
                    entry = new VettingNotice { Id = NoticeId(cgId, type, plan.DateKey), CaregiverId = cgId, NoticeType = type, DateKey = plan.DateKey,
                        Result = VettingNoticeResults.Pending, AttemptedAt = NowUtc, Attempts = 1 };
                    _db.VettingNotices.Add(entry);          // atomic claim: duplicate _id => another sender already has this caregiver
                }
                await _db.SaveChangesAsync();
            }
            catch (Exception ex) when (ex is DbUpdateException || ex is DbUpdateConcurrencyException || ex is InvalidOperationException || ex is MongoDB.Driver.MongoException)
            {
                foreach (var e in _db.ChangeTracker.Entries<VettingNotice>().ToList()) e.State = EntityState.Detached;
                return "skipped";
            }

            try
            {
                var msg = VettingNoticeContent.Build(type, r.Caregiver.FirstName ?? string.Empty, r.Missing, plan.Deadline!.Value, clearanceDeadline, maxAge, hubUrl);
                await _email.SendVettingNoticeEmailAsync(r.Caregiver.Email, msg.Subject, msg.Html, msg.Text);
                try
                {
                    await _mediator.Send(new SendNotificationCommand(cgId, "system", NotificationTypes.VettingEnforcementNotice, msg.InAppContent, msg.InAppTitle, type));
                }
                catch (Exception ex) { _logger.LogWarning(ex, "Vetting notice in-app notification failed (email was sent)."); }
                entry.Result = VettingNoticeResults.Sent; entry.SentAt = NowUtc; entry.Error = null;
            }
            catch (Exception ex)
            {
                entry.Result = VettingNoticeResults.Failed; entry.Error = ex.GetType().Name;   // type name only: no address, no message text
            }
            await _db.SaveChangesAsync();
            var final = entry.Result!;
            _db.Entry(entry).State = EntityState.Detached;
            return final;
        }
    }
}
