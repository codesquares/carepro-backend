using System;
using System.Collections.Generic;

namespace Application.DTOs
{
    /// <summary>
    /// Consolidated hire-readiness state for a caregiver in a given service category.
    /// Single source of truth for identity verification + assessment/certificate
    /// eligibility + active-gig existence, used at matching, browse, outreach, and hire time.
    /// </summary>
    public class CaregiverReadinessResult
    {
        public bool IsReady { get; set; }
        public List<string> IneligibilityReasons { get; set; } = new();
        public bool IsIdentityVerified { get; set; }
        public bool HasActiveGig { get; set; }
        public bool AssessmentPassed { get; set; }

        // ── Phase 2 vetting gates ──
        public bool HasTwoConfirmedGuarantors { get; set; }
        public bool AddressHistoryComplete { get; set; }
        public bool CaregiverTypeSet { get; set; }

        // ── Police clearance vetting step ──
        /// <summary>none | pending | approved | rejected | revoked (latest non-superseded record).</summary>
        public string PoliceClearanceStatus { get; set; } = "none";
        public bool PoliceClearanceApproved { get; set; }
        /// <summary>True once PoliceClearance:EnforcementDate has been reached. Unset/unparseable = never enforced.</summary>
        public bool PoliceClearanceEnforced { get; set; }
        public DateTime? PoliceClearanceEnforcementDate { get; set; }

        /// <summary>
        /// Reasons that do NOT affect <see cref="IsReady"/> — currently the police clearance reasons before the enforcement
        /// date. Shown to the caregiver as "Required from [date]" but never block assignment.
        /// </summary>
        public List<string> InformationalReasons { get; set; } = new();
    }

    public static class CaregiverReadinessReasons
    {
        public const string NotIdentityVerified = "not_identity_verified";
        public const string AssessmentNotPassed = "assessment_not_passed";
        /// <summary>RETIRED — no longer emitted (gigs are retired for caregivers). Constant kept for old tests/data.</summary>
        public const string NoActiveGig = "no_active_gig";
        public const string CaregiverNotFound = "caregiver_not_found";
        public const string CertificateMissing = "certificate_missing";

        // ── Phase 2 vetting gates ──
        public const string GuarantorsIncomplete = "guarantors_incomplete";
        public const string AddressHistoryIncomplete = "address_history_incomplete";
        public const string CaregiverTypeNotSet = "caregiver_type_not_set";

        // ── Police clearance (blocks readiness only at/after PoliceClearance:EnforcementDate) ──
        /// <summary>No clearance, or the latest was rejected/revoked. Actionable: the caregiver must (re)submit.</summary>
        public const string PoliceClearanceMissing = "police_clearance_missing";
        /// <summary>Submitted and awaiting staff review. Not actionable — shown as "Under review". Blocks assignment once enforced.</summary>
        public const string PoliceClearancePending = "police_clearance_pending";
    }
}
