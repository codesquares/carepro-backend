using Application.DTOs;
using Application.Interfaces.Content;
using Domain.Entities;
using Infrastructure.Content.Data;
using Infrastructure.Content.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace Infrastructure.Content.Services
{
    /// <summary>
    /// Caregiver side of the police clearance vetting step. Submission creates a NEW record and supersedes the
    /// previous one (history is never deleted). The caregiver id is supplied by the controller from the JWT only.
    /// </summary>
    public class PoliceClearanceService : IPoliceClearanceService
    {
        private const int MaxSubmissionsPer24Hours = 5;

        private readonly CareProDbContext _db;
        private readonly IPoliceClearanceFileStore _store;
        private readonly IPoliceClearanceNotifier _notifier;
        private readonly IConfiguration _configuration;
        private readonly ILogger<PoliceClearanceService> _logger;
        private readonly TimeProvider _time;

        public PoliceClearanceService(CareProDbContext db, IPoliceClearanceFileStore store, IPoliceClearanceNotifier notifier,
            IConfiguration configuration, ILogger<PoliceClearanceService> logger, TimeProvider? time = null)
        {
            _db = db; _store = store; _notifier = notifier; _configuration = configuration; _logger = logger;
            _time = time ?? TimeProvider.System;
        }

        private DateTime NowUtc => _time.GetUtcNow().UtcDateTime;

        public async Task<PoliceClearanceStatusResponse> GetStatusAsync(string caregiverId)
        {
            var records = await _db.PoliceClearances.Where(c => c.CaregiverId == caregiverId).ToListAsync();
            return BuildStatus(records);
        }

        private PoliceClearanceStatusResponse BuildStatus(List<PoliceClearance> records)
        {
            var settings = PoliceClearanceSettings.Load(_configuration);
            var latest = PoliceClearanceLogic.Latest(records);
            var status = PoliceClearanceLogic.StatusName(latest);
            return new PoliceClearanceStatusResponse
            {
                Status = status,
                ClearanceId = latest?.Id.ToString(),
                IssueDate = latest?.IssueDate,
                SubmittedAt = latest?.SubmittedAt,
                ReviewedAt = latest == null ? null : PoliceClearanceLogic.DecisionAt(latest),
                NameOnDocument = latest?.NameOnDocument,
                ReferenceNumber = latest?.ReferenceNumber,
                RejectionReason = status == "rejected" ? latest!.RejectionReason : null,
                RevokedReason = status == "revoked" ? latest!.RevokedReason : null,
                CanResubmit = PoliceClearanceLogic.CanResubmit(latest),
                MaxAgeMonths = settings.MaxAgeMonths,
                Enforced = settings.IsEnforced(NowUtc),
                EnforcementDate = settings.EnforcementDateUtc,
                Files = latest == null ? new() : PoliceClearanceLogic.FileInfos(latest),
                History = PoliceClearanceLogic.History(records)
            };
        }

        public async Task<PoliceClearanceStatusResponse> SubmitAsync(string caregiverId, SubmitPoliceClearanceRequest request)
        {
            if (!ObjectId.TryParse(caregiverId, out var caregiverOid))
                throw new PoliceClearanceValidationException("Caregiver identity is invalid.");
            var caregiver = await _db.CareGivers.FirstOrDefaultAsync(c => c.Id == caregiverOid)
                ?? throw new KeyNotFoundException("Caregiver not found.");

            var settings = PoliceClearanceSettings.Load(_configuration);
            var now = NowUtc;

            // ── fields ──
            var name = (request.NameOnDocument ?? string.Empty).Trim();
            if (name.Length < 2 || name.Length > 150)
                throw new PoliceClearanceValidationException("Please enter your full name exactly as it appears on the clearance.");
            var reference = (request.ReferenceNumber ?? string.Empty).Trim();
            if (reference.Length < 3 || reference.Length > 80)
                throw new PoliceClearanceValidationException("Please enter the reference number printed on the clearance.");
            if (!DateTime.TryParse(request.IssueDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var issueDate))
                throw new PoliceClearanceValidationException("Please enter the date the clearance was issued.");
            var dateError = PoliceClearanceLogic.CheckIssueDate(issueDate, now, settings.MaxAgeMonths);
            if (dateError != null) throw new PoliceClearanceValidationException(dateError);

            // ── can this caregiver submit now? ──
            var records = await _db.PoliceClearances.Where(c => c.CaregiverId == caregiverId).ToListAsync();
            var latest = PoliceClearanceLogic.Latest(records);
            if (!PoliceClearanceLogic.CanResubmit(latest))
                throw new PoliceClearanceConflictException("Your police clearance is already approved — there is nothing more to submit.");
            if (records.Count(r => (r.SubmittedAt ?? DateTime.MinValue) > now.AddHours(-24)) >= MaxSubmissionsPer24Hours)
                throw new PoliceClearanceConflictException("You've submitted several times today. Please wait a while before trying again.");

            // ── files (content type AND magic bytes) ──
            var validation = PoliceClearanceFileValidator.Validate(
                request.Files.Select(f => new IncomingClearanceFile(f.FileName, f.ContentType, f.Bytes)).ToList(), settings);
            if (!validation.IsValid) throw new PoliceClearanceValidationException(validation.Error!);

            // ── store privately ──
            var stored = new List<PoliceClearanceFile>();
            try
            {
                foreach (var f in validation.Files)
                {
                    var s = await _store.UploadAsync(f.Bytes, f.Extension);
                    stored.Add(new PoliceClearanceFile
                    {
                        PublicId = s.PublicId, ResourceType = s.ResourceType, OriginalFileName = f.SafeFileName,
                        ContentType = f.ContentType, SizeBytes = f.Bytes.LongLength
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Police clearance upload to storage failed for caregiver {CaregiverId}", caregiverId);
                await CleanUpAsync(stored);
                throw new InvalidOperationException("We couldn't store your files just now. Please try again in a moment.");
            }

            // ── persist: new pending record, older ones superseded (history kept) ──
            var record = new PoliceClearance
            {
                Id = ObjectId.GenerateNewId(), CaregiverId = caregiverId, Status = PoliceClearanceStatuses.Pending,
                Files = stored, NameOnDocument = name, ReferenceNumber = reference, IssueDate = issueDate.Date,
                SubmittedAt = now, Superseded = false
            };
            foreach (var old in records.Where(r => r.Superseded != true)) old.Superseded = true;
            _db.PoliceClearances.Add(record);
            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                await CleanUpAsync(stored);
                throw new PoliceClearanceConflictException("Your clearance was just updated. Please refresh the page and try again.");
            }

            await _notifier.NotifySubmittedAsync(record, $"{caregiver.FirstName} {caregiver.LastName}".Trim());
            records.Add(record);
            return BuildStatus(records);
        }

        private async Task CleanUpAsync(IEnumerable<PoliceClearanceFile> files)
        {
            foreach (var f in files)
            {
                var outcome = await _store.DeleteAsync(f.PublicId!, f.ResourceType);
                if (outcome == ClearanceFileDeleteOutcome.Failed)
                    _logger.LogError("Orphaned police clearance file after a failed submission (public id {PublicId}) — needs manual cleanup", f.PublicId);
            }
        }

        public async Task<ClearanceFileContent> GetOwnFileAsync(string caregiverId, string clearanceId, int fileIndex)
        {
            if (!ObjectId.TryParse(clearanceId, out var oid)) throw new KeyNotFoundException("Clearance not found.");
            var record = await _db.PoliceClearances.FirstOrDefaultAsync(c => c.Id == oid)
                ?? throw new KeyNotFoundException("Clearance not found.");
            if (!string.Equals(record.CaregiverId, caregiverId, StringComparison.Ordinal))
                throw new UnauthorizedAccessException("You are not authorised to access this clearance.");
            return await ReadFileAsync(_store, record, fileIndex);
        }

        internal static async Task<ClearanceFileContent> ReadFileAsync(IPoliceClearanceFileStore store, PoliceClearance record, int fileIndex)
        {
            var file = record.Files != null && fileIndex >= 0 && fileIndex < record.Files.Count ? record.Files[fileIndex] : null;
            if (file == null || string.IsNullOrWhiteSpace(file.PublicId)) throw new KeyNotFoundException("File not found.");
            var bytes = await store.DownloadAsync(file.PublicId, file.ResourceType)
                ?? throw new KeyNotFoundException("File not found.");
            return new ClearanceFileContent
            {
                Bytes = bytes, ContentType = file.ContentType ?? "application/octet-stream", FileName = file.OriginalFileName ?? "clearance"
            };
        }
    }
}
