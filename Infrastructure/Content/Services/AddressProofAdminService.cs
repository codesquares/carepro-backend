using Application.DTOs;
using Application.Interfaces.Content;
using Domain.Entities;
using Infrastructure.Content.Data;
using Infrastructure.Content.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Infrastructure.Content.Services
{
    public class AddressProofAdminService : IAddressProofAdminService
    {
        public const int MinReasonLength = 5;
        private const int MaxTextLength = 1000;

        private readonly CareProDbContext _db;
        private readonly IPoliceClearanceFileStore _store;
        private readonly IAddressProofNotifier _notifier;
        private readonly IConfiguration _configuration;
        private readonly TimeProvider _time;

        public AddressProofAdminService(CareProDbContext db, IPoliceClearanceFileStore store, IAddressProofNotifier notifier,
            IConfiguration configuration, TimeProvider? time = null)
        {
            _db = db; _store = store; _notifier = notifier; _configuration = configuration;
            _time = time ?? TimeProvider.System;
        }

        private DateTime NowUtc => _time.GetUtcNow().UtcDateTime;

        // ───────────────────────── list / detail ─────────────────────────

        /// <summary>Every CURRENT address that has a bill submitted (any status). Filtered in memory — nullable comparisons are unreliable in the Mongo provider.</summary>
        private async Task<List<CaregiverAddressHistory>> SubmittedAsync()
        {
            var all = await _db.CaregiverAddressHistories.Where(a => a.ProofStatus != null).ToListAsync();
            return all.Where(a => a.MovedOut == null).ToList();
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

        private static void Fill(AdminAddressProofListItemDTO dto, CaregiverAddressHistory r, Caregiver? cg)
        {
            dto.Id = r.Id.ToString();
            dto.CaregiverId = r.CaregiverId;
            dto.CaregiverName = FullName(cg);
            dto.CaregiverEmail = cg?.Email;
            dto.Status = AddressProofMapper.StatusName(r);
            dto.Address = r.Address;
            dto.MovedIn = r.MovedIn;
            dto.SubmittedAt = r.ProofSubmittedAt;
            dto.BillDate = r.ProofBillDate;
            dto.NameOnBill = r.ProofNameOnBill;
            dto.DecisionAt = r.ProofReviewedAt;
            dto.DecidedByEmail = r.ProofReviewedByAdminEmail;
        }

        public async Task<PaginatedResponse<AdminAddressProofListItemDTO>> ListAsync(string? status, string? search, int page, int pageSize)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);
            var current = await SubmittedAsync();

            if (!string.IsNullOrWhiteSpace(status))
            {
                var wanted = status.Trim().ToLowerInvariant();
                if (wanted is not ("pending" or "approved" or "rejected"))
                    throw new PoliceClearanceValidationException("status must be pending, approved or rejected.");
                current = current.Where(r => AddressProofMapper.StatusName(r) == wanted).ToList();
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

            var ordered = current.OrderBy(r => r.ProofSubmittedAt ?? DateTime.MaxValue).ToList();   // oldest first
            var items = ordered.Skip((page - 1) * pageSize).Take(pageSize).Select(r =>
            {
                var dto = new AdminAddressProofListItemDTO();
                Fill(dto, r, caregivers.GetValueOrDefault(r.CaregiverId ?? string.Empty));
                return dto;
            }).ToList();

            return new PaginatedResponse<AdminAddressProofListItemDTO>
            {
                Items = items, TotalCount = ordered.Count, Page = page, PageSize = pageSize, HasMore = page * pageSize < ordered.Count
            };
        }

        public async Task<int> PendingCountAsync() =>
            (await SubmittedAsync()).Count(r => r.ProofStatus == AddressProofStatuses.Pending);

        private async Task<CaregiverAddressHistory> LoadAsync(string addressId)
        {
            if (!ObjectId.TryParse(addressId, out var oid)) throw new KeyNotFoundException("Address proof not found.");
            var record = await _db.CaregiverAddressHistories.FirstOrDefaultAsync(a => a.Id == oid);
            if (record == null || record.ProofStatus == null) throw new KeyNotFoundException("Address proof not found.");
            return record;
        }

        private static (string first, string last) SplitName(string? full)
        {
            var parts = (full ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return parts.Length == 0 ? ("", "") : parts.Length == 1 ? (parts[0], "") : (parts[0], parts[^1]);
        }

        private async Task<AdminAddressProofDetailDTO> BuildDetailAsync(CaregiverAddressHistory r)
        {
            var settings = AddressProofSettings.Load(_configuration);
            Caregiver? cg = null;
            if (ObjectId.TryParse(r.CaregiverId, out var cgOid))
                cg = await _db.CareGivers.FirstOrDefaultAsync(c => c.Id == cgOid);

            var dto = new AdminAddressProofDetailDTO();
            Fill(dto, r, cg);
            var (first, last) = SplitName(r.ProofNameOnBill);
            dto.ProfileName = FullName(cg);
            dto.NameMatchHint = cg != null && CertificateValidationHelper.ValidateNameMatch(first, last, cg.FirstName, cg.LastName)
                ? "likely_match" : "check_manually";
            dto.MaxBillAgeMonths = settings.MaxBillAgeMonths;
            var file = r.ProofFiles?.FirstOrDefault();
            dto.FileName = file?.OriginalFileName;
            dto.FileContentType = file?.ContentType;
            dto.Checklist = r.ProofChecklist == null ? null : new AddressProofChecklistDTO
            {
                NameMatchesProfile = r.ProofChecklist.NameMatchesProfile, AddressMatchesEntry = r.ProofChecklist.AddressMatchesEntry,
                BillWithinAllowedAge = r.ProofChecklist.BillWithinAllowedAge, ScanLegibleAndUnaltered = r.ProofChecklist.ScanLegibleAndUnaltered
            };
            dto.ReviewNotes = r.ProofReviewNotes;
            dto.RejectionReason = r.ProofRejectionReason;
            dto.CanReview = dto.Status == "pending" && r.MovedOut == null;
            return dto;
        }

        public async Task<AdminAddressProofDetailDTO> GetDetailAsync(string addressId) => await BuildDetailAsync(await LoadAsync(addressId));

        // ───────────────────────── decisions ─────────────────────────

        private static string? CleanOptional(string? text)
        {
            var t = (text ?? string.Empty).Trim();
            if (t.Length == 0) return null;
            return t.Length > MaxTextLength ? t[..MaxTextLength] : t;
        }

        private static void RequireAdmin(AdminIdentity admin)
        {
            if (admin == null || string.IsNullOrWhiteSpace(admin.Id))
                throw new UnauthorizedAccessException("Reviewer identity not found in token.");
        }

        private async Task AuditAsync(AdminIdentity admin, CaregiverAddressHistory record, string action, object? after, string reason)
        {
            await _db.AdminAuditLogs.AddAsync(new AdminAuditLog
            {
                Id = ObjectId.GenerateNewId(), AdminId = admin.Id, AdminEmail = admin.Email,
                TargetEntityType = "CaregiverAddressHistory", TargetEntityId = record.Id.ToString(), TargetUserId = record.CaregiverId,
                Action = action, AfterJson = after == null ? null : JsonSerializer.Serialize(after),
                Reason = reason, Timestamp = NowUtc
            });
        }

        private static void RequireReviewable(CaregiverAddressHistory r)
        {
            if (r.ProofStatus != AddressProofStatuses.Pending)
                throw new PoliceClearanceConflictException($"This bill is already {AddressProofMapper.StatusName(r)}.");
            if (r.MovedOut != null)
                throw new PoliceClearanceConflictException("The caregiver has since marked this address as a past address, so it no longer needs a bill.");
        }

        private async Task SaveDecisionAsync()
        {
            try { await _db.SaveChangesAsync(); }
            catch (DbUpdateConcurrencyException)
            {
                // A reviewer raced another reviewer, or the caregiver edited the address/re-submitted: the first write wins.
                throw new PoliceClearanceConflictException("This bill was just updated by someone else. Please reload and check its current status.");
            }
        }

        public async Task<AdminAddressProofDetailDTO> ApproveAsync(string addressId, AdminIdentity admin, ApproveAddressProofRequest request)
        {
            RequireAdmin(admin);
            var record = await LoadAsync(addressId);
            RequireReviewable(record);

            var c = request?.Checklist;
            var missing = new List<string>();
            if (c?.NameMatchesProfile != true) missing.Add("name matches the caregiver profile");
            if (c?.AddressMatchesEntry != true) missing.Add("address on the bill matches the address entered");
            if (c?.BillWithinAllowedAge != true) missing.Add("bill date present and within the allowed age");
            if (c?.ScanLegibleAndUnaltered != true) missing.Add("scan legible and unaltered");
            if (missing.Count > 0)
                throw new PoliceClearanceValidationException("To approve, confirm every checklist item. Not confirmed: " + string.Join("; ", missing) + ".");

            record.ProofStatus = AddressProofStatuses.Approved;
            record.ProofReviewedAt = NowUtc;
            record.ProofReviewedByAdminId = admin.Id;
            record.ProofReviewedByAdminEmail = admin.Email;
            record.ProofReviewNotes = CleanOptional(request!.Notes);
            record.ProofRejectionReason = null;
            record.ProofChecklist = new AddressProofChecklist
            {
                NameMatchesProfile = true, AddressMatchesEntry = true, BillWithinAllowedAge = true, ScanLegibleAndUnaltered = true
            };
            await AuditAsync(admin, record, "AddressProofApproved", new { checklist = record.ProofChecklist }, record.ProofReviewNotes ?? "Approved");
            await SaveDecisionAsync();

            await _notifier.NotifyApprovedAsync(record.CaregiverId!, record.Id.ToString());
            return await BuildDetailAsync(record);
        }

        public async Task<AdminAddressProofDetailDTO> RejectAsync(string addressId, AdminIdentity admin, string? reason)
        {
            RequireAdmin(admin);
            var cleanReason = (reason ?? string.Empty).Trim();
            if (cleanReason.Length < MinReasonLength)
                throw new PoliceClearanceValidationException($"A reason of at least {MinReasonLength} characters is required to reject a bill.");
            if (cleanReason.Length > MaxTextLength) cleanReason = cleanReason[..MaxTextLength];

            var record = await LoadAsync(addressId);
            RequireReviewable(record);

            record.ProofStatus = AddressProofStatuses.Rejected;
            record.ProofReviewedAt = NowUtc;
            record.ProofReviewedByAdminId = admin.Id;
            record.ProofReviewedByAdminEmail = admin.Email;
            record.ProofRejectionReason = cleanReason;
            await AuditAsync(admin, record, "AddressProofRejected", null, cleanReason);
            await SaveDecisionAsync();

            await _notifier.NotifyRejectedAsync(record.CaregiverId!, record.Id.ToString(), cleanReason);
            return await BuildDetailAsync(record);
        }

        // ───────────────────────── staff file view ─────────────────────────

        public async Task<ClearanceFileContent> GetFileForStaffAsync(string addressId, AdminIdentity admin)
        {
            RequireAdmin(admin);
            var record = await LoadAsync(addressId);
            if (record.ProofFiles == null || record.ProofFiles.Count == 0) throw new KeyNotFoundException("File not found.");

            // Audit first and fail closed: if the entry cannot be written, the file is not served.
            await AuditAsync(admin, record, "AddressProofFileViewed", null, "Staff viewed an address proof file");
            await _db.SaveChangesAsync();

            return await AddressProofService.ReadFileAsync(_store, record);
        }
    }
}
