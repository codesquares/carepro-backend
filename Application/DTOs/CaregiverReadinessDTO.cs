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
    }

    public static class CaregiverReadinessReasons
    {
        public const string NotIdentityVerified = "not_identity_verified";
        public const string AssessmentNotPassed = "assessment_not_passed";
        public const string NoActiveGig = "no_active_gig";
        public const string CertificateMissing = "certificate_missing";

        // ── Phase 2 vetting gates ──
        public const string GuarantorsIncomplete = "guarantors_incomplete";
        public const string AddressHistoryIncomplete = "address_history_incomplete";
        public const string CaregiverTypeNotSet = "caregiver_type_not_set";
    }
}
