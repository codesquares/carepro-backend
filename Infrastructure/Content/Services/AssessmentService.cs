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

            /// CONVERT DTO TO DOMAIN OBJECT            
            var assessment = new Assessment
            {
                Questions = addAssessmentRequest.Questions,
                Status = addAssessmentRequest.Status,
                Score = addAssessmentRequest.Score,
                CaregiverId = addAssessmentRequest.CaregiverId,
                ServiceCategory = addAssessmentRequest.ServiceCategory,

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
                ServiceCategory = assesement.ServiceCategory,
                PassingThreshold = assesement.PassingThreshold,
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
                    ServiceCategory = assessmentRequest.ServiceCategory,
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

                    // Use category-specific threshold if available, default to 70%
                    int threshold = assessment.PassingThreshold > 0 ? assessment.PassingThreshold : 70;
                    assessment.PassingThreshold = threshold;

                    // Determine if passed
                    assessment.Passed = assessment.Score >= threshold;

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

        // ===== Specialized Assessment Methods =====

        public async Task<SpecializedQuestionsResponse> GetSpecializedQuestionsAsync(string serviceCategory, string caregiverId)
        {
            try
            {
                if (string.IsNullOrEmpty(caregiverId))
                    throw new ArgumentException("Caregiver ID is required to start an assessment session.");

                // Look up requirements for this category to determine question count and session duration
                var requirements = await careProDbContext.ServiceRequirements
                    .Where(sr => sr.Active)
                    .ToListAsync();
                var requirement = requirements
                    .FirstOrDefault(sr => string.Equals(sr.ServiceCategory, serviceCategory, StringComparison.OrdinalIgnoreCase));

                int questionCount = requirement?.QuestionCount ?? 30;
                int sessionMinutes = requirement?.SessionDurationMinutes ?? 60;

                // Check for an existing active session for this caregiver + category
                var existingSessions = await careProDbContext.AssessmentSessions
                    .Where(s => s.CaregiverId == caregiverId && s.Status == "Active")
                    .ToListAsync();
                var activeSession = existingSessions
                    .FirstOrDefault(s => string.Equals(s.ServiceCategory, serviceCategory, StringComparison.OrdinalIgnoreCase));

                if (activeSession != null)
                {
                    // If session is expired, mark it as expired
                    if (activeSession.ExpiresAt <= DateTime.UtcNow)
                    {
                        activeSession.Status = "Expired";
                        careProDbContext.AssessmentSessions.Update(activeSession);
                        await careProDbContext.SaveChangesAsync();
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            $"You already have an active assessment session for {serviceCategory}. " +
                            $"Session expires at {activeSession.ExpiresAt:yyyy-MM-ddTHH:mm:ssZ}. " +
                            $"Please submit your current session or wait for it to expire.");
                    }
                }

                // Fetch random questions
                var questions = await questionBankService.GetRandomSpecializedQuestionsAsync(serviceCategory, questionCount);

                // Create a session record binding these questions to this caregiver
                var session = new AssessmentSession
                {
                    Id = ObjectId.GenerateNewId(),
                    CaregiverId = caregiverId,
                    ServiceCategory = serviceCategory,
                    QuestionIds = questions.Select(q => q.Id).ToList(),
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.AddMinutes(sessionMinutes),
                    Status = "Active"
                };

                await careProDbContext.AssessmentSessions.AddAsync(session);
                await careProDbContext.SaveChangesAsync();

                logger.LogInformation(
                    "Assessment session created: SessionId={SessionId}, CaregiverId={CaregiverId}, Category={Category}, Questions={QuestionCount}",
                    session.Id, caregiverId, serviceCategory, questions.Count);

                return new SpecializedQuestionsResponse
                {
                    SessionId = session.Id.ToString(),
                    ServiceCategory = serviceCategory,
                    SessionDurationMinutes = sessionMinutes,
                    ExpiresAt = session.ExpiresAt,
                    Questions = questions
                };
            }
            catch (Exception ex) when (ex is not ArgumentException && ex is not InvalidOperationException)
            {
                logger.LogError(ex, "Error getting specialized assessment questions for category: {ServiceCategory}", serviceCategory);
                throw;
            }
        }

        public async Task<AssessmentSubmitResponse> SubmitSpecializedAssessmentAsync(AddAssessmentRequest assessmentRequest)
        {
            try
            {
                if (string.IsNullOrEmpty(assessmentRequest.UserId))
                    throw new ArgumentException("User ID is required");

                if (string.IsNullOrEmpty(assessmentRequest.CaregiverId))
                    throw new ArgumentException("Caregiver ID is required");

                // Validate session if provided (required for specialized assessments)
                AssessmentSession? session = null;
                if (!string.IsNullOrEmpty(assessmentRequest.SessionId))
                {
                    var sessionId = ObjectId.Parse(assessmentRequest.SessionId);
                    session = await careProDbContext.AssessmentSessions
                        .FirstOrDefaultAsync(s => s.Id == sessionId);

                    if (session == null)
                        throw new ArgumentException("Invalid session ID. Please start a new assessment.");

                    if (session.Status == "Submitted")
                        throw new InvalidOperationException("This assessment session has already been submitted.");

                    if (session.Status == "Expired" || session.ExpiresAt <= DateTime.UtcNow)
                    {
                        if (session.Status != "Expired")
                        {
                            session.Status = "Expired";
                            careProDbContext.AssessmentSessions.Update(session);
                            await careProDbContext.SaveChangesAsync();
                        }
                        throw new InvalidOperationException(
                            "This assessment session has expired. Please start a new assessment.");
                    }

                    // Validate that the caregiver matches the session
                    if (session.CaregiverId != assessmentRequest.CaregiverId)
                        throw new ArgumentException("Session does not belong to this caregiver.");

                    // Validate that submitted question IDs match the session's assigned questions
                    var submittedQuestionIds = assessmentRequest.Questions
                        .Select(q => q.QuestionId)
                        .OrderBy(id => id)
                        .ToList();
                    var sessionQuestionIds = session.QuestionIds.OrderBy(id => id).ToList();

                    if (!submittedQuestionIds.SequenceEqual(sessionQuestionIds))
                    {
                        throw new ArgumentException(
                            "Submitted questions do not match the questions assigned in your session. " +
                            "Please answer only the questions you were given.");
                    }

                    // Use the session's service category
                    assessmentRequest.ServiceCategory = session.ServiceCategory;
                }
                else if (!string.IsNullOrEmpty(assessmentRequest.ServiceCategory))
                {
                    // Backward compatibility: allow submission without session for non-specialized
                    logger.LogWarning(
                        "Assessment submitted without session ID for category {Category} by caregiver {CaregiverId}",
                        assessmentRequest.ServiceCategory, assessmentRequest.CaregiverId);
                }

                // Look up requirements for cooldown and passing threshold (case-insensitive)
                var allReqs = await careProDbContext.ServiceRequirements
                    .Where(sr => sr.Active)
                    .ToListAsync();
                var reqCategory = assessmentRequest.ServiceCategory ?? "General";
                var requirement = allReqs
                    .FirstOrDefault(sr => string.Equals(sr.ServiceCategory, reqCategory, StringComparison.OrdinalIgnoreCase));

                int passingThreshold = requirement?.PassingScore ?? 70;
                int cooldownHours = requirement?.CooldownHours ?? 24;

                // Enforce cooldown: check ALL failed attempts (not just the most recent)
                var allAttempts = await careProDbContext.Assessments
                    .Where(a => a.CaregiverId == assessmentRequest.CaregiverId)
                    .ToListAsync();

                var lastFailedAttempt = allAttempts
                    .Where(a => !a.Passed && (assessmentRequest.ServiceCategory == null
                        ? (a.ServiceCategory == null || a.ServiceCategory == "")
                        : string.Equals(a.ServiceCategory, assessmentRequest.ServiceCategory, StringComparison.OrdinalIgnoreCase)))
                    .OrderByDescending(a => a.EndTimestamp)
                    .FirstOrDefault();

                if (lastFailedAttempt != null)
                {
                    var cooldownUntil = lastFailedAttempt.EndTimestamp.AddHours(cooldownHours);
                    if (DateTime.UtcNow < cooldownUntil)
                    {
                        throw new InvalidOperationException(
                            $"Cooldown active. You can retake this assessment after {cooldownUntil:yyyy-MM-ddTHH:mm:ssZ}. " +
                            $"Please wait {cooldownHours} hours between attempts.");
                    }
                }

                // Create the assessment record
                var assessment = new Assessment
                {
                    Id = ObjectId.GenerateNewId(),
                    UserId = assessmentRequest.UserId,
                    CaregiverId = assessmentRequest.CaregiverId,
                    UserType = assessmentRequest.UserType ?? "Caregiver",
                    ServiceCategory = assessmentRequest.ServiceCategory,
                    PassingThreshold = passingThreshold,
                    StartTimestamp = session?.CreatedAt ?? DateTime.UtcNow,
                    EndTimestamp = DateTime.UtcNow,
                    Status = "Completed",
                    AssessedDate = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    Questions = new List<AssessmentQuestion>()
                };

                // Score each question server-side with normalized answer comparison
                foreach (var questionSubmission in assessmentRequest.Questions)
                {
                    var originalQuestion = await questionBankService.GetQuestionByIdAsync(questionSubmission.QuestionId);

                    // Trim and normalize whitespace before comparing
                    var userAnswer = questionSubmission.UserAnswer?.Trim() ?? "";
                    var correctAnswer = originalQuestion.CorrectAnswer?.Trim() ?? "";

                    bool isCorrect = correctAnswer.Equals(userAnswer, StringComparison.OrdinalIgnoreCase);

                    assessment.Questions.Add(new AssessmentQuestion
                    {
                        QuestionId = originalQuestion.Id,
                        Question = originalQuestion.Question,
                        Options = originalQuestion.Options,
                        CorrectAnswer = originalQuestion.CorrectAnswer,
                        UserAnswer = questionSubmission.UserAnswer,
                        IsCorrect = isCorrect
                    });
                }

                // Calculate score
                int totalQuestions = assessment.Questions.Count;
                int correctAnswers = assessment.Questions.Count(q => q.IsCorrect);
                assessment.Score = totalQuestions > 0
                    ? (int)Math.Round((double)correctAnswers / totalQuestions * 100)
                    : 0;
                assessment.Passed = assessment.Score >= passingThreshold;

                // Persist assessment
                await careProDbContext.Assessments.AddAsync(assessment);

                // Mark the session as Submitted
                if (session != null)
                {
                    session.Status = "Submitted";
                    careProDbContext.AssessmentSessions.Update(session);
                }

                await careProDbContext.SaveChangesAsync();

                // Build response
                var response = new AssessmentSubmitResponse
                {
                    AttemptId = assessment.Id.ToString(),
                    Passed = assessment.Passed,
                    Score = assessment.Score,
                    Threshold = passingThreshold,
                    ServiceCategory = assessmentRequest.ServiceCategory,
                    CooldownUntil = assessment.Passed ? null : assessment.EndTimestamp.AddHours(cooldownHours)
                };

                logger.LogInformation(
                    "Assessment submitted: CaregiverId={CaregiverId}, Category={Category}, Score={Score}, Passed={Passed}, SessionId={SessionId}",
                    assessmentRequest.CaregiverId, assessmentRequest.ServiceCategory ?? "General",
                    assessment.Score, assessment.Passed, assessmentRequest.SessionId ?? "none");

                return response;
            }
            catch (Exception ex) when (ex is not ArgumentException && ex is not InvalidOperationException)
            {
                logger.LogError(ex, "Error submitting specialized assessment");
                throw;
            }
        }

        public async Task<PaginatedResponse<AssessmentHistoryDTO>> GetAssessmentHistoryAsync(string caregiverId, string? serviceCategory = null, int page = 1, int pageSize = 20)
        {
            try
            {
                var allAssessments = await careProDbContext.Assessments
                    .Where(a => a.CaregiverId == caregiverId)
                    .ToListAsync();

                IEnumerable<Assessment> filtered = allAssessments;
                if (!string.IsNullOrEmpty(serviceCategory))
                {
                    filtered = allAssessments.Where(a => string.Equals(a.ServiceCategory, serviceCategory, StringComparison.OrdinalIgnoreCase));
                }

                var assessments = filtered
                    .OrderByDescending(a => a.EndTimestamp)
                    .ToList();

                int totalCount = assessments.Count;

                // Apply pagination
                var pagedAssessments = assessments
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                // Lookup requirements per category for cooldown calculation
                var requirements = await careProDbContext.ServiceRequirements
                    .Where(sr => sr.Active)
                    .ToListAsync();

                var history = new List<AssessmentHistoryDTO>();

                foreach (var a in pagedAssessments)
                {
                    var categoryKey = a.ServiceCategory ?? "General";
                    var req = requirements.FirstOrDefault(r => string.Equals(r.ServiceCategory, categoryKey, StringComparison.OrdinalIgnoreCase));
                    int cooldownHours = req?.CooldownHours ?? 24;

                    DateTime? nextRetry = null;
                    if (!a.Passed)
                    {
                        var retryDate = a.EndTimestamp.AddHours(cooldownHours);
                        if (retryDate > DateTime.UtcNow)
                            nextRetry = retryDate;
                    }

                    history.Add(new AssessmentHistoryDTO
                    {
                        AttemptId = a.Id.ToString(),
                        ServiceCategory = a.ServiceCategory,
                        ServiceCategoryDisplayName = req?.DisplayName ?? categoryKey,
                        Score = a.Score,
                        Passed = a.Passed,
                        Threshold = a.PassingThreshold > 0 ? a.PassingThreshold : 70,
                        Date = a.EndTimestamp,
                        NextRetryDate = nextRetry
                    });
                }

                return new PaginatedResponse<AssessmentHistoryDTO>
                {
                    Items = history,
                    TotalCount = totalCount,
                    Page = page,
                    PageSize = pageSize,
                    HasMore = (page * pageSize) < totalCount
                };
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting assessment history for caregiver: {CaregiverId}", caregiverId);
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
                ServiceCategory = assessment.ServiceCategory,
                StartTimestamp = assessment.StartTimestamp,
                EndTimestamp = assessment.EndTimestamp,
                Score = assessment.Score,
                Passed = assessment.Passed,
                PassingThreshold = assessment.PassingThreshold,
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
