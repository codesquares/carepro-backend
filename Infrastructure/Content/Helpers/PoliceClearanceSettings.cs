using Microsoft.Extensions.Configuration;
using System;
using System.Globalization;

namespace Infrastructure.Content.Helpers
{
    /// <summary>
    /// Police clearance configuration, read fresh from <see cref="IConfiguration"/> on each call so a changed
    /// value (including a reloaded enforcement date) takes effect without a restart.
    /// Keys (all under "PoliceClearance"): MaxAgeMonths (6), MaxFileSizeMB (10), MaxTotalSizeMB (20), MaxFiles (5),
    /// EnforcementDate (UTC; unset or unparseable = NOT enforced — fail open), AssetTag.
    /// </summary>
    public class PoliceClearanceSettings
    {
        public const string Section = "PoliceClearance";
        public int MaxAgeMonths { get; init; } = 6;
        public int MaxFileSizeMB { get; init; } = 10;
        public int MaxTotalSizeMB { get; init; } = 20;
        public int MaxFiles { get; init; } = 5;
        public DateTime? EnforcementDateUtc { get; init; }
        public string AssetTag { get; init; } = "police_clearance";

        public long MaxFileSizeBytes => MaxFileSizeMB * 1024L * 1024L;
        public long MaxTotalSizeBytes => MaxTotalSizeMB * 1024L * 1024L;

        /// <summary>True only when a valid enforcement date is set and has been reached. Unset/unparseable = false.</summary>
        public bool IsEnforced(DateTime nowUtc) => EnforcementDateUtc.HasValue && nowUtc >= EnforcementDateUtc.Value;

        public static PoliceClearanceSettings Load(IConfiguration? configuration)
        {
            if (configuration == null) return new PoliceClearanceSettings();
            var s = configuration.GetSection(Section);

            static int Positive(string? raw, int fallback)
                => int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : fallback;

            DateTime? enforcement = null;
            var rawDate = s["EnforcementDate"];
            if (!string.IsNullOrWhiteSpace(rawDate)
                && DateTime.TryParse(rawDate, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
                enforcement = parsed;

            return new PoliceClearanceSettings
            {
                MaxAgeMonths = Positive(s["MaxAgeMonths"], 6),
                MaxFileSizeMB = Positive(s["MaxFileSizeMB"], 10),
                MaxTotalSizeMB = Positive(s["MaxTotalSizeMB"], 20),
                MaxFiles = Positive(s["MaxFiles"], 5),
                EnforcementDateUtc = enforcement,
                AssetTag = string.IsNullOrWhiteSpace(s["AssetTag"]) ? "police_clearance" : s["AssetTag"]!
            };
        }
    }
}
