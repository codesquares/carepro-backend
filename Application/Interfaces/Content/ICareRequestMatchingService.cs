using Application.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    public interface ICareRequestMatchingService
    {
        /// <summary>
        /// Phase 4 — internal assignment entry point. Scores available caregivers,
        /// hard-filtering on the package's required caregiver type / specialty. Readiness gaps
        /// are flagged on each result rather than excluding the caregiver. Returns ranked candidates;
        /// does not persist recommendations or send competitive match notifications.
        /// </summary>
        Task<List<CaregiverMatchDTO>> FindCandidatesForPackageAsync(PackageAssignmentMatchQuery query);
    }
}
