using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Application.DTOs
{
    // ─────────────────── 4.1  Matching against a package-derived request ───────────────────

    public class PackageAssignmentMatchQuery
    {
        public string ServiceCategory { get; set; } = string.Empty;
        public string RequiredCaregiverType { get; set; } = string.Empty; // AuxiliaryNurse | CHEW | RegisteredNurse
        public string? RequiredSpecialty { get; set; }
        public string? Location { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public string? ClientId { get; set; }
        public decimal? Budget { get; set; }
    }

    /// <summary>
    /// A single caregiver match result with scoring breakdown
    /// </summary>
    public class CaregiverMatchDTO
    {
        public int Rank { get; set; }
        public string CaregiverId { get; set; } = string.Empty;
        public string CaregiverName { get; set; } = string.Empty;
        public string? ProfileImage { get; set; }
        public bool IsAvailable { get; set; }
        public string? AboutMe { get; set; }
        public string? Location { get; set; }
        public double MatchScore { get; set; }
        public string MatchedServiceCategory { get; set; } = string.Empty;
        /// <summary>False when the category requires an assessment/certificate the caregiver hasn't completed. Informational — never hides the candidate.</summary>
        public bool AssessmentReady { get; set; } = true;
        /// <summary>Which requirements are unmet, e.g. "assessment", "certificate".</summary>
        public List<string> ReadinessGaps { get; set; } = new();
        public string? ReadinessMessage { get; set; }
        public double? DistanceKm { get; set; }
        public double AverageRating { get; set; }
        public int ReviewCount { get; set; }
        public MatchScoreBreakdownDTO ScoreBreakdown { get; set; } = new();
    }

    /// <summary>Points contributed by each factor; they sum to MatchScore (0-100).</summary>
    public class MatchScoreBreakdownDTO
    {
        public double ProximityScore { get; set; }   // max 45
        public double ExperienceScore { get; set; }  // max 30
        public double VettingScore { get; set; }     // max 25
    }

    // ─────────────────── 4.2  Package request ───────────────────

    public class CreatePackageRequestRequest
    {
        [Required]
        public string PackageId { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Location { get; set; }

        public double? Latitude { get; set; }
        public double? Longitude { get; set; }

        [Range(0, 100_000_000)]
        public decimal? Budget { get; set; }

        [StringLength(2000)]
        public string? Notes { get; set; }

        /// <summary>Optional override for the gig category the matcher scores against. Defaults to the package category.</summary>
        [StringLength(120)]
        public string? ServiceCategory { get; set; }

        /// <summary>
        /// Phase 10 — "OneTime" or "Recurring" (see <see cref="Domain.Entities.PackageRequestBillingTypes"/>).
        /// Not client-supplied through this DTO directly (there is no client-facing create endpoint —
        /// see PackagePaymentService); set internally by the admin payment-completion path based on
        /// what the client chose at purchase time. Defaults to OneTime when left unset.
        /// </summary>
        public string? BillingType { get; set; }
    }

    public class PackageRequestDTO
    {
        public string Id { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string PackageId { get; set; } = string.Empty;
        public string PackageCategory { get; set; } = string.Empty;
        public string PackageTierLabel { get; set; } = string.Empty;
        public string RequiredCaregiverType { get; set; } = string.Empty;
        public string? RequiredSpecialty { get; set; }
        public string ServiceCategory { get; set; } = string.Empty;
        public string? Location { get; set; }
        public decimal? Budget { get; set; }
        public string? Notes { get; set; }
        public string Status { get; set; } = string.Empty;
        public string BillingType { get; set; } = string.Empty;
        /// <summary>Non-null only once a caregiver has accepted — this is all the client sees before then.</summary>
        public ConfirmedCaregiverDTO? ConfirmedCaregiver { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>The client-facing view of a confirmed caregiver (only after acceptance).</summary>
    public class ConfirmedCaregiverDTO
    {
        public string CaregiverId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? ProfileImage { get; set; }
        public string CaregiverType { get; set; } = string.Empty;
        public string? Specialty { get; set; }
        public DateTime ConfirmedAt { get; set; }
    }

    /// <summary>
    /// Row in the staff-facing package-request picker (Assignment Console). Lets ops
    /// browse/filter requests before searching candidates for one — there was previously
    /// no admin-facing way to discover a PackageRequestId at all.
    /// </summary>
    public class AdminPackageRequestDTO
    {
        public string Id { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string ClientName { get; set; } = string.Empty;
        public string PackageCategory { get; set; } = string.Empty;
        public string PackageTierLabel { get; set; } = string.Empty;
        public string RequiredCaregiverType { get; set; } = string.Empty;
        public string? RequiredSpecialty { get; set; }
        public string ServiceCategory { get; set; } = string.Empty;
        public string? Location { get; set; }
        public decimal? Budget { get; set; }
        public string Status { get; set; } = string.Empty;
        public string BillingType { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    // ─────────────────── 4.2  Assignment ───────────────────

    public class AssignCaregiverRequest
    {
        [Required]
        public string PackageRequestId { get; set; } = string.Empty;

        [Required]
        public string CaregiverId { get; set; } = string.Empty;

        /// <summary>Optional match score from the engine, recorded on the assignment.</summary>
        public double? MatchScore { get; set; }

        /// <summary>"staff" (default) or "system".</summary>
        public string? AssignedBy { get; set; }
    }

    public class DeclineAssignmentRequest
    {
        [StringLength(500)]
        public string? Reason { get; set; }
    }

    public class CancelAssignmentRequest
    {
        [Required]
        [MinLength(5, ErrorMessage = "Reason must be at least 5 characters")]
        public string Reason { get; set; } = string.Empty;
    }

    public class AssignmentDTO
    {
        public string Id { get; set; } = string.Empty;
        public string PackageRequestId { get; set; } = string.Empty;
        public string CaregiverId { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string AssignedByAdminId { get; set; } = string.Empty;
        public string AssignedBy { get; set; } = string.Empty;
        public DateTime AssignedAt { get; set; }
        public double? MatchScore { get; set; }
        public DateTime? RespondedAt { get; set; }
        public string? DeclineReason { get; set; }
    }

    /// <summary>Row in the admin "Pending Acceptance" view — longest-pending first.</summary>
    public class PendingAssignmentDTO
    {
        public string AssignmentId { get; set; } = string.Empty;
        public string PackageRequestId { get; set; } = string.Empty;
        public string CaregiverId { get; set; } = string.Empty;
        public string CaregiverName { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string PackageCategory { get; set; } = string.Empty;
        public string PackageTierLabel { get; set; } = string.Empty;
        public string AssignedByAdminId { get; set; } = string.Empty;
        public DateTime AssignedAt { get; set; }
        /// <summary>How long this assignment has been awaiting a response.</summary>
        public double PendingForHours { get; set; }
        public string PendingForHuman { get; set; } = string.Empty;
    }

    /// <summary>
    /// Row in the staff-facing Accepted-assignments picker (Payroll Admin). Lets ops browse
    /// which assignments are actually in service before creating a payroll record for one —
    /// there was previously no admin-facing way to list Accepted assignments at all.
    /// </summary>
    public class AcceptedAssignmentDTO
    {
        public string AssignmentId { get; set; } = string.Empty;
        public string PackageRequestId { get; set; } = string.Empty;
        public string CaregiverId { get; set; } = string.Empty;
        public string CaregiverName { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string PackageCategory { get; set; } = string.Empty;
        public string PackageTierLabel { get; set; } = string.Empty;
        /// <summary>"Hourly" | "Fixed" — which branch payroll creation will take for this assignment.</summary>
        public string PayCalculationType { get; set; } = string.Empty;
        public DateTime? AcceptedAt { get; set; }
    }

    /// <summary>
    /// The caregiver's own view of an assignment — used for both the "My Assignments"
    /// list and its detail view. Enriched the same way <see cref="AcceptedAssignmentDTO"/>
    /// is for the admin picker (client name, package category/tier, pay type), but scoped
    /// to what's appropriate to share with the caregiver rather than staff.
    ///
    /// Deliberately has no "Schedule" field: neither <c>Contract.Schedule</c> nor any other
    /// record holds an advance day-of-week schedule for a package-model assignment today —
    /// <c>PackageContractService</c> always writes an empty Schedule list for these. What
    /// visit-time data does exist lives on individual <c>TaskSheet</c> rows, created one at
    /// a time by the caregiver — see <c>GET /api/caregiver/assignments/{id}/visits</c>.
    /// </summary>
    public class CaregiverAssignmentDTO
    {
        public string Id { get; set; } = string.Empty;
        public string PackageRequestId { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string ClientName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string PackageCategory { get; set; } = string.Empty;
        public string PackageTierLabel { get; set; } = string.Empty;
        /// <summary>"Hourly" | "Fixed" — informational context, not shown as an amount.</summary>
        public string PayCalculationType { get; set; } = string.Empty;
        public DateTime AssignedAt { get; set; }
        public DateTime? RespondedAt { get; set; }
        public string? DeclineReason { get; set; }
    }

    public class AssignmentActionResult
    {
        public bool Success { get; set; }
        public string AssignmentId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}
