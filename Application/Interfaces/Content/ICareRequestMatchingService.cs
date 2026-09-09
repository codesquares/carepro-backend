using Application.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    public interface ICareRequestMatchingService
    {
        /// <summary>
        /// Phase 4 — internal assignment entry point. Reuses the same scoring pipeline
        /// and eligibility checks as the competitive flow, adding only a hard filter on
        /// the package's required caregiver type / specialty. Returns ranked candidates;
        /// does not persist recommendations or send competitive match notifications.
        /// </summary>
        Task<List<CaregiverMatchDTO>> FindCandidatesForPackageAsync(PackageAssignmentMatchQuery query);
    }
}
