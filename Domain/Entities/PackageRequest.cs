using MongoDB.Bson;
using System;
using System.Collections.Generic;

namespace Domain.Entities
{
    /// <summary>
    /// A client's request for a specific pre-priced <see cref="Package"/> (Phase 4).
    /// Distinct from the competitive <see cref="CareRequest"/> flow: a package request
    /// is fulfilled by <b>internal assignment</b> — staff/system pick one caregiver,
    /// who must accept before the client sees them as confirmed.
    ///
    /// The required-caregiver-type / specialty are snapshotted from the Package at
    /// creation so a later Package edit doesn't move an in-flight request.
    /// </summary>
    public class PackageRequest
    {
        public ObjectId Id { get; set; }

        public string ClientId { get; set; } = string.Empty;

        public string PackageId { get; set; } = string.Empty;

        // ── Snapshots from the Package at creation ──
        public string PackageCategory { get; set; } = string.Empty;
        public string PackageTierLabel { get; set; } = string.Empty;
        public CaregiverType RequiredCaregiverType { get; set; }
        public string? RequiredSpecialty { get; set; }

        /// <summary>Gig category the matching engine scores against (defaults to PackageCategory).</summary>
        public string ServiceCategory { get; set; } = string.Empty;

        public string? Location { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public decimal? Budget { get; set; }

        public string? Notes { get; set; }

        /// <summary>
        /// Phase 10 — client's billing choice at purchase time: <see cref="PackageRequestBillingTypes.OneTime"/>
        /// or <see cref="PackageRequestBillingTypes.Recurring"/>. Deliberately a per-request field, not a
        /// Package-level one — the same Package can be bought either way. Recurring requests get a
        /// corresponding <see cref="PackageSubscription"/> record (created alongside this one, see
        /// PackagePaymentService.CompleteRecurringPackagePaymentAsync); OneTime requests never do.
        /// Defaults to OneTime so pre-Phase-10 records (and the plain admin-payment path) are unaffected.
        /// </summary>
        public string BillingType { get; set; } = PackageRequestBillingTypes.OneTime;

        /// <summary>
        /// "pending"   — awaiting assignment
        /// "assigned"  — a caregiver has been assigned and is deciding (see Assignment)
        /// "confirmed" — caregiver accepted; ConfirmedCaregiverId is set and visible to the client
        /// "cancelled" — client or staff cancelled the request
        /// </summary>
        public string Status { get; set; } = "pending";

        /// <summary>Only populated once an Assignment is Accepted. This is what the client sees.</summary>
        public string? ConfirmedCaregiverId { get; set; }
        public DateTime? ConfirmedAt { get; set; }

        // ── Phase 7.1: caregiver pending-balance credit (fires at confirmation, not request creation) ──
        public bool CaregiverCredited { get; set; }
        public DateTime? CaregiverCreditedAt { get; set; }
        public decimal? CaregiverCreditAmount { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public DateTime? DeletedAt { get; set; }
    }

    public static class PackageRequestStatuses
    {
        public const string Pending = "pending";
        public const string Assigned = "assigned";
        public const string Confirmed = "confirmed";
        public const string Cancelled = "cancelled";
    }

    public static class PackageRequestBillingTypes
    {
        public const string OneTime = "OneTime";
        public const string Recurring = "Recurring";

        public static readonly IReadOnlyCollection<string> All = new[] { OneTime, Recurring };
    }
}
