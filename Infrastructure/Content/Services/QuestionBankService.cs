using Application.DTOs;
using Application.Interfaces.Content;
using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Infrastructure.Content.Services
{
    public class QuestionBankService : IQuestionBankService
    {
        private readonly CareProDbContext careProDbContext;
        private readonly ILogger<QuestionBankService> logger;

        public QuestionBankService(CareProDbContext careProDbContext, ILogger<QuestionBankService> logger)
        {
            this.careProDbContext = careProDbContext;
            this.logger = logger;
        }

        public async Task<string> AddQuestionAsync(AddQuestionBankRequest addQuestionRequest)
        {
            try
            {
                var question = new QuestionBank
                {
                    Id = ObjectId.GenerateNewId(),
                    Category = addQuestionRequest.Category,
                    UserType = addQuestionRequest.UserType,
                    Question = addQuestionRequest.Question,
                    Options = addQuestionRequest.Options,
                    CorrectAnswer = addQuestionRequest.CorrectAnswer,
                    Explanation = addQuestionRequest.Explanation,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    Active = true
                };

                await careProDbContext.QuestionBank.AddAsync(question);
                await careProDbContext.SaveChangesAsync();

                return question.Id.ToString();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error adding question to question bank");
                throw;
            }
        }

        public async Task<bool> BatchAddQuestionsAsync(BatchAddQuestionBankRequest batchAddRequest)
        {
            try
            {
                var questions = new List<QuestionBank>();

                foreach (var questionRequest in batchAddRequest.Questions)
                {
                    var question = new QuestionBank
                    {
                        Id = ObjectId.GenerateNewId(),
                        Category = questionRequest.Category,
                        UserType = questionRequest.UserType,
                        Question = questionRequest.Question,
                        Options = questionRequest.Options,
                        CorrectAnswer = questionRequest.CorrectAnswer,
                        Explanation = questionRequest.Explanation,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                        Active = true
                    };

                    questions.Add(question);
                }

                await careProDbContext.QuestionBank.AddRangeAsync(questions);
                await careProDbContext.SaveChangesAsync();

                return true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error batch adding questions to question bank");
                throw;
            }
        }

        public async Task<QuestionBankDTO> GetQuestionByIdAsync(string id)
        {
            try
            {
                var question = await careProDbContext.QuestionBank.FirstOrDefaultAsync(q => q.Id.ToString() == id);

                if (question == null)
                {
                    throw new KeyNotFoundException($"Question with ID '{id}' not found.");
                }

                return MapToDTO(question);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting question by ID");
                throw;
            }
        }

        public async Task<List<QuestionBankDTO>> GetQuestionsByUserTypeAsync(string userType)
        {
            try
            {
                // Get questions specifically for this user type or for "Both"
                var questions = await careProDbContext.QuestionBank
                    .Where(q => q.UserType == userType || q.UserType == "Both")
                    .Where(q => q.Active)
                    .ToListAsync();

                return questions.Select(MapToDTO).ToList();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting questions by user type");
                throw;
            }
        }

        public async Task<List<QuestionBankDTO>> GetQuestionsByCategoryAsync(string category)
        {
            try
            {
                var questions = await careProDbContext.QuestionBank
                    .Where(q => q.Category == category)
                    .Where(q => q.Active)
                    .ToListAsync();

                return questions.Select(MapToDTO).ToList();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting questions by category");
                throw;
            }
        }

        public async Task<List<QuestionBankDTO>> GetAllQuestionsAsync()
        {
            try
            {
                var questions = await careProDbContext.QuestionBank.ToListAsync();
                return questions.Select(MapToDTO).ToList();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting all questions");
                throw;
            }
        }

        public async Task<bool> UpdateQuestionAsync(UpdateQuestionBankRequest updateQuestionRequest)
        {
            try
            {
                var question = await careProDbContext.QuestionBank
                    .FirstOrDefaultAsync(q => q.Id.ToString() == updateQuestionRequest.Id);

                if (question == null)
                {
                    throw new KeyNotFoundException($"Question with ID '{updateQuestionRequest.Id}' not found.");
                }

                // Update only provided fields
                if (!string.IsNullOrEmpty(updateQuestionRequest.Category))
                    question.Category = updateQuestionRequest.Category;

                if (!string.IsNullOrEmpty(updateQuestionRequest.UserType))
                    question.UserType = updateQuestionRequest.UserType;

                if (!string.IsNullOrEmpty(updateQuestionRequest.Question))
                    question.Question = updateQuestionRequest.Question;

                if (updateQuestionRequest.Options != null && updateQuestionRequest.Options.Count > 0)
                    question.Options = updateQuestionRequest.Options;

                if (!string.IsNullOrEmpty(updateQuestionRequest.CorrectAnswer))
                    question.CorrectAnswer = updateQuestionRequest.CorrectAnswer;

                if (!string.IsNullOrEmpty(updateQuestionRequest.Explanation))
                    question.Explanation = updateQuestionRequest.Explanation;

                if (updateQuestionRequest.Active.HasValue)
                    question.Active = updateQuestionRequest.Active.Value;

                question.UpdatedAt = DateTime.UtcNow;

                await careProDbContext.SaveChangesAsync();

                return true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error updating question");
                throw;
            }
        }

        public async Task<bool> DeleteQuestionAsync(string id)
        {
            try
            {
                var question = await careProDbContext.QuestionBank
                    .FirstOrDefaultAsync(q => q.Id.ToString() == id);

                if (question == null)
                {
                    throw new KeyNotFoundException($"Question with ID '{id}' not found.");
                }

                // Soft delete - just mark as inactive
                question.Active = false;
                question.UpdatedAt = DateTime.UtcNow;

                await careProDbContext.SaveChangesAsync();

                return true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error deleting question");
                throw;
            }
        }

        public async Task<List<QuestionBankDTO>> GetRandomQuestionsForAssessmentAsync(string userType, int count)
        {
            try
            {
                if (userType != "Cleaner" && userType != "Caregiver")
                {
                    throw new ArgumentException("User type must be either 'Cleaner' or 'Caregiver'");
                }

                var questions = new List<QuestionBankDTO>();

                if (userType == "Cleaner")
                {
                    // For cleaners: Get 10 questions from cleaner categories
                    var cleanerQuestions = await careProDbContext.QuestionBank
                        .Where(q => (q.UserType == "Cleaner" || q.UserType == "Both") && q.Active)
                        .ToListAsync();

                    // If we don't have enough questions, throw an error
                    if (cleanerQuestions.Count < count)
                    {
                        throw new InvalidOperationException($"Not enough active questions for Cleaner assessment. Required: {count}, Available: {cleanerQuestions.Count}");
                    }

                    // Randomly select questions
                    var random = new Random();
                    questions = cleanerQuestions
                        .OrderBy(q => random.Next())
                        .Take(count)
                        .Select(MapToDTO)
                        .ToList();
                }
                else if (userType == "Caregiver")
                {
                    // For caregivers: Get 30 questions covering all categories
                    var caregiverQuestions = await careProDbContext.QuestionBank
                        .Where(q => q.Active)
                        .ToListAsync();

                    // If we don't have enough questions, throw an error
                    if (caregiverQuestions.Count < count)
                    {
                        throw new InvalidOperationException($"Not enough active questions for Caregiver assessment. Required: {count}, Available: {caregiverQuestions.Count}");
                    }

                    // Group by category to ensure we have a good mix
                    var questionsByCategory = caregiverQuestions.GroupBy(q => q.Category).ToDictionary(g => g.Key, g => g.ToList());

                    // Calculate how many questions to take from each category
                    var categoryCounts = new Dictionary<string, int>();
                    var remainingCount = count;
                    var remainingCategories = questionsByCategory.Count;

                    foreach (var category in questionsByCategory.Keys)
                    {
                        // Calculate a proportional allocation for each category
                        var categoryCount = Math.Max(1, Math.Min(remainingCount / remainingCategories, questionsByCategory[category].Count));
                        categoryCounts[category] = categoryCount;
                        remainingCount -= categoryCount;
                        remainingCategories--;
                    }

                    // If we still have remaining questions to allocate, add them to categories with more questions
                    if (remainingCount > 0)
                    {
                        var sortedCategories = questionsByCategory
                            .OrderByDescending(kv => kv.Value.Count)
                            .Select(kv => kv.Key)
                            .ToList();

                        foreach (var category in sortedCategories)
                        {
                            if (remainingCount <= 0) break;

                            var additionalCount = Math.Min(remainingCount, questionsByCategory[category].Count - categoryCounts[category]);
                            if (additionalCount > 0)
                            {
                                categoryCounts[category] += additionalCount;
                                remainingCount -= additionalCount;
                            }
                        }
                    }

                    // Now select random questions from each category based on the calculated counts
                    var random = new Random();
                    foreach (var category in questionsByCategory.Keys)
                    {
                        var categoryCount = categoryCounts[category];
                        var categoryQuestions = questionsByCategory[category]
                            .OrderBy(q => random.Next())
                            .Take(categoryCount)
                            .Select(MapToDTO);

                        questions.AddRange(categoryQuestions);
                    }
                }

                return questions;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting random questions for assessment");
                throw;
            }
        }

        private QuestionBankDTO MapToDTO(QuestionBank question)
        {
            return new QuestionBankDTO
            {
                Id = question.Id.ToString(),
                Category = question.Category,
                UserType = question.UserType,
                Question = question.Question,
                Options = question.Options,
                CorrectAnswer = question.CorrectAnswer,
                Explanation = question.Explanation,
                CreatedAt = question.CreatedAt,
                UpdatedAt = question.UpdatedAt,
                Active = question.Active
            };
        }
    }
}
