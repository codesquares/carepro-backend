using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Domain.Entities
{
    /// <summary>
    /// A single past/current residential address for a caregiver (Phase 2 vetting).
    /// Deliberately NOT an extension of <see cref="Location"/> — that entity's
    /// upsert behaviour is destructive by design. Vetting needs an append-only
    /// history: two addresses covering the last five years.
    /// </summary>
    public class CaregiverAddressHistory
    {
        public ObjectId Id { get; set; }

        public string CaregiverId { get; set; } = string.Empty;

        public string Address { get; set; } = string.Empty;

        public DateTime MovedIn { get; set; }

        /// <summary>Null means "current address" (open-ended to now).</summary>
        public DateTime? MovedOut { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// Shared rule for "address history covers the last 5 years". Used by both the
    /// vetting service (validation feedback) and CaregiverReadinessService (gate).
    /// </summary>
    public static class CaregiverAddressHistoryCoverage
    {
        public const int RequiredYears = 5;
        public const int RequiredCount = 2;

        /// <summary>Gaps up to this many days between consecutive addresses are tolerated.</summary>
        public const int GapToleranceDays = 31;

        public static bool IsComplete(IEnumerable<CaregiverAddressHistory> records, DateTime now)
        {
            var list = records?.ToList() ?? new List<CaregiverAddressHistory>();
            if (list.Count < RequiredCount)
                return false;

            var windowStart = now.AddYears(-RequiredYears);

            // Normalise to [start, end] intervals clamped to the 5-year window.
            var intervals = list
                .Select(r => (Start: r.MovedIn, End: r.MovedOut ?? now))
                .Where(i => i.End > i.Start)
                .Select(i => (Start: i.Start < windowStart ? windowStart : i.Start,
                              End: i.End > now ? now : i.End))
                .Where(i => i.End > i.Start)
                .OrderBy(i => i.Start)
                .ToList();

            if (intervals.Count == 0)
                return false;

            // Must start at (or before) the window start, allowing the tolerance.
            if (intervals[0].Start > windowStart.AddDays(GapToleranceDays))
                return false;

            var coveredUntil = intervals[0].End;
            foreach (var (start, end) in intervals.Skip(1))
            {
                if (start > coveredUntil.AddDays(GapToleranceDays))
                    return false; // gap too large
                if (end > coveredUntil)
                    coveredUntil = end;
            }

            // Coverage must reach ~now.
            return coveredUntil >= now.AddDays(-GapToleranceDays);
        }
    }
}
