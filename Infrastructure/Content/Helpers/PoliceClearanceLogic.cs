using Application.DTOs;
using Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Infrastructure.Content.Helpers
{
    /// <summary>Pure rules shared by the caregiver service, the admin service and readiness.</summary>
    public static class PoliceClearanceLogic
    {
        /// <summary>The caregiver's current record: newest non-superseded one (null when none).</summary>
        public static PoliceClearance? Latest(IEnumerable<PoliceClearance> records) =>
            records.Where(r => r.Superseded != true)
                   .OrderByDescending(r => r.SubmittedAt ?? DateTime.MinValue)
                   .ThenByDescending(r => r.Id)
                   .FirstOrDefault();

        /// <summary>none | pending | approved | rejected | revoked. A record with a missing/unknown status counts as pending, never approved.</summary>
        public static string StatusName(PoliceClearance? c)
        {
            if (c == null) return "none";
            return c.Status switch
            {
                PoliceClearanceStatuses.Approved => "approved",
                PoliceClearanceStatuses.Rejected => "rejected",
                PoliceClearanceStatuses.Revoked => "revoked",
                _ => "pending"
            };
        }

        public static bool CanResubmit(PoliceClearance? latest) => StatusName(latest) != "approved";

        /// <summary>
        /// Age rule on the issue date. "Today" is the Africa/Lagos calendar date (UTC+1, no DST) so a Nigerian caregiver
        /// submitting just after local midnight isn't told today's date is in the future. A clearance issued exactly
        /// MaxAgeMonths before today is still accepted; one day older is rejected. Returns null when acceptable.
        /// </summary>
        public static string? CheckIssueDate(DateTime issueDate, DateTime nowUtc, int maxAgeMonths)
        {
            var today = nowUtc.AddHours(1).Date;
            if (issueDate.Date > today)
                return "The issue date can't be in the future. Please check the date on your clearance.";
            if (issueDate.Date < today.AddMonths(-maxAgeMonths))
                return $"This clearance is older than {maxAgeMonths} month{(maxAgeMonths == 1 ? "" : "s")}. Please upload a recent one.";
            return null;
        }

        /// <summary>When the latest decision was made: the revocation time for revoked records, else the review time.</summary>
        public static DateTime? DecisionAt(PoliceClearance c) =>
            StatusName(c) == "revoked" ? (c.RevokedAt ?? c.ReviewedAt) : c.ReviewedAt;

        public static string? ReasonFor(PoliceClearance c) => StatusName(c) switch
        {
            "rejected" => c.RejectionReason,
            "revoked" => c.RevokedReason,
            _ => null
        };

        public static List<ClearanceFileInfoDTO> FileInfos(PoliceClearance c) =>
            (c.Files ?? new List<PoliceClearanceFile>()).Select((f, i) => new ClearanceFileInfoDTO
            {
                Index = i, FileName = f.OriginalFileName, ContentType = f.ContentType, SizeBytes = f.SizeBytes
            }).ToList();

        public static List<ClearanceHistoryItemDTO> History(IEnumerable<PoliceClearance> records, int max = 20) =>
            records.OrderByDescending(r => r.SubmittedAt ?? DateTime.MinValue)
                   .Take(max)
                   .Select(r => new ClearanceHistoryItemDTO
                   {
                       Id = r.Id.ToString(), Status = StatusName(r), SubmittedAt = r.SubmittedAt, ReviewedAt = DecisionAt(r),
                       IssueDate = r.IssueDate, Reason = ReasonFor(r), Superseded = r.Superseded == true
                   }).ToList();
    }
}
