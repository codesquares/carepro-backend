using System.ComponentModel.DataAnnotations;

namespace Application.DTOs
{
    /// <summary>
    /// Admin overrides the verification status (e.g. flips a "Failed" record
    /// to "Completed" after manually confirming the mismatch is benign —
    /// such as Dojah returning a middle name as the first name while DOB +
    /// last name match). Reason is required for the audit trail.
    /// </summary>
    public class AdminVerificationStatusOverrideRequest
    {
        [Required]
        public string AdminId { get; set; } = string.Empty;

        /// <summary>
        /// Target status. Allowed: "Completed", "Verified", "Success",
        /// "Failed", "Pending".
        /// </summary>
        [Required]
        public string NewStatus { get; set; } = string.Empty;

        [Required]
        [MinLength(5, ErrorMessage = "Reason must be at least 5 characters")]
        public string Reason { get; set; } = string.Empty;
    }

    public class AdminVerificationStatusOverrideResponse
    {
        public bool Success { get; set; }
        public string VerificationId { get; set; } = string.Empty;
        public string PreviousStatus { get; set; } = string.Empty;
        public string NewStatus { get; set; } = string.Empty;
        public bool IsVerified { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// Admin edits a caregiver's legal name. Used to correct mismatches that
    /// caused identity verification to fail (e.g. middle name registered as
    /// first name). Requires explicit confirmation and a reason.
    /// </summary>
    public class AdminUpdateCaregiverNameRequest
    {
        [Required]
        public string AdminId { get; set; } = string.Empty;

        [Required]
        public string FirstName { get; set; } = string.Empty;

        public string? MiddleName { get; set; }

        [Required]
        public string LastName { get; set; } = string.Empty;

        /// <summary>
        /// Must be true. Acts as a safety latch so the UI cannot accidentally
        /// trigger a name edit without explicit admin confirmation.
        /// </summary>
        [Required]
        public bool Confirmed { get; set; }

        [Required]
        [MinLength(5, ErrorMessage = "Reason must be at least 5 characters")]
        public string Reason { get; set; } = string.Empty;
    }

    public class AdminUpdateCaregiverNameResponse
    {
        public bool Success { get; set; }
        public string CaregiverId { get; set; } = string.Empty;
        public string PreviousFirstName { get; set; } = string.Empty;
        public string? PreviousMiddleName { get; set; }
        public string PreviousLastName { get; set; } = string.Empty;
        public string NewFirstName { get; set; } = string.Empty;
        public string? NewMiddleName { get; set; }
        public string NewLastName { get; set; } = string.Empty;
        public bool AppUserUpdated { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
