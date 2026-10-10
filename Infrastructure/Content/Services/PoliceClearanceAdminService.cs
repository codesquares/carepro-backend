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
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Infrastructure.Content.Services
{
    public class PoliceClearanceAdminService : IPoliceClearanceAdminService
    {
        public const int MinReasonLength = 5;
        private const int MaxTextLength = 1000;

        private readonly CareProDbContext _db;
        private readonly IPoliceClearanceFileStore _store;
        private readonly IPoliceClearanceNotifier _notifier;
        private readonly IConfiguration _configuration;
        private readonly ILogger<PoliceClearanceAdminService> _logger;
        private readonly TimeProvider _time;

        public PoliceClearanceAdminService(CareProDbContext db, IPoliceClearanceFileStore store, IPoliceClearanceNotifier notifier,
            IConfiguration configuration, ILogger<PoliceClearanceAdminService> logger, TimeProvider? time = null)
        {
            _db = db; _store = store; _notifier = notifier; _configuration = configuration; _logger = logger;
            _time = time ?? TimeProvider.System;
        }

        private DateTime NowUtc => _time.GetUtcNow().UtcDateTime;

        // ───────────────────────── list / detail ─────────────────────────

        private async Task<List<PoliceClearance>> CurrentRecordsAsync()
        {
            // Latest non-superseded record per caregiver. Filtered in memory after a coarse query because
            // the Mongo LINQ provider handles nullable-bool comparisons unreliably.
            var all = await _db.PoliceClearances.Where(c => c.Superseded == null || c.Superseded == false).ToListAsync();
            return all.GroupBy(c => c.CaregiverId ?? string.Empty)
                      .Select(g => PoliceClearanceLogic.Latest(g)!)
                      .ToList();
        }

        private async Task<Dictionary<string, Caregiver>> CaregiversAsync(IEnumerable<string?> ids)
        {
            var oids = ids.Select(i => ObjectId.TryParse(i, out var o) ? o : (ObjectId?)null)
                          .Where(o => o.HasValue).Select(o => o!.Value).Distinct().ToList();
            if (oids.Count == 0) return new();
            var list = await _db.CareGivers.Where(c => oids.Contains(c.Id)).ToListAsync();
            return list.ToDictionary(c => c.Id.ToString());
        }

        private static string FullName(Caregiver? c) =>
            c == null ? "(unknown caregiver)" : string.Join(" ", new[] { c.FirstName, c.LastName }.Where(x => !string.IsNullOrWhiteSpace(x)));

        private static void FillListItem(AdminClearanceListItemDTO dto, PoliceClearance r, Caregiver? cg)
        {
            dto.Id = r.Id.ToString();
            dto.CaregiverId = r.CaregiverId;
            dto.CaregiverName = FullName(cg);
            dto.CaregiverEmail = cg?.Email;
            dto.Status = PoliceClearanceLogic.StatusName(r);
            dto.SubmittedAt = r.SubmittedAt;
            dto.IssueDate = r.IssueDate;
            dto.NameOnDocument = r.NameOnDocument;
            dto.ReferenceNumber = r.ReferenceNumber;
            dto.FileCount = r.Files?.Count ?? 0;
            dto.DecisionAt = PoliceClearanceLogic.DecisionAt(r);
            dto.DecidedByEmail = dto.Status == "revoked" ? (r.RevokedByAdminEmail ?? r.ReviewedByAdminEmail) : r.ReviewedByAdminEmail;
        }

        public async Task<PaginatedResponse<AdminClearanceListItemDTO>> ListAsync(string? status, string? search, int page, int pageSize)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);
            var current = await CurrentRecordsAsync();

            if (!string.IsNullOrWhiteSpace(status))
            {
                var wanted = status.Trim().ToLowerInvariant();
                if (wanted is not ("pending" or "approved" or "rejected" or "revoked"))
                    throw new PoliceClearanceValidationException("status must be pending, approved, rejected or revoked.");
                current = current.Where(r => PoliceClearanceLogic.StatusName(r) == wanted).ToList();
            }

            var caregivers = await CaregiversAsync(current.Select(r => r.CaregiverId));
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                current = current.Where(r =>
                {
                    caregivers.TryGetValue(r.CaregiverId ?? string.Empty, out var cg);
                    return FullName(cg).Contains(term, StringComparison.OrdinalIgnoreCase)
                           || (cg?.Email ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase);
                }).ToList();
            }

            var ordered = current.OrderBy(r => r.SubmittedAt ?? DateTime.MaxValue).ToList();   // oldest first
            var items = ordered.Skip((page - 1) * pageSize).Take(pageSize).Select(r =>
            {
                var dto = new AdminClearanceListItemDTO();
                FillListItem(dto, r, caregivers.GetValueOrDefault(r.CaregiverId ?? string.Empty));
                return dto;
            }).ToList();

            return new PaginatedResponse<AdminClearanceListItemDTO>
            {
                Items = items, TotalCount = ordered.Count, Page = page, PageSize = pageSize, HasMore = page * pageSize < ordered.Count
            };
        }

        public async Task<int> PendingCountAsync() =>
            (await CurrentRecordsAsync()).Count(r => PoliceClearanceLogic.StatusName(r) == "pending");

        private async Task<(PoliceClearance record, List<PoliceClearance> all, bool isLatest)> LoadAsync(string clearanceId)
        {
            if (!ObjectId.TryParse(clearanceId, out var oid)) throw new KeyNotFoundException("Clearance not found.");
            var record = await _db.PoliceClearances.FirstOrDefaultAsync(c => c.Id == oid)
                ?? throw new KeyNotFoundException("Clearance not found.");
            var all = await _db.PoliceClearances.Where(c => c.CaregiverId == record.CaregiverId).ToListAsync();
            var latest = PoliceClearanceLogic.Latest(all);
            return (record, all, latest != null && latest.Id == record.Id);
        }

        private static (string first, string last) SplitName(string? full)
        {
            var parts = (full ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return parts.Length == 0 ? ("", "") : parts.Length == 1 ? (parts[0], "") : (parts[0], parts[^1]);
        }

        private async Task<AdminClearanceDetailDTO> BuildDetailAsync(PoliceClearance r, List<PoliceClearance> all, bool isLatest)
        {
            var settings = PoliceClearanceSettings.Load(_configuration);
            Caregiver? cg = null;
            if (ObjectId.TryParse(r.CaregiverId, out var cgOid))
                cg = await _db.CareGivers.FirstOrDefaultAsync(c => c.Id == cgOid);

            var dto = new AdminClearanceDetailDTO();
            FillListItem(dto, r, cg);
            var (first, last) = SplitName(r.NameOnDocument);
            dto.ProfileName = FullName(cg);
            dto.NameMatchHint = cg != null && CertificateValidationHelper.ValidateNameMatch(first, last, cg.FirstName, cg.LastName)
                ? "likely_match" : "check_manually";
            dto.MaxAgeMonths = settings.MaxAgeMonths;
            dto.Files = PoliceClearanceLogic.FileInfos(r);
            dto.Checklist = r.Checklist == null ? null : new ClearanceChecklistDTO
            {
                NameMatchesProfile = r.Checklist.NameMatchesProfile, IssuedByNigeriaPoliceForce = r.Checklist.IssuedByNigeriaPoliceForce,
                IssueDateWithinAllowedAge = r.Checklist.IssueDateWithinAllowedAge, ReferenceNumberCaptured = r.Checklist.ReferenceNumberCaptured,
                ScanLegibleAndUnaltered = r.Checklist.ScanLegibleAndUnaltered
            };
            dto.ReviewNotes = r.ReviewNotes;
            dto.RejectionReason = r.RejectionReason;
            dto.RevokedReason = r.RevokedReason;
            dto.RevokedByEmail = r.RevokedByAdminEmail;
            dto.RevokedAt = r.RevokedAt;
            dto.IsLatest = isLatest;
            var status = dto.Status;
            dto.CanApprove = isLatest && status == "pending";
            dto.CanReject = isLatest && status == "pending";
            dto.CanRevoke = isLatest && status == "approved";
            dto.History = PoliceClearanceLogic.History(all);
            return dto;
        }

        public async Task<AdminClearanceDetailDTO> GetDetailAsync(string clearanceId)
        {
            var (record, all, isLatest) = await LoadAsync(clearanceId);
            return await BuildDetailAsync(record, all, isLatest);
        }

        // ───────────────────────── decisions ─────────────────────────

        private static string? CleanOptional(string? text)
        {
            var t = (text ?? string.Empty).Trim();
            if (t.Length == 0) return null;
            return t.Length > MaxTextLength ? t[..MaxTextLength] : t;
        }

        private static string RequireReason(string? reason, string action)
        {
            var t = (reason ?? string.Empty).Trim();
            if (t.Length < MinReasonLength)
                throw new PoliceClearanceValidationException($"A reason of at least {MinReasonLength} characters is required to {action}.");
            return t.Length > MaxTextLength ? t[..MaxTextLength] : t;
        }

        private static void RequireAdmin(AdminIdentity admin)
        {
            if (admin == null || string.IsNullOrWhiteSpace(admin.Id))
                throw new UnauthorizedAccessException("Reviewer identity not found in token.");
        }

        private async Task AuditAsync(AdminIdentity admin, PoliceClearance record, string action, object? after, string reason)
        {
            await _db.AdminAuditLogs.AddAsync(new AdminAuditLog
            {
                Id = ObjectId.GenerateNewId(), AdminId = admin.Id, AdminEmail = admin.Email,
                TargetEntityType = "PoliceClearance", TargetEntityId = record.Id.ToString(), TargetUserId = record.CaregiverId,
                Action = action, AfterJson = after == null ? null : JsonSerializer.Serialize(after),
                Reason = reason, Timestamp = NowUtc
            });
        }

        private static (PoliceClearance record, List<PoliceClearance> all) RequireLatest((PoliceClearance, List<PoliceClearance>, bool) loaded)
        {
            if (!loaded.Item3)
                throw new PoliceClearanceConflictException("A newer submission has replaced this one. Please review the caregiver's latest submission.");
            return (loaded.Item1, loaded.Item2);
        }

        private async Task SaveDecisionAsync()
        {
            try { await _db.SaveChangesAsync(); }
            catch (DbUpdateConcurrencyException)
            {
                // Two reviewers (or a reviewer and a resubmission) raced: the first write won, this one must not overwrite it.
                throw new PoliceClearanceConflictException("This clearance was just updated by someone else. Please reload and check its current status.");
            }
        }

        public async Task<AdminClearanceDetailDTO> ApproveAsync(string clearanceId, AdminIdentity admin, ApprovePoliceClearanceRequest request)
        {
            RequireAdmin(admin);
            var (record, all) = RequireLatest(await LoadAsync(clearanceId));
            if (PoliceClearanceLogic.StatusName(record) != "pending")
                throw new PoliceClearanceConflictException($"This clearance is already {PoliceClearanceLogic.StatusName(record)}.");

            var c = request?.Checklist;
            var missing = new List<string>();
            if (c?.NameMatchesProfile != true) missing.Add("name matches the caregiver profile");
            if (c?.IssuedByNigeriaPoliceForce != true) missing.Add("issued by the Nigeria Police Force");
            if (c?.IssueDateWithinAllowedAge != true) missing.Add("issue date present and within the allowed age");
            if (c?.ReferenceNumberCaptured != true) missing.Add("reference number captured");
            if (c?.ScanLegibleAndUnaltered != true) missing.Add("scan legible and unaltered");
            if (missing.Count > 0)
                throw new PoliceClearanceValidationException("To approve, confirm every checklist item. Not confirmed: " + string.Join("; ", missing) + ".");

            var now = NowUtc;
            record.Status = PoliceClearanceStatuses.Approved;
            record.ReviewedAt = now;
            record.ReviewedByAdminId = admin.Id;
            record.ReviewedByAdminEmail = admin.Email;
            record.ReviewNotes = CleanOptional(request!.Notes);
            record.RejectionReason = null;
            record.Checklist = new PoliceClearanceChecklist
            {
                NameMatchesProfile = true, IssuedByNigeriaPoliceForce = true, IssueDateWithinAllowedAge = true,
                ReferenceNumberCaptured = true, ScanLegibleAndUnaltered = true
            };
            await AuditAsync(admin, record, "PoliceClearanceApproved", new { checklist = record.Checklist }, record.ReviewNotes ?? "Approved");
            await SaveDecisionAsync();

            await _notifier.NotifyApprovedAsync(record.CaregiverId!, record.Id.ToString());
            return await BuildDetailAsync(record, all, true);
        }

        public async Task<AdminClearanceDetailDTO> RejectAsync(string clearanceId, AdminIdentity admin, string? reason)
        {
            RequireAdmin(admin);
            var cleanReason = RequireReason(reason, "reject a clearance");
            var (record, all) = RequireLatest(await LoadAsync(clearanceId));
            if (PoliceClearanceLogic.StatusName(record) != "pending")
                throw new PoliceClearanceConflictException($"This clearance is already {PoliceClearanceLogic.StatusName(record)}.");

            var now = NowUtc;
            record.Status = PoliceClearanceStatuses.Rejected;
            record.ReviewedAt = now;
            record.ReviewedByAdminId = admin.Id;
            record.ReviewedByAdminEmail = admin.Email;
            record.RejectionReason = cleanReason;
            await AuditAsync(admin, record, "PoliceClearanceRejected", null, cleanReason);
            await SaveDecisionAsync();

            await _notifier.NotifyRejectedAsync(record.CaregiverId!, record.Id.ToString(), cleanReason);
            return await BuildDetailAsync(record, all, true);
        }

        public async Task<AdminClearanceDetailDTO> RevokeAsync(string clearanceId, AdminIdentity admin, string? reason)
        {
            RequireAdmin(admin);
            var cleanReason = RequireReason(reason, "revoke an approval");
            var (record, all) = RequireLatest(await LoadAsync(clearanceId));
            if (PoliceClearanceLogic.StatusName(record) != "approved")
                throw new PoliceClearanceConflictException(
                    $"Only an approved clearance can be revoked; this one is {PoliceClearanceLogic.StatusName(record)}.");

            var now = NowUtc;
            record.Status = PoliceClearanceStatuses.Revoked;
            record.RevokedAt = now;
            record.RevokedReason = cleanReason;
            record.RevokedByAdminId = admin.Id;
            record.RevokedByAdminEmail = admin.Email;
            await AuditAsync(admin, record, "PoliceClearanceRevoked", null, cleanReason);
            await SaveDecisionAsync();

            await _notifier.NotifyRevokedAsync(record.CaregiverId!, record.Id.ToString(), cleanReason);
            return await BuildDetailAsync(record, all, true);
        }

        // ───────────────────────── staff file view ─────────────────────────

        public async Task<ClearanceFileContent> GetFileForStaffAsync(string clearanceId, int fileIndex, AdminIdentity admin)
        {
            RequireAdmin(admin);
            if (!ObjectId.TryParse(clearanceId, out var oid)) throw new KeyNotFoundException("Clearance not found.");
            var record = await _db.PoliceClearances.FirstOrDefaultAsync(c => c.Id == oid)
                ?? throw new KeyNotFoundException("Clearance not found.");
            if (record.Files == null || fileIndex < 0 || fileIndex >= record.Files.Count)
                throw new KeyNotFoundException("File not found.");

            // Audit first and fail closed: if the entry cannot be written, the file is not served.
            await AuditAsync(admin, record, "PoliceClearanceFileViewed",
                new { fileIndex, fileName = record.Files[fileIndex].OriginalFileName }, "Staff viewed a police clearance file");
            await _db.SaveChangesAsync();

            return await PoliceClearanceService.ReadFileAsync(_store, record, fileIndex);
        }
    }
}
