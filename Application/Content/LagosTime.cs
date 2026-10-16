using System;
using System.Globalization;

namespace Application.Content
{
    /// <summary>
    /// Single place that turns stored UTC timestamps into Africa/Lagos (WAT, UTC+1) display text.
    /// Storage stays UTC everywhere; this is display only.
    /// </summary>
    public static class LagosTime
    {
        // Linux/macOS use the IANA id; Windows without ICU uses its own id. Nigeria has no DST, so if neither
        // resolves (e.g. a stripped container image) a fixed +01:00 zone is exactly equivalent.
        private static readonly TimeZoneInfo Zone = Resolve();

        private static TimeZoneInfo Resolve()
        {
            foreach (var id in new[] { "Africa/Lagos", "W. Central Africa Standard Time" })
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (TimeZoneNotFoundException) { } catch (InvalidTimeZoneException) { }
            }
            return TimeZoneInfo.CreateCustomTimeZone("WAT", TimeSpan.FromHours(1), "West Africa Time", "WAT");
        }

        public static DateTime FromUtc(DateTime utc) =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

        /// <summary>e.g. "Friday, 9 October 2026 at 8:51 AM WAT"</summary>
        public static string Format(DateTime utc) =>
            FromUtc(utc).ToString("dddd, d MMMM yyyy 'at' h:mm tt", CultureInfo.InvariantCulture) + " WAT";
    }
}
