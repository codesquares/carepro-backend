using Application.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    /// <summary>
    /// CRUD service for the LinkedIn-style "Professional Profile" sections
    /// (education, certifications/qualifications, work experience) attached
    /// to a caregiver and surfaced read-only on the public gig detail page.
    /// </summary>
    public interface ICaregiverProfileService
    {
        // Education
        Task<IEnumerable<CaregiverEducationResponse>> GetEducationAsync(string caregiverId);
        Task<CaregiverEducationResponse> AddEducationAsync(string caregiverId, AddCaregiverEducationRequest request);
        Task<CaregiverEducationResponse> UpdateEducationAsync(string caregiverId, string id, UpdateCaregiverEducationRequest request);
        Task DeleteEducationAsync(string caregiverId, string id);

        // Certifications / Qualifications
        Task<IEnumerable<CaregiverQualificationResponse>> GetQualificationsAsync(string caregiverId);
        Task<CaregiverQualificationResponse> AddQualificationAsync(string caregiverId, AddCaregiverQualificationRequest request);
        Task<CaregiverQualificationResponse> UpdateQualificationAsync(string caregiverId, string id, UpdateCaregiverQualificationRequest request);
        Task DeleteQualificationAsync(string caregiverId, string id);

        // Work Experience
        Task<IEnumerable<CaregiverWorkExperienceResponse>> GetWorkExperienceAsync(string caregiverId);
        Task<CaregiverWorkExperienceResponse> AddWorkExperienceAsync(string caregiverId, AddCaregiverWorkExperienceRequest request);
        Task<CaregiverWorkExperienceResponse> UpdateWorkExperienceAsync(string caregiverId, string id, UpdateCaregiverWorkExperienceRequest request);
        Task DeleteWorkExperienceAsync(string caregiverId, string id);

        // Public read (used by gig detail enrichment)
        Task<IEnumerable<CaregiverEducationResponse>> GetPublicEducationAsync(string caregiverId);
        Task<IEnumerable<CaregiverQualificationResponse>> GetPublicQualificationsAsync(string caregiverId);
        Task<IEnumerable<CaregiverWorkExperienceResponse>> GetPublicWorkExperienceAsync(string caregiverId);
    }
}
