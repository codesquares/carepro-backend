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
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Content.Services
{
    public class AssessmentService : IAssessmentService
    {
        private readonly CareProDbContext careProDbContext;
        private readonly ICareGiverService careGiverService;
        private readonly IQuestionBankService questionBankService;
        private readonly ILogger<AssessmentService> logger;

        public AssessmentService(
            CareProDbContext careProDbContext, 
            ICareGiverService careGiverService,
            IQuestionBankService questionBankService,
            ILogger<AssessmentService> logger)
        {
            this.careProDbContext = careProDbContext;
            this.careGiverService = careGiverService;
            this.questionBankService = questionBankService;
            this.logger = logger;
        }


        public async Task<string> CreateAssessementAsync(AddAssessmentRequest addAssessmentRequest)
        {
            var careGiver = await careGiverService.GetCaregiverUserAsync(addAssessmentRequest.CaregiverId);
            if (careGiver == null)
            {
                throw new KeyNotFoundException("The CaregiverID entered is not a Valid ID");
            }

            //var existingAssessment = await careProDbContext.Assessments.FirstOrDefaultAsync(x => x.UserId == addAssessmentRequest.UserId);

            //if (existingAssessment != null)
            //{
            //    throw new InvalidOperationException("This caregiver has already been assessed. Please update the existing assessment.");                
            //}


            /// CONVERT DTO TO DOMAIN OBJECT            
            var assessment = new Assessment
            {
                Questions = addAssessmentRequest.Questions,
                Status = addAssessmentRequest.Status,
                Score = addAssessmentRequest.Score,
                CaregiverId = addAssessmentRequest.CaregiverId,

                // Assign new ID
                Id = ObjectId.GenerateNewId(),
                AssessedDate = DateTime.Now,
            };

            await careProDbContext.Assessments.AddAsync(assessment);

            await careProDbContext.SaveChangesAsync();

            return assessment.Id.ToString();
        }

        public async Task<AssessmentDTO> GetAssesementAsync(string caregiverId)
        {
            var assesement = await careProDbContext.Assessments.FirstOrDefaultAsync(x => x.CaregiverId.ToString() == caregiverId);

            if (assesement == null)
            {
                throw new KeyNotFoundException($"User with ID '{caregiverId}' has not been verified.");
            }


            var assessmentDTO = new AssessmentDTO()
            {
                Id = assesement.Id.ToString(),
                CaregiverId = assesement.CaregiverId,
                AssessedDate = assesement.AssessedDate,
                Questions = assesement.Questions,
                Status = assesement.Status,
                Score = assesement.Score,

            };

            return assessmentDTO;
        }

        // New methods for updated assessment system
        
        public async Task<List<QuestionBankDTO>> GetQuestionsForAssessmentAsync(string userType)
        {
            try
            {
                // Determine number of questions based on user type
                int questionCount = userType.ToLower() == "cleaner" ? 10 : 30;
                
                // Use the question bank service to get random questions for this user type
                return await questionBankService.GetRandomQuestionsForAssessmentAsync(userType, questionCount);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error getting assessment questions for user type: {userType}");
                throw;
            }
        }
        
        public async Task<string> SubmitAssessmentAsync(AddAssessmentRequest assessmentRequest)
        {
            try
            {
                // Validate user ID
                if (string.IsNullOrEmpty(assessmentRequest.UserId))
                {
                    throw new ArgumentException("User ID is required");
                }
                
                // Create a new assessment record
                var assessment = new Assessment
                {
                    Id = ObjectId.GenerateNewId(),
                    UserId = assessmentRequest.UserId,
                    CaregiverId = assessmentRequest.CaregiverId,
                    UserType = assessmentRequest.UserType,
                    StartTimestamp = DateTime.UtcNow,
                    EndTimestamp = DateTime.UtcNow,
                    Status = assessmentRequest.Status,
                    AssessedDate = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    Questions = new List<AssessmentQuestion>()
                };
                
                // Process each submitted question
                foreach (var questionSubmission in assessmentRequest.Questions)
                {
                    // Get the original question from the bank
                    var originalQuestion = await questionBankService.GetQuestionByIdAsync(questionSubmission.QuestionId);
                    
                    // Determine if the answer is correct
                    bool isCorrect = originalQuestion.CorrectAnswer.Equals(questionSubmission.UserAnswer, StringComparison.OrdinalIgnoreCase);
                    
                    // Add to assessment
                    var assessmentQuestion = new AssessmentQuestion
                    {
                        QuestionId = originalQuestion.Id,
                        Question = originalQuestion.Question,
                        Options = originalQuestion.Options,
                        CorrectAnswer = originalQuestion.CorrectAnswer,
                        UserAnswer = questionSubmission.UserAnswer,
                        IsCorrect = isCorrect
                    };
                    
                    assessment.Questions.Add(assessmentQuestion);
                }
                
                // Save the assessment
                await careProDbContext.Assessments.AddAsync(assessment);
                await careProDbContext.SaveChangesAsync();
                
                // Calculate the score
                await CalculateAssessmentScoreAsync(assessment.Id.ToString());
                
                return assessment.Id.ToString();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error submitting assessment");
                throw;
            }
        }
        
        public async Task<AssessmentDTO> GetAssessmentByIdAsync(string assessmentId)
        {
            try
            {
                var assessment = await careProDbContext.Assessments.FirstOrDefaultAsync(a => a.Id.ToString() == assessmentId);
                
                if (assessment == null)
                {
                    throw new KeyNotFoundException($"Assessment with ID '{assessmentId}' not found.");
                }
                
                return MapToDTO(assessment);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting assessment by ID");
                throw;
            }
        }
        
        public async Task<List<AssessmentDTO>> GetAssessmentsByUserIdAsync(string userId)
        {
            try
            {
                var assessments = await careProDbContext.Assessments
                    .Where(a => a.UserId == userId)
                    .OrderByDescending(a => a.EndTimestamp)
                    .ToListAsync();
                
                return assessments.Select(MapToDTO).ToList();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting assessments by user ID");
                throw;
            }
        }
        
        public async Task<AssessmentDTO> CalculateAssessmentScoreAsync(string assessmentId)
        {
            try
            {
                var assessment = await careProDbContext.Assessments.FirstOrDefaultAsync(a => a.Id.ToString() == assessmentId);
                
                if (assessment == null)
                {
                    throw new KeyNotFoundException($"Assessment with ID '{assessmentId}' not found.");
                }
                
                // Calculate score based on correct answers
                int totalQuestions = assessment.Questions.Count;
                int correctAnswers = assessment.Questions.Count(q => q.IsCorrect);
                
                if (totalQuestions > 0)
                {
                    // Calculate percentage score (0-100)
                    assessment.Score = (int)Math.Round((double)correctAnswers / totalQuestions * 100);
                    
                    // Determine if passed (70% threshold)
                    assessment.Passed = assessment.Score >= 70;
                    
                    // Update end timestamp
                    assessment.EndTimestamp = DateTime.UtcNow;
                    assessment.UpdatedAt = DateTime.UtcNow;
                    
                    await careProDbContext.SaveChangesAsync();
                }
                
                return MapToDTO(assessment);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error calculating assessment score");
                throw;
            }
        }
        
        private AssessmentDTO MapToDTO(Assessment assessment)
        {
            return new AssessmentDTO
            {
                Id = assessment.Id.ToString(),
                UserId = assessment.UserId,
                CaregiverId = assessment.CaregiverId,
                UserType = assessment.UserType,
                StartTimestamp = assessment.StartTimestamp,
                EndTimestamp = assessment.EndTimestamp,
                Score = assessment.Score,
                Passed = assessment.Passed,
                Status = assessment.Status,
                AssessedDate = assessment.AssessedDate,
                UpdatedAt = assessment.UpdatedAt,
                Questions = assessment.Questions?.Select(q => new AssessmentQuestion
                {
                    QuestionId = q.QuestionId,
                    Question = q.Question,
                    Options = q.Options,
                    CorrectAnswer = q.CorrectAnswer,
                    UserAnswer = q.UserAnswer,
                    IsCorrect = q.IsCorrect
                }).ToList() ?? new List<AssessmentQuestion>()
            };
        }
    }
}
