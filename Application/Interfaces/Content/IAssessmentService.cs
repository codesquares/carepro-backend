using Application.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    public interface IAssessmentService
    {
        // Legacy method - will be updated to use new format
        Task<string> CreateAssessementAsync(AddAssessmentRequest addAssessmentRequest);

        // Legacy method - will be updated to use new format
        Task<AssessmentDTO> GetAssesementAsync(string caregiverId);

        // New methods for updated assessment system
        Task<List<QuestionBankDTO>> GetQuestionsForAssessmentAsync(string userType);
        Task<string> SubmitAssessmentAsync(AddAssessmentRequest assessmentRequest);
        Task<AssessmentDTO> GetAssessmentByIdAsync(string assessmentId);
        Task<List<AssessmentDTO>> GetAssessmentsByUserIdAsync(string userId);
        Task<AssessmentDTO> CalculateAssessmentScoreAsync(string assessmentId);
    }
}
