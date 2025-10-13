using Application.DTOs;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    public interface IQuestionBankService
    {
        Task<string> AddQuestionAsync(AddQuestionBankRequest addQuestionRequest);
        Task<bool> BatchAddQuestionsAsync(BatchAddQuestionBankRequest batchAddRequest);
        Task<QuestionBankDTO> GetQuestionByIdAsync(string id);
        Task<List<QuestionBankDTO>> GetQuestionsByUserTypeAsync(string userType);
        Task<List<QuestionBankDTO>> GetQuestionsByCategoryAsync(string category);
        Task<List<QuestionBankDTO>> GetAllQuestionsAsync();
        Task<bool> UpdateQuestionAsync(UpdateQuestionBankRequest updateQuestionRequest);
        Task<bool> DeleteQuestionAsync(string id);
        Task<List<QuestionBankDTO>> GetRandomQuestionsForAssessmentAsync(string userType, int count);
    }
}
