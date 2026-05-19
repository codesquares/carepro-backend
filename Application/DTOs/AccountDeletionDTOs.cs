namespace Application.DTOs
{
    /// <summary>
    /// Request body for initiating account deletion (self or admin-initiated).
    /// </summary>
    public class RequestAccountDeletionRequest
    {
        /// <summary>
        /// Required. Free-text reason the user is deleting their account.
        /// Stored in the AdminAuditLog for admin-initiated deletions.
        /// </summary>
        public string Reason { get; set; } = string.Empty;
    }

    /// <summary>
    /// Returned when a deletion request is successfully scheduled or blocked.
    /// </summary>
    public class AccountDeletionResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// UTC date/time when permanent deletion will occur (30 days after request).
        /// Only populated on a successful request.
        /// </summary>
        public DateTime? PermanentDeletionDate { get; set; }

        /// <summary>
        /// Populated when Success=false and the request is blocked.
        /// Lists the specific reasons the deletion cannot proceed.
        /// </summary>
        public List<string>? Blockers { get; set; }
    }
}
