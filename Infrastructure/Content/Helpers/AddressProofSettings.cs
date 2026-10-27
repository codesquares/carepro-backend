using Microsoft.Extensions.Configuration;
using System;
using System.Globalization;

namespace Infrastructure.Content.Helpers
{
    /// <summary>
    /// Config for the address-proof (utility bill) step, section "AddressProof". All optional.
    /// <c>EnforcementDate</c> (UTC): from that moment a caregiver is not "address complete" until staff approve a bill for the
    /// current address. Unset/unparseable = NOT enforced (fail open): address history is judged on dates alone, as before, so
    /// nobody flips to incomplete. Bills can still be uploaded and reviewed before the date.
    /// </summary>
    public class AddressProofSettings
    {
        public const string Section = "AddressProof";
        public int MaxBillAgeMonths { get; init; } = 3;
        public int MaxFileSizeMB { get; init; } = 10;
        public DateTime? EnforcementDateUtc { get; init; }

        /// <summary>True only when a valid enforcement date is set and has been reached.</summary>
        public bool IsEnforced(DateTime nowUtc) => EnforcementDateUtc.HasValue && nowUtc >= EnforcementDateUtc.Value;

        public static AddressProofSettings Load(IConfiguration? configuration)
        {
            if (configuration == null) return new AddressProofSettings();
            var s = configuration.GetSection(Section);
            static int Positive(string? raw, int fallback)
                => int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : fallback;
            DateTime? enforcement = null;
            var rawDate = s["EnforcementDate"];
            if (!string.IsNullOrWhiteSpace(rawDate)
                && DateTime.TryParse(rawDate, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
                enforcement = parsed;

            return new AddressProofSettings
            {
                EnforcementDateUtc = enforcement,
                MaxBillAgeMonths = Positive(s["MaxBillAgeMonths"], 3),
                MaxFileSizeMB = Positive(s["MaxFileSizeMB"], 10)
            };
        }

        /// <summary>The validator is shared with police clearance; a bill is exactly one file.</summary>
        public PoliceClearanceSettings ToFileSettings() => new()
        {
            MaxFiles = 1, MaxFileSizeMB = MaxFileSizeMB, MaxTotalSizeMB = MaxFileSizeMB
        };

        /// <summary>Lagos date (UTC+1), same convention as the police clearance issue date.</summary>
        public static string? CheckBillDate(DateTime billDate, DateTime nowUtc, int maxAgeMonths)
        {
            var today = nowUtc.AddHours(1).Date;
            if (billDate.Date > today)
                return "The bill date can't be in the future. Please check the date on your bill.";
            if (billDate.Date < today.AddMonths(-maxAgeMonths))
                return $"This bill is older than {maxAgeMonths} month{(maxAgeMonths == 1 ? "" : "s")}. Please upload a recent one.";
            return null;
        }
    }
}
