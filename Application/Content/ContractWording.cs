using System;
using System.Globalization;

namespace Application.Content
{
    /// <summary>
    /// Single source for contract wording that must read identically in the HTML contract
    /// (ContractTemplateService), the PDF (ContractPdfService), the contract email and the client contract view.
    ///
    /// ⚠ LEGAL: these strings are the CURRENT approved-for-now text or an owner DRAFT awaiting legal review.
    /// To swap in new wording, change it here once — nowhere else.
    /// </summary>
    public static class ContractWording
    {
        // ── Section 2: relationship ─────────────────────────────────────────────────────────────
        public const string PlatformRelationshipHeading = "Section 2: Platform Relationship";

        /// <summary>CURRENT text (unchanged). Not yet replaced by the agency-direction draft — see <see cref="PlatformRelationshipDraft"/>.</summary>
        public const string PlatformRelationshipText =
            "CarePro is a technology platform that connects clients seeking care services with independent caregivers. " +
            "Caregivers are independent contractors and market participants offering their services on the CarePro platform. " +
            "CarePro does not employ caregivers and is not a party to the care relationship between client and caregiver. " +
            "CarePro facilitates the matching, payment processing, and contract management only.";

        /// <summary>
        /// DRAFT replacement for Section 2 — NOT IN USE. Owner draft, needs legal review before it replaces
        /// <see cref="PlatformRelationshipText"/>. Kept here so the swap is a one-line change.
        /// </summary>
        public const string PlatformRelationshipDraft =
            "CarePro is a home-care service provider. CarePro enters into this agreement with the Client and delivers the agreed care package. " +
            "CarePro assigns a vetted caregiver to deliver the service and remains responsible to the Client for the arrangement, subject to the terms of this agreement. " +
            "The Caregiver acts on CarePro's behalf in delivering the service and is not engaged by the Client directly.";

        // ── Section 11.1: caregiver vetting claim ───────────────────────────────────────────────
        /// <summary>
        /// Deliberately does NOT claim a "background check": no criminal/police background check exists or is enforced
        /// (verified in code 2026-10-09). Describes only what is real — identity verification, guarantors, address history.
        /// Restore a background-check claim only once police clearance is enforced.
        /// Previous text: "The Caregiver has been verified through CarePro's onboarding and background check process."
        /// </summary>
        public const string CaregiverVettingClaim =
            "The Caregiver has been verified through CarePro's onboarding and vetting process.";

        // ── Cancellation window acknowledgement ────────────────────────────────────────────────
        public const string CancellationHeading = "Cancellation window";

        /// <summary>
        /// DRAFT wording (owner-supplied, pending legal review): the client has N hours from the contract-sent
        /// time to cancel; not cancelling = agreeing to the terms.
        /// </summary>
        public static string CancellationAcknowledgement(int windowHours, DateTime contractSentAtUtc) =>
            $"You have {windowHours} hours from {FormatUtc(contractSentAtUtc)} to cancel this order. " +
            "If you do not cancel within this period, you confirm that you have read and agree to all the terms of this agreement.";

        public static string FormatUtc(DateTime utc) =>
            utc.ToString("dddd, MMMM dd, yyyy 'at' HH:mm 'UTC'", CultureInfo.InvariantCulture);
    }
}
