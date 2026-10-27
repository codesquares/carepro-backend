using System.Collections.Generic;
using System.Linq;

namespace Domain.Entities
{
    /// <summary>
    /// Reasons a package-visit check-in can be flagged. HARD reasons set <c>IsFlaggedForDistanceReview</c> and notify
    /// admins; SOFT reasons are informational only. A check-in is never blocked for any of these.
    /// </summary>
    public static class CheckinFlagReasons
    {
        public const string DistanceFar = "distance_far";                 // HARD — client-confirmed location, beyond threshold
        public const string GeocodedFar = "geocoded_far";                 // HARD — geocoded-only location, beyond (wider) threshold
        public const string LocationNotConfirmed = "location_not_confirmed"; // SOFT — no coordinates at all
        public const string LocationGeocodedOnly = "location_geocoded_only"; // SOFT — coordinates exist but client hasn't confirmed
        public const string LowAccuracy = "low_accuracy";                 // SOFT — reported GPS accuracy worse than threshold

        public static readonly IReadOnlyCollection<string> Hard = new[] { DistanceFar, GeocodedFar };

        public static bool IsHard(string reason) => Hard.Contains(reason);
        public static bool HasHard(IEnumerable<string>? reasons) => reasons != null && reasons.Any(IsHard);
    }
}
