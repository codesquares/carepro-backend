namespace Domain.Entities
{
    /// <summary>
    /// Thrown when visit check-in validation fails. Carries a structured error code
    /// so the controller can return it to the frontend for targeted UI handling.
    /// </summary>
    public class CheckinValidationException : Exception
    {
        public string ErrorCode { get; }
        public double? DistanceMeters { get; init; }
        public int? MaxDistanceMeters { get; init; }
        public string? ScheduledDay { get; init; }
        public string? ScheduledStartTime { get; init; }
        public string? ScheduledEndTime { get; init; }
        public string? CurrentTimeNigeria { get; init; }

        public CheckinValidationException(string message, string errorCode) : base(message)
        {
            ErrorCode = errorCode;
        }

        public static CheckinValidationException Proximity(string message, double distance, int maxDistance) =>
            new(message, "PROXIMITY_TOO_FAR") { DistanceMeters = distance, MaxDistanceMeters = maxDistance };

        public static CheckinValidationException OutsideSchedule(string message, string day, string start, string end, string currentTime) =>
            new(message, "OUTSIDE_SCHEDULE") { ScheduledDay = day, ScheduledStartTime = start, ScheduledEndTime = end, CurrentTimeNigeria = currentTime };

        public static CheckinValidationException NotScheduledToday(string message, string currentDay, string currentTime) =>
            new(message, "NOT_SCHEDULED_TODAY") { ScheduledDay = currentDay, CurrentTimeNigeria = currentTime };

        public static CheckinValidationException NoApprovedContract(string message) =>
            new(message, "NO_APPROVED_CONTRACT");

        public static CheckinValidationException NoGeocodedAddress(string message) =>
            new(message, "NO_GEOCODED_ADDRESS");

        public static CheckinValidationException TaskSheetCancelled(string message) =>
            new(message, "TASKSHEET_CANCELLED");
    }
}
