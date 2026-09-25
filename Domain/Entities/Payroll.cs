using MongoDB.Bson;
using System;

namespace Domain.Entities
{
    /// <summary>
    /// One payroll record per Assignment per pay-period month (Phase 9.7) — a caregiver
    /// earns payroll for a given month only if they were actively assigned during that
    /// period. Created and approved through admin CRUD endpoints; approval is the
    /// finality event that credits the caregiver's wallet (see PayrollService), so there
    /// is no separate per-visit release ceremony the way order-based earnings have one.
    /// </summary>
    public class Payroll
    {
        public ObjectId Id { get; set; }

        public string AssignmentId { get; set; } = string.Empty;
        public string PackageRequestId { get; set; } = string.Empty;
        public string CaregiverId { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;

        /// <summary>First day of the pay-period month, e.g. 2026-03-01.</summary>
        public DateTime PayPeriod { get; set; }

        /// <summary>Snapshotted from Package.PayCalculationType at creation — a later Package
        /// edit must not retroactively change an already-created payroll record.</summary>
        public PayCalculationType PayCalculationType { get; set; }

        /// <summary>Only meaningful when PayCalculationType is Hourly (from Phase 9.6's monthly aggregation).</summary>
        public double? HoursWorked { get; set; }

        /// <summary>Only meaningful when PayCalculationType is Hourly — the CaregiverPayRate
        /// snapshotted at creation time (a later rate-table edit doesn't move this record).</summary>
        public decimal? RateApplied { get; set; }

        public decimal CalculatedAmount { get; set; }

        /// <summary>Defaults to CalculatedAmount; can differ only via an admin override with
        /// a required reason (see OverrideReason), recorded in AdminAuditLog.</summary>
        public decimal FinalAmount { get; set; }

        /// <summary>Required when FinalAmount != CalculatedAmount.</summary>
        public string? OverrideReason { get; set; }

        /// <summary>Draft | Approved | Paid</summary>
        public string Status { get; set; } = PayrollStatuses.Draft;

        /// <summary>Set the moment approval credits the caregiver's wallet.</summary>
        public DateTime? CreditedToWalletAt { get; set; }

        public string? ApprovedByAdminId { get; set; }
        public string? ApprovedByAdminEmail { get; set; }
        public DateTime? ApprovedAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public static class PayrollStatuses
    {
        public const string Draft = "Draft";
        public const string Approved = "Approved";
        public const string Paid = "Paid";
    }
}
