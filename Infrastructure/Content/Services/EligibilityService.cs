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
    public class EligibilityService : IEligibilityService
    {
        private readonly CareProDbContext careProDbContext;
        private readonly ILogger<EligibilityService> logger;

        public EligibilityService(
            CareProDbContext careProDbContext,
            ILogger<EligibilityService> logger)
        {
            this.careProDbContext = careProDbContext;
            this.logger = logger;
        }

        public async Task<EligibilityResponse> GetEligibilityAsync(string caregiverId)
        {
            try
            {
                // Get all service requirements
                var requirements = await careProDbContext.ServiceRequirements
                    .Where(sr => sr.Active)
                    .ToListAsync();

                // Get all assessments for this caregiver
                var assessments = await careProDbContext.Assessments
                    .Where(a => a.CaregiverId == caregiverId)
                    .ToListAsync();

                // Get all verified certificates for this caregiver
                var certificates = await careProDbContext.Certifications
                    .Where(c => c.CaregiverId == caregiverId)
                    .ToListAsync();

                var categories = new List<CategoryEligibilityDTO>();

                foreach (var req in requirements)
                {
                    var eligibility = EvaluateCategoryEligibility(
                        req, assessments, certificates);
                    categories.Add(eligibility);
                }

                return new EligibilityResponse
                {
                    CaregiverId = caregiverId,
                    Categories = categories
                };
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting eligibility for caregiver: {CaregiverId}", caregiverId);
                throw;
            }
        }

        public async Task<GigEligibilityError?> ValidateGigEligibilityAsync(string caregiverId, string gigCategory)
        {
            try
            {
                // Find the service requirement for this gig category
                // Find the service requirement for this gig category (case-insensitive)
                var allRequirements = await careProDbContext.ServiceRequirements
                    .Where(sr => sr.Active)
                    .ToListAsync();
                var requirement = allRequirements
                    .FirstOrDefault(sr => string.Equals(sr.ServiceCategory, gigCategory, StringComparison.OrdinalIgnoreCase));

                // If no requirement found, or it's a general tier, allow publishing
                if (requirement == null || requirement.Tier == "general")
                    return null;

                // Get assessments for this caregiver + category (case-insensitive)
                var allAssessments = await careProDbContext.Assessments
                    .Where(a => a.CaregiverId == caregiverId)
                    .ToListAsync();
                var assessments = allAssessments
                    .Where(a => string.Equals(a.ServiceCategory, gigCategory, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                // Get certificates for this caregiver
                var certificates = await careProDbContext.Certifications
                    .Where(c => c.CaregiverId == caregiverId)
                    .ToListAsync();

                var eligibility = EvaluateCategoryEligibility(requirement, assessments, certificates);

                if (eligibility.IsEligible)
                    return null;

                // Build error response
                var missing = new List<string>();
                if (!eligibility.AssessmentPassed)
                    missing.Add("assessment");
                if (!eligibility.CertificatesVerified)
                    missing.Add("certificate");

                var missingDetails = new List<string>();
                if (!eligibility.AssessmentPassed)
                {
                    missingDetails.Add(eligibility.AssessmentCompleted
                        ? $"Assessment completed but score ({eligibility.AssessmentScore}%) is below the required {requirement.PassingScore}%"
                        : $"Assessment for {requirement.DisplayName} has not been completed");
                }
                if (eligibility.MissingCertificates.Any())
                {
                    missingDetails.Add($"Missing certificates: {string.Join(", ", eligibility.MissingCertificates)}");
                }

                return new GigEligibilityError
                {
                    Error = "ELIGIBILITY_REQUIRED",
                    Missing = missing,
                    Category = gigCategory,
                    Message = $"Cannot publish gig in {requirement.DisplayName}. " +
                              string.Join(". ", missingDetails)
                };
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error validating gig eligibility for caregiver: {CaregiverId}, category: {Category}",
                    caregiverId, gigCategory);
                throw;
            }
        }

        public async Task<List<ServiceRequirementDTO>> GetAllServiceRequirementsAsync()
        {
            try
            {
                var requirements = await careProDbContext.ServiceRequirements
                    .Where(sr => sr.Active)
                    .ToListAsync();

                return requirements.Select(r => new ServiceRequirementDTO
                {
                    Id = r.Id.ToString(),
                    ServiceCategory = r.ServiceCategory,
                    DisplayName = r.DisplayName,
                    Tier = r.Tier,
                    RequiredCertificates = r.RequiredCertificates,
                    RequiredAssessment = r.RequiredAssessment,
                    PassingScore = r.PassingScore,
                    QuestionCount = r.QuestionCount,
                    CooldownHours = r.CooldownHours,
                    AssessmentValidityMonths = r.AssessmentValidityMonths,
                    SessionDurationMinutes = r.SessionDurationMinutes,
                    Active = r.Active,
                    CreatedAt = r.CreatedAt,
                    UpdatedAt = r.UpdatedAt
                }).ToList();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting all service requirements");
                throw;
            }
        }

        public async Task<ServiceRequirementDTO?> GetServiceRequirementByIdAsync(string id)
        {
            try
            {
                var objectId = ObjectId.Parse(id);
                var requirement = await careProDbContext.ServiceRequirements
                    .FirstOrDefaultAsync(sr => sr.Id == objectId);

                if (requirement == null) return null;

                return new ServiceRequirementDTO
                {
                    Id = requirement.Id.ToString(),
                    ServiceCategory = requirement.ServiceCategory,
                    DisplayName = requirement.DisplayName,
                    Tier = requirement.Tier,
                    RequiredCertificates = requirement.RequiredCertificates,
                    RequiredAssessment = requirement.RequiredAssessment,
                    PassingScore = requirement.PassingScore,
                    QuestionCount = requirement.QuestionCount,
                    CooldownHours = requirement.CooldownHours,
                    AssessmentValidityMonths = requirement.AssessmentValidityMonths,
                    SessionDurationMinutes = requirement.SessionDurationMinutes,
                    Active = requirement.Active,
                    CreatedAt = requirement.CreatedAt,
                    UpdatedAt = requirement.UpdatedAt
                };
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting service requirement by id: {Id}", id);
                throw;
            }
        }

        public async Task<ServiceRequirementDTO> CreateServiceRequirementAsync(AddServiceRequirementRequest request)
        {
            try
            {
                // Check if a requirement already exists for this service category
                var existing = await careProDbContext.ServiceRequirements
                    .FirstOrDefaultAsync(sr => sr.ServiceCategory == request.ServiceCategory);

                if (existing != null)
                    throw new ArgumentException($"A service requirement for category '{request.ServiceCategory}' already exists.");

                var entity = new ServiceRequirement
                {
                    ServiceCategory = request.ServiceCategory,
                    DisplayName = request.DisplayName,
                    Tier = request.Tier,
                    RequiredCertificates = request.RequiredCertificates ?? new List<string>(),
                    RequiredAssessment = request.RequiredAssessment,
                    PassingScore = request.PassingScore,
                    QuestionCount = request.QuestionCount,
                    CooldownHours = request.CooldownHours,
                    AssessmentValidityMonths = request.AssessmentValidityMonths,
                    SessionDurationMinutes = request.SessionDurationMinutes,
                    Active = request.Active,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                careProDbContext.ServiceRequirements.Add(entity);
                await careProDbContext.SaveChangesAsync();

                return new ServiceRequirementDTO
                {
                    Id = entity.Id.ToString(),
                    ServiceCategory = entity.ServiceCategory,
                    DisplayName = entity.DisplayName,
                    Tier = entity.Tier,
                    RequiredCertificates = entity.RequiredCertificates,
                    RequiredAssessment = entity.RequiredAssessment,
                    PassingScore = entity.PassingScore,
                    QuestionCount = entity.QuestionCount,
                    CooldownHours = entity.CooldownHours,
                    AssessmentValidityMonths = entity.AssessmentValidityMonths,
                    SessionDurationMinutes = entity.SessionDurationMinutes,
                    Active = entity.Active,
                    CreatedAt = entity.CreatedAt,
                    UpdatedAt = entity.UpdatedAt
                };
            }
            catch (ArgumentException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error creating service requirement for category: {Category}", request.ServiceCategory);
                throw;
            }
        }

        public async Task<ServiceRequirementDTO?> UpdateServiceRequirementAsync(string id, UpdateServiceRequirementRequest request)
        {
            try
            {
                var objectId = ObjectId.Parse(id);
                var entity = await careProDbContext.ServiceRequirements
                    .FirstOrDefaultAsync(sr => sr.Id == objectId);

                if (entity == null) return null;

                // Apply only provided fields
                if (request.DisplayName != null) entity.DisplayName = request.DisplayName;
                if (request.Tier != null) entity.Tier = request.Tier;
                if (request.RequiredCertificates != null) entity.RequiredCertificates = request.RequiredCertificates;
                if (request.RequiredAssessment != null) entity.RequiredAssessment = request.RequiredAssessment;
                if (request.PassingScore.HasValue) entity.PassingScore = request.PassingScore.Value;
                if (request.QuestionCount.HasValue) entity.QuestionCount = request.QuestionCount.Value;
                if (request.CooldownHours.HasValue) entity.CooldownHours = request.CooldownHours.Value;
                if (request.AssessmentValidityMonths.HasValue) entity.AssessmentValidityMonths = request.AssessmentValidityMonths.Value;
                if (request.SessionDurationMinutes.HasValue) entity.SessionDurationMinutes = request.SessionDurationMinutes.Value;
                if (request.Active.HasValue) entity.Active = request.Active.Value;
                entity.UpdatedAt = DateTime.UtcNow;

                careProDbContext.ServiceRequirements.Update(entity);
                await careProDbContext.SaveChangesAsync();

                return new ServiceRequirementDTO
                {
                    Id = entity.Id.ToString(),
                    ServiceCategory = entity.ServiceCategory,
                    DisplayName = entity.DisplayName,
                    Tier = entity.Tier,
                    RequiredCertificates = entity.RequiredCertificates,
                    RequiredAssessment = entity.RequiredAssessment,
                    PassingScore = entity.PassingScore,
                    QuestionCount = entity.QuestionCount,
                    CooldownHours = entity.CooldownHours,
                    AssessmentValidityMonths = entity.AssessmentValidityMonths,
                    SessionDurationMinutes = entity.SessionDurationMinutes,
                    Active = entity.Active,
                    CreatedAt = entity.CreatedAt,
                    UpdatedAt = entity.UpdatedAt
                };
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error updating service requirement: {Id}", id);
                throw;
            }
        }

        public async Task<bool> DeleteServiceRequirementAsync(string id)
        {
            try
            {
                var objectId = ObjectId.Parse(id);
                var entity = await careProDbContext.ServiceRequirements
                    .FirstOrDefaultAsync(sr => sr.Id == objectId);

                if (entity == null) return false;

                careProDbContext.ServiceRequirements.Remove(entity);
                await careProDbContext.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error deleting service requirement: {Id}", id);
                throw;
            }
        }

        private CategoryEligibilityDTO EvaluateCategoryEligibility(
            ServiceRequirement requirement,
            List<Assessment> assessments,
            List<Certification> certificates)
        {
            // Use RequiredAssessment field to match assessments (falls back to ServiceCategory if "General")
            var assessmentKey = requirement.RequiredAssessment;
            var categoryAssessments = assessments
                .Where(a => requirement.Tier == "general"
                    ? string.IsNullOrEmpty(a.ServiceCategory)
                    : string.Equals(a.ServiceCategory, assessmentKey, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(a => a.EndTimestamp)
                .ToList();

            var latestAttempt = categoryAssessments.FirstOrDefault();
            var bestPassingAttempt = categoryAssessments.FirstOrDefault(a => a.Passed);
            bool assessmentPassed = bestPassingAttempt != null;
            bool assessmentCompleted = latestAttempt != null;
            bool assessmentExpired = false;
            DateTime? assessmentExpiresAt = null;

            // Check if the passing assessment has expired (AssessmentValidityMonths > 0)
            if (assessmentPassed && requirement.AssessmentValidityMonths > 0 && bestPassingAttempt != null)
            {
                assessmentExpiresAt = bestPassingAttempt.EndTimestamp.AddMonths(requirement.AssessmentValidityMonths);
                if (assessmentExpiresAt <= DateTime.UtcNow)
                {
                    assessmentPassed = false;
                    assessmentExpired = true;
                }
            }

            // Check cooldown: look at the most recent failed attempt
            DateTime? cooldownUntil = null;
            var lastFailedAttempt = categoryAssessments.FirstOrDefault(a => !a.Passed);
            if (lastFailedAttempt != null)
            {
                var retryDate = lastFailedAttempt.EndTimestamp.AddHours(requirement.CooldownHours);
                if (retryDate > DateTime.UtcNow)
                    cooldownUntil = retryDate;
            }

            // Check certificates
            var missingCerts = new List<string>();
            bool certificatesVerified = true;

            if (requirement.RequiredCertificates?.Any() == true)
            {
                foreach (var requiredCert in requirement.RequiredCertificates)
                {
                    var hasCert = certificates.Any(c =>
                        string.Equals(c.CertificateName, requiredCert, StringComparison.OrdinalIgnoreCase)
                        && c.IsVerified
                        && c.VerificationStatus == DocumentVerificationStatus.Verified
                        && (c.ExpiryDate == null || c.ExpiryDate > DateTime.UtcNow));

                    if (!hasCert)
                    {
                        missingCerts.Add(requiredCert);
                        certificatesVerified = false;
                    }
                }
            }

            // For general tier, certificates may not be strictly required
            if (requirement.Tier == "general" && (requirement.RequiredCertificates == null || !requirement.RequiredCertificates.Any()))
            {
                certificatesVerified = true;
            }

            bool isEligible = assessmentPassed && certificatesVerified;

            return new CategoryEligibilityDTO
            {
                ServiceCategory = requirement.ServiceCategory,
                DisplayName = requirement.DisplayName,
                Tier = requirement.Tier,
                IsEligible = isEligible,
                AssessmentPassed = assessmentPassed,
                AssessmentExpired = assessmentExpired,
                CertificatesVerified = certificatesVerified,
                MissingCertificates = missingCerts,
                AssessmentCompleted = assessmentCompleted,
                AssessmentScore = bestPassingAttempt?.Score ?? latestAttempt?.Score,
                AssessmentCompletedAt = bestPassingAttempt?.EndTimestamp ?? latestAttempt?.EndTimestamp,
                AssessmentExpiresAt = assessmentExpiresAt,
                CooldownUntil = cooldownUntil
            };
        }
    }
}
