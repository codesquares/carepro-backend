using System;
using System.ComponentModel.DataAnnotations;

namespace Application.DTOs
{
    public class PayrollDTO
    {
        public string Id { get; set; } = string.Empty;
        public string AssignmentId { get; set; } = string.Empty;
        public string PackageRequestId { get; set; } = string.Empty;
        public string CaregiverId { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public DateTime PayPeriod { get; set; }
        /// <summary>"Hourly" | "Fixed".</summary>
        public string PayCalculationType { get; set; } = string.Empty;
        public double? HoursWorked { get; set; }
        public decimal? RateApplied { get; set; }
        public decimal CalculatedAmount { get; set; }
        public decimal FinalAmount { get; set; }
        public string? OverrideReason { get; set; }
        /// <summary>"Draft" | "Approved" | "Paid".</summary>
        public string Status { get; set; } = string.Empty;
        public DateTime? CreditedToWalletAt { get; set; }
        public string? ApprovedByAdminEmail { get; set; }
        public DateTime? ApprovedAt { get; set; }
        /// <summary>Draft only: how many of this assignment's visits in the pay period still carry an uncleared HARD GPS flag.</summary>
        public int UnclearedHardFlagCount { get; set; }
        /// <summary>Approved: how many uncleared flags the admin confirmed approving over.</summary>
        public int? ApprovedWithUnclearedFlags { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// Admin creates a Draft payroll record for one Assignment's pay-period month.
    /// The amount is calculated server-side (Package.FixedCaregiverPay for Fixed, or
    /// Phase 9.6's monthly-hours aggregation × the caregiver's active CaregiverPayRate
    /// for Hourly) — never supplied by the caller.
    /// </summary>
    public class CreatePayrollRequest
    {
        [Required]
        public string AssignmentId { get; set; } = string.Empty;

        [Required, Range(2020, 2100)]
        public int Year { get; set; }

        [Required, Range(1, 12)]
        public int Month { get; set; }
    }

    /// <summary>
    /// Approves a Draft payroll record, triggering the wallet credit. Omit FinalAmount to
    /// approve at CalculatedAmount; supply it (with a reason) to override.
    /// </summary>
    public class ApprovePayrollRequest
    {
        [Range(0, 100_000_000)]
        public decimal? FinalAmount { get; set; }

        [StringLength(1000, MinimumLength = 5)]
        public string? OverrideReason { get; set; }

        /// <summary>
        /// Required when the payroll has uncleared HARD-flagged visits: must equal the current count
        /// ("Approve with N uncleared flagged visits"). Soft gate — approval is allowed, but only knowingly.
        /// </summary>
        [Range(0, 10000)]
        public int? AcknowledgeUnclearedFlaggedVisits { get; set; }
    }
}
