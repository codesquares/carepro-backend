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
        // ── Document-wide agency wording (ALL strings below are DRAFTS pending legal review — see LEGAL-DRAFTS-PENDING-REVIEW.md) ──

        /// <summary>Subtitle under the title. Previous: "Facilitated through the CarePro Platform".</summary>
        public const string Subtitle = "A CarePro Care Service Agreement";

        // ── Section 1: parties ──
        /// <summary>New sentence under the Section 1 heading naming CarePro as the contracting party.</summary>
        public const string PartiesIntro =
            "This agreement is between CarePro (the service provider) and the Client. " +
            "CarePro has assigned the Caregiver named below to deliver the care service on CarePro's behalf.";
        public const string ClientColumnHeading = "Client (Care Recipient)";
        /// <summary>Previous: "Caregiver (Service Provider)".</summary>
        public const string CaregiverColumnHeading = "Assigned Caregiver (delivering the service for CarePro)";
        /// <summary>Signature-block label. Previous: "Caregiver".</summary>
        public const string CaregiverSignatureLabel = "Assigned Caregiver";

        // ── Section 2: relationship ──
        public const string PlatformRelationshipHeading = "Section 2: Platform Relationship";

        /// <summary>
        /// Section 2 body (agency direction, owner draft — needs legal review). Previous text:
        /// "CarePro is a technology platform that connects clients seeking care services with independent caregivers.
        /// Caregivers are independent contractors and market participants offering their services on the CarePro platform.
        /// CarePro does not employ caregivers and is not a party to the care relationship between client and caregiver.
        /// CarePro facilitates the matching, payment processing, and contract management only."
        /// </summary>
        public const string PlatformRelationshipText =
            "CarePro is a home-care service provider. CarePro enters into this agreement with the Client and delivers the agreed care package. " +
            "CarePro assigns a vetted caregiver to deliver the service and remains responsible to the Client for the arrangement, subject to the terms of this agreement. " +
            "The Caregiver acts on CarePro's behalf in delivering the service and is not engaged by the Client directly.";

        // ── Section 13: limitation of liability ──
        /// <summary>Previous: "CarePro operates as a marketplace platform and is not liable for the actions or omissions of either party."</summary>
        public const string LiabilityLine1 =
            "CarePro is responsible to the Client for the delivery of the agreed care package, subject to the terms of this agreement.";
        /// <summary>Previous: "The Caregiver operates as an independent contractor and is solely responsible for the quality of care provided."</summary>
        public const string LiabilityLine2 =
            "The Caregiver delivers the care on CarePro's behalf and is expected to provide it to a professional standard.";

        // ── Section 14: acknowledgment ──
        /// <summary>Previous: "Both parties acknowledge they have read, understood, and agree to all terms of this Care Service Agreement."</summary>
        public const string AcknowledgmentText =
            "The Client and CarePro acknowledge they have read, understood, and agree to all terms of this Care Service Agreement. " +
            "The Caregiver acknowledges the terms that apply to delivering the service.";

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

        /// <summary>Display text for a stored UTC time — Africa/Lagos, labelled WAT (see <see cref="LagosTime"/>).</summary>
        public static string FormatUtc(DateTime utc) => LagosTime.Format(utc);

        /// <summary>
        /// Factual line added under the acknowledgement (DRAFT, pending legal review): when the window closes.
        /// The acknowledgement sentence itself is unchanged.
        /// </summary>
        public static string CancellationClosesLine(DateTime windowEndsUtc) =>
            $"The cancellation period closes on {LagosTime.Format(windowEndsUtc)}.";
    }
}
