using Application.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    /// <summary>
    /// Phase 2 vetting: caregiver guarantors. Exactly two confirmed guarantors
    /// are required per caregiver — enforced here at the service layer. Guarantors
    /// confirm through a self-serve emailed link (no account required).
    /// </summary>
    public interface IGuarantorService
    {
        /// <summary>Exactly this many guarantors are required per caregiver.</summary>
        public const int RequiredGuarantorCount = 2;

        Task<IEnumerable<GuarantorResponse>> GetGuarantorsAsync(string caregiverId);

        /// <summary>
        /// Adds a guarantor. Throws <see cref="System.InvalidOperationException"/>
        /// if the caregiver already has <see cref="RequiredGuarantorCount"/> guarantors.
        /// </summary>
        Task<GuarantorResponse> AddGuarantorAsync(string caregiverId, AddGuarantorRequest request);

        /// <summary>Updates guarantor contact details. Only permitted while pending.</summary>
        Task<GuarantorResponse> UpdateGuarantorAsync(string caregiverId, string guarantorId, UpdateGuarantorRequest request);

        Task DeleteGuarantorAsync(string caregiverId, string guarantorId);

        /// <summary>
        /// Generates a signed confirmation link and emails it to the guarantor.
        /// Applies the same attempt-cap + cooldown gating as Verification.
        /// </summary>
        Task<GuarantorResponse> SendConfirmationLinkAsync(string caregiverId, string guarantorId, string? origin);

        /// <summary>
        /// Anonymous: validates the emailed token and returns the minimum the questionnaire page needs to
        /// render. Returns null when the token is invalid, expired or the guarantor no longer exists.
        /// Carries nothing the caregiver declared.
        /// </summary>
        Task<GuarantorQuizInfo?> GetQuizInfoAsync(string token);

        /// <summary>
        /// Anonymous: consumes the emailed token plus the guarantor's answers. Runs the soft-gate
        /// auto-checks — all clear → confirmed (self-serve), any flag → needs_review for staff. The
        /// result wording is the same either way. One submission per guarantor.
        /// </summary>
        Task<GuarantorConfirmationResult> SubmitResponsesAsync(string token, SubmitGuarantorResponsesRequest request);

        // ── Staff (OperationsPolicy) actions ──

        /// <summary>
        /// Staff manually confirms a guarantor, bypassing the self-serve email flow
        /// (email bounced, link lost, verified by phone). Records the acting admin and
        /// writes an <c>AdminAuditLog</c> entry. Idempotent: confirming an already-confirmed
        /// guarantor is a safe no-op that leaves the original confirmation intact.
        /// </summary>
        Task<AdminGuarantorResponse> AdminConfirmGuarantorAsync(string guarantorId, string adminId, string? adminEmail, string reason);

        /// <summary>
        /// Staff resends a guarantor's confirmation link on the caregiver's behalf.
        /// Respects the lifetime attempt cap but bypasses the resend cooldown.
        /// </summary>
        Task<AdminGuarantorResponse> AdminResendConfirmationLinkAsync(string guarantorId, string adminId, string? adminEmail, string? origin);

        /// <summary>Staff view of a caregiver's guarantors (no caller-ownership requirement).</summary>
        Task<IEnumerable<AdminGuarantorResponse>> AdminGetGuarantorsForCaregiverAsync(string caregiverId);

        /// <summary>Every guarantor waiting on a staff decision (status needs_review), oldest submission first.</summary>
        Task<IEnumerable<AdminGuarantorResponse>> AdminGetNeedsReviewAsync();

        /// <summary>
        /// Staff approve or reject a needs_review guarantor after reading their answers. A note is
        /// required and the decision is audit-logged. Throws <see cref="System.InvalidOperationException"/>
        /// if the guarantor is not currently in needs_review.
        /// </summary>
        Task<AdminGuarantorResponse> AdminReviewGuarantorAsync(string guarantorId, string adminId, string? adminEmail, AdminReviewGuarantorRequest request);
    }
}
