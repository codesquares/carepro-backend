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

    public class CancelVisitResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public decimal? CreditAmount { get; set; }
        public decimal? NewCreditBalance { get; set; }
        public string? TaskSheetId { get; set; }
    }
}
