using Application.DTOs;
using Application.Interfaces.Content;
using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using MongoDB.Bson;

namespace Infrastructure.Content.Services
{
    public class PackageVisitAdminService : IPackageVisitAdminService
    {
        private readonly CareProDbContext _db;
        public PackageVisitAdminService(CareProDbContext db) { _db = db; }

        public async Task<List<PackageVisitAdminRowDTO>> ListAsync(PackageVisitAdminQuery q)
        {
            var checkins = await _db.VisitCheckins
                .Where(c => c.AssignmentId != null && c.AssignmentId != "")
                .ToListAsync();

            IEnumerable<VisitCheckin> filtered = checkins;
            if (q.From.HasValue) filtered = filtered.Where(c => c.CheckinTimestamp >= q.From.Value);
            if (q.To.HasValue) filtered = filtered.Where(c => c.CheckinTimestamp <= q.To.Value);
            if (!string.IsNullOrWhiteSpace(q.CaregiverId)) filtered = filtered.Where(c => c.CaregiverId == q.CaregiverId);

            var mode = (q.Flagged ?? "all").Trim().ToLowerInvariant();
            filtered = mode switch
            {
                "hard" => filtered.Where(c => CheckinFlagReasons.HasHard(c.FlagReasons)),
                "soft" => filtered.Where(c => (c.FlagReasons?.Count ?? 0) > 0 && !CheckinFlagReasons.HasHard(c.FlagReasons)),
                "any" => filtered.Where(c => (c.FlagReasons?.Count ?? 0) > 0),
                _ => filtered,
            };
            if (q.UnclearedOnly) filtered = filtered.Where(c => c.FlagClearedAt == null && (c.FlagReasons?.Count ?? 0) > 0);

            // Uncleared HARD first, then uncleared SOFT, then everything else; newest first inside each group.
            var ordered = filtered
                .OrderBy(c => Rank(c))
                .ThenByDescending(c => c.CheckinTimestamp)
                .Take(Math.Clamp(q.Limit, 1, 500))
                .ToList();

            return await BuildRowsAsync(ordered);
        }

        private static int Rank(VisitCheckin c) =>
            c.FlagClearedAt == null && CheckinFlagReasons.HasHard(c.FlagReasons) ? 0
            : c.FlagClearedAt == null && (c.FlagReasons?.Count ?? 0) > 0 ? 1 : 2;

        public async Task<List<PackageVisitAdminRowDTO>> GetUnclearedHardFlagsAsync(string caregiverId, int year, int month, string? assignmentId = null)
        {
            var start = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
            var end = start.AddMonths(1);
            var list = (await _db.VisitCheckins.Where(c => c.CaregiverId == caregiverId && c.AssignmentId != null && c.AssignmentId != "").ToListAsync())
                .Where(c => (assignmentId == null || c.AssignmentId == assignmentId)
                            && c.CheckinTimestamp >= start && c.CheckinTimestamp < end
                            && c.FlagClearedAt == null && CheckinFlagReasons.HasHard(c.FlagReasons))
                .OrderBy(c => c.CheckinTimestamp).ToList();
            return await BuildRowsAsync(list);
        }

        public async Task<PackageVisitAdminRowDTO> ClearFlagAsync(string checkinId, string adminId, string? adminEmail, string note)
        {
            if (string.IsNullOrWhiteSpace(adminId)) throw new ArgumentException("Admin identity is required.");
            if (string.IsNullOrWhiteSpace(note) || note.Trim().Length < 3) throw new ArgumentException("A note is required to clear a flag.");
            if (!ObjectId.TryParse(checkinId, out var oid)) throw new ArgumentException("Invalid check-in id.");

            var checkin = await _db.VisitCheckins.FirstOrDefaultAsync(c => c.Id == oid)
                ?? throw new KeyNotFoundException("Check-in not found.");
            if ((checkin.FlagReasons?.Count ?? 0) == 0)
                throw new InvalidOperationException("This check-in has no flag to clear.");

            if (checkin.FlagClearedAt == null)
            {
                var now = DateTime.UtcNow;
                checkin.FlagClearedAt = now;
                checkin.FlagClearedByAdminId = adminId;          // from the JWT, never the body
                checkin.FlagClearedByAdminEmail = adminEmail;
                checkin.FlagClearNote = note.Trim();
                _db.VisitCheckins.Update(checkin);
                await _db.AdminAuditLogs.AddAsync(new AdminAuditLog
                {
                    Id = ObjectId.GenerateNewId(),
                    AdminId = adminId, AdminEmail = adminEmail,
                    TargetEntityType = "VisitCheckin", TargetEntityId = checkin.Id.ToString(), TargetUserId = checkin.CaregiverId,
                    Action = "CheckinFlagCleared",
                    AfterJson = System.Text.Json.JsonSerializer.Serialize(new { reasons = checkin.FlagReasons }),
                    Reason = note.Trim(), Timestamp = now,
                });
                await _db.SaveChangesAsync();
            }
            return (await BuildRowsAsync(new List<VisitCheckin> { checkin }))[0];
        }

        private async Task<List<PackageVisitAdminRowDTO>> BuildRowsAsync(List<VisitCheckin> items)
        {
            if (items.Count == 0) return new();

            static List<ObjectId> Oids(IEnumerable<string?> ids) => ids
                .Select(i => ObjectId.TryParse(i, out var o) ? o : (ObjectId?)null).Where(o => o.HasValue).Select(o => o!.Value).Distinct().ToList();

            var sheetOids = Oids(items.Select(i => i.TaskSheetId));
            var sheets = (await _db.TaskSheets.Where(t => sheetOids.Contains(t.Id)).ToListAsync()).ToDictionary(t => t.Id.ToString());
            var assignOids = Oids(items.Select(i => i.AssignmentId));
            var assignments = (await _db.Assignments.Where(a => assignOids.Contains(a.Id)).ToListAsync()).ToDictionary(a => a.Id.ToString());
            var cgOids = Oids(items.Select(i => i.CaregiverId));
            var caregivers = (await _db.CareGivers.Where(c => cgOids.Contains(c.Id)).ToListAsync()).ToDictionary(c => c.Id.ToString(), c => $"{c.FirstName} {c.LastName}".Trim());
            var clientOids = Oids(assignments.Values.Select(a => a.ClientId));
            var clients = (await _db.Clients.Where(c => clientOids.Contains(c.Id)).ToListAsync()).ToDictionary(c => c.Id.ToString(), c => $"{c.FirstName} {c.LastName}".Trim());
            var sheetIds = sheets.Keys.ToList();
            var openDisputeSheets = (await _db.Disputes.Where(d => d.TaskSheetId != null && sheetIds.Contains(d.TaskSheetId)).ToListAsync())
                .Where(d => d.Status != DisputeStatus.Resolved && d.Status != DisputeStatus.Dismissed)
                .Select(d => d.TaskSheetId!).ToHashSet();

            return items.Select(c =>
            {
                sheets.TryGetValue(c.TaskSheetId, out var sheet);
                assignments.TryGetValue(c.AssignmentId ?? "", out var asg);
                var reasons = c.FlagReasons ?? new List<string>();
                return new PackageVisitAdminRowDTO
                {
                    CheckinId = c.Id.ToString(),
                    TaskSheetId = c.TaskSheetId,
                    SheetNumber = sheet?.SheetNumber ?? 0,
                    AssignmentId = c.AssignmentId ?? string.Empty,
                    PackageRequestId = c.PackageRequestId ?? string.Empty,
                    CaregiverId = c.CaregiverId,
                    CaregiverName = caregivers.GetValueOrDefault(c.CaregiverId, "(unknown)"),
                    ClientId = asg?.ClientId ?? string.Empty,
                    ClientName = asg != null ? clients.GetValueOrDefault(asg.ClientId, "(unknown)") : "(unknown)",
                    VisitDate = sheet?.ScheduledDate,
                    CheckinTimestamp = c.CheckinTimestamp,
                    DistanceMeters = c.DistanceFromServiceAddress,
                    AccuracyMeters = c.Accuracy,
                    Reasons = reasons,
                    Severity = CheckinFlagReasons.HasHard(reasons) ? "hard" : reasons.Count > 0 ? "soft" : "none",
                    FlagCleared = c.FlagClearedAt != null,
                    FlagClearedAt = c.FlagClearedAt,
                    FlagClearedByAdminEmail = c.FlagClearedByAdminEmail,
                    FlagClearNote = c.FlagClearNote,
                    TaskSheetStatus = sheet?.Status ?? string.Empty,
                    ClientReviewStatus = sheet?.ClientReviewStatus,
                    HasOpenDispute = openDisputeSheets.Contains(c.TaskSheetId),
                };
            }).ToList();
        }
    }
}
