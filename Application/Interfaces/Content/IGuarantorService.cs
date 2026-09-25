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
        /// Consumes a guarantor confirmation token (from the emailed link) and
        /// flips the guarantor's status to confirmed. Anonymous / unauthenticated.
        /// </summary>
        Task<GuarantorConfirmationResult> ConfirmByTokenAsync(string token);

        // ── Staff (OperationsPolicy) actions ──

        /// <summary>
        /// Staff manually confirms a guarantor, bypassing the self-serve email flow
        /// (email bounced, link lost, verified by phone). Records the acting admin and
        /// writes an <c>AdminAuditLog</c> entry. Idempotent: confirming an already-confirmed
        /// guarantor is a safe no-op that leaves the original confirmation intact.
        /// </summary>
        Task<GuarantorResponse> AdminConfirmGuarantorAsync(string guarantorId, string adminId, string? adminEmail, string reason);

        /// <summary>
        /// Staff resends a guarantor's confirmation link on the caregiver's behalf.
        /// Respects the lifetime attempt cap but bypasses the resend cooldown.
        /// </summary>
        Task<GuarantorResponse> AdminResendConfirmationLinkAsync(string guarantorId, string adminId, string? adminEmail, string? origin);

        /// <summary>Staff view of a caregiver's guarantors (no caller-ownership requirement).</summary>
        Task<IEnumerable<GuarantorResponse>> AdminGetGuarantorsForCaregiverAsync(string caregiverId);
    }
}
