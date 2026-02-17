using Application.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    public interface IEligibilityService
    {
        /// <summary>
        /// Returns a full eligibility map for all service categories for a caregiver.
        /// </summary>
        Task<EligibilityResponse> GetEligibilityAsync(string caregiverId);

        /// <summary>
        /// Checks whether a caregiver is eligible to publish a gig in the given category.
        /// Returns null if eligible, or a GigEligibilityError if not.
        /// </summary>
        Task<GigEligibilityError?> ValidateGigEligibilityAsync(string caregiverId, string gigCategory);

        /// <summary>
        /// Returns all service requirements (for the frontend requirements endpoint).
        /// </summary>
        Task<List<ServiceRequirementDTO>> GetAllServiceRequirementsAsync();

        /// <summary>
        /// Returns a single service requirement by its ID.
        /// </summary>
        Task<ServiceRequirementDTO?> GetServiceRequirementByIdAsync(string id);

        /// <summary>
        /// Creates a new service requirement. Returns the created requirement.
        /// </summary>
        Task<ServiceRequirementDTO> CreateServiceRequirementAsync(AddServiceRequirementRequest request);

        /// <summary>
        /// Updates an existing service requirement. Returns the updated requirement.
        /// </summary>
        Task<ServiceRequirementDTO?> UpdateServiceRequirementAsync(string id, UpdateServiceRequirementRequest request);

        /// <summary>
        /// Deletes a service requirement by its ID.
        /// </summary>
        Task<bool> DeleteServiceRequirementAsync(string id);
    }
}
