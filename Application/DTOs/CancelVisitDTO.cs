namespace Application.DTOs
{
    public class CancelVisitRequest
    {
        /// <summary>
        /// The task sheet ID for the visit to cancel.
        /// </summary>
        public string TaskSheetId { get; set; } = string.Empty;

        /// <summary>
        /// Optional reason for cancellation.
        /// </summary>
        public string? Reason { get; set; }
    }

    /// <summary>
    /// Caregiver requests cancellation of an upcoming visit.
    /// This notifies the client so they can cancel through the platform.
    /// </summary>
    public class CaregiverCancelVisitRequest
    {
        /// <summary>
        /// The task sheet ID for the visit to cancel.
        /// </summary>
        public string TaskSheetId { get; set; } = string.Empty;

        /// <summary>
        /// Required reason for cancellation.
        /// </summary>
        public string Reason { get; set; } = string.Empty;
    }

    public class CancelVisitResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public decimal? CreditAmount { get; set; }
        public decimal? NewCreditBalance { get; set; }
        public string? TaskSheetId { get; set; }
    }
}
