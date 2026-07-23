using Application.DTOs;
using Domain.Entities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    /// <summary>
    /// Consolidated hire-readiness gate: identity verification + assessment/certificate
    /// eligibility (delegated to IEligibilityService) + active-gig existence.
    /// Enforced at hire time (CareRequestResponseService.HireResponderAsync) and at
    /// negotiation-agree / payment-completion time (the backstop for a caregiver who
    /// becomes ineligible after being hired but before the order is created) — not at
    /// browse/matching/outreach time, which surface full inventory by design (see
    /// discussion: filtering discovery collapsed visible marketplace supply and worked
    /// against the conversion goal this service exists to support).
    /// </summary>
    public interface ICaregiverReadinessService
    {
        /// <summary>
        /// <paramref name="knownHasActiveGig"/>: pass a value when the caller has already
        /// determined gig existence from data it loaded itself (e.g. a batch-loaded gig list),
        /// to skip the internal gig query. Leave null to have this service look it up.
        /// </summary>
        Task<CaregiverReadinessResult> GetReadinessAsync(
            string caregiverId, string? category = null, bool? knownHasActiveGig = null);

        /// <summary>
        /// Batched readiness check for a set of caregivers, by ID (fetches caregiver records
        /// itself). Pass a category to also gate on category-specific assessment/certificate
        /// eligibility and category-scoped gig existence; pass null for identity + any-active-gig
        /// only.
        /// </summary>
        Task<Dictionary<string, CaregiverReadinessResult>> GetReadinessBulkAsync(
            IEnumerable<string> caregiverIds, string? category);

        /// <summary>
        /// Same as <see cref="GetReadinessBulkAsync(IEnumerable{string}, string?)"/> but takes
        /// already-loaded <see cref="Caregiver"/> entities, avoiding a redundant re-fetch when
        /// the caller already has them in memory.
        /// </summary>
        Task<Dictionary<string, CaregiverReadinessResult>> GetReadinessBulkAsync(
            List<Caregiver> caregivers, string? category);
    }
}
