using Domain.Entities;
using System.Collections.Generic;

namespace Infrastructure.Content.Services
{
    /// <summary>How trustworthy the service location a check-in was compared against is.</summary>
    public enum ServiceLocationState { None, GeocodedOnly, ClientConfirmed }

    public sealed record CheckinFlagThresholds(double ConfirmedDistanceMeters, double GeocodedDistanceMeters, double LowAccuracyMeters);

    /// <summary>
    /// Pure rule set deciding which flag reasons apply to a package-visit check-in. Never decides to block anything.
    /// </summary>
    public static class CheckinFlagEvaluator
    {
        public static List<string> Evaluate(double? distanceMeters, double accuracyMeters, ServiceLocationState state, CheckinFlagThresholds t)
        {
            var reasons = new List<string>();
            switch (state)
            {
                case ServiceLocationState.None:
                    reasons.Add(CheckinFlagReasons.LocationNotConfirmed);
                    break;
                case ServiceLocationState.GeocodedOnly:
                    reasons.Add(CheckinFlagReasons.LocationGeocodedOnly);
                    if (distanceMeters.HasValue && distanceMeters.Value > t.GeocodedDistanceMeters) reasons.Add(CheckinFlagReasons.GeocodedFar);
                    break;
                case ServiceLocationState.ClientConfirmed:
                    if (distanceMeters.HasValue && distanceMeters.Value > t.ConfirmedDistanceMeters) reasons.Add(CheckinFlagReasons.DistanceFar);
                    break;
            }
            if (accuracyMeters > t.LowAccuracyMeters) reasons.Add(CheckinFlagReasons.LowAccuracy);
            return reasons;
        }
    }
}
