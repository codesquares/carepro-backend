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
    public class CaregiverProfileService : ICaregiverProfileService
    {
        private readonly CareProDbContext db;
        private readonly ILogger<CaregiverProfileService> logger;

        private static readonly HashSet<string> AllowedDegreeTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "B.Sc", "HND", "OND", "M.Sc", "PhD", "Diploma", "Certificate", "Other"
        };

        private static readonly HashSet<string> AllowedEmploymentTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "Full-time", "Part-time", "Contract", "Self-employed", "Internship", "Volunteer"
        };

        public CaregiverProfileService(CareProDbContext db, ILogger<CaregiverProfileService> logger)
        {
            this.db = db;
            this.logger = logger;
        }

        // ───────────────── Helpers ─────────────────

        private static ObjectId ParseObjectId(string id, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(id) || !ObjectId.TryParse(id, out var oid))
                throw new ArgumentException($"Invalid {fieldName} '{id}'.");
            return oid;
        }

        private static void RequireCaregiverId(string caregiverId)
        {
            if (string.IsNullOrWhiteSpace(caregiverId))
                throw new ArgumentException("CaregiverId is required.");
        }

        private static void EnsureOwnership(string ownerId, string callerCaregiverId, string resource)
        {
            if (!string.Equals(ownerId, callerCaregiverId, StringComparison.Ordinal))
                throw new UnauthorizedAccessException($"You are not authorised to access this {resource}.");
        }

        private static void ValidateMonthRange(int? month, string fieldName)
        {
            if (month.HasValue && (month < 1 || month > 12))
                throw new ArgumentException($"{fieldName} must be between 1 and 12.");
        }

        private static void ValidatePeriod(int startMonth, int startYear, int? endMonth, int? endYear, bool ongoing, string ongoingFlagName)
        {
            ValidateMonthRange(startMonth, "Start month");
            ValidateMonthRange(endMonth, "End month");

            if (ongoing)
                return;

            if (!endMonth.HasValue || !endYear.HasValue)
                throw new ArgumentException($"End month and end year are required when {ongoingFlagName} is false.");

            var start = (startYear * 12) + startMonth;
            var end = (endYear.Value * 12) + endMonth.Value;
            if (end < start)
                throw new ArgumentException("End date cannot be earlier than start date.");
        }

        private static void ValidateExpiry(int issueMonth, int issueYear, int? expiryMonth, int? expiryYear, bool doesNotExpire)
        {
            ValidateMonthRange(issueMonth, "Issue month");
            ValidateMonthRange(expiryMonth, "Expiry month");

            if (doesNotExpire)
                return;

            if (!expiryMonth.HasValue || !expiryYear.HasValue)
                throw new ArgumentException("Expiry month and year are required when DoesNotExpire is false.");

            var issued = (issueYear * 12) + issueMonth;
            var expires = (expiryYear.Value * 12) + expiryMonth.Value;
            if (expires < issued)
                throw new ArgumentException("Expiry date cannot be earlier than issue date.");
        }

        private static void ValidateUrl(string? url, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) ||
                (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
            {
                throw new ArgumentException($"{fieldName} must be a valid http or https URL.");
            }
        }

        private static void ValidateAllowed(string value, HashSet<string> allowed, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value) || !allowed.Contains(value))
                throw new ArgumentException($"{fieldName} must be one of: {string.Join(", ", allowed)}.");
        }

        // ───────────────── Mappers ─────────────────

        private static CaregiverEducationResponse Map(CaregiverEducation e) => new()
        {
            Id = e.Id.ToString(),
            CaregiverId = e.CaregiverId,
            SchoolName = e.SchoolName,
            DegreeType = e.DegreeType,
            FieldOfStudy = e.FieldOfStudy,
            StartMonth = e.StartMonth,
            StartYear = e.StartYear,
            EndMonth = e.EndMonth,
            EndYear = e.EndYear,
            CurrentlyStudying = e.CurrentlyStudying,
            Grade = e.Grade,
            Activities = e.Activities,
            CreatedAt = e.CreatedAt,
            UpdatedAt = e.UpdatedAt,
        };

        private static CaregiverQualificationResponse Map(CaregiverQualification q) => new()
        {
            Id = q.Id.ToString(),
            CaregiverId = q.CaregiverId,
            CertificationName = q.CertificationName,
            IssuingOrganisation = q.IssuingOrganisation,
            IssueMonth = q.IssueMonth,
            IssueYear = q.IssueYear,
            ExpiryMonth = q.ExpiryMonth,
            ExpiryYear = q.ExpiryYear,
            DoesNotExpire = q.DoesNotExpire,
            CredentialId = q.CredentialId,
            CredentialUrl = q.CredentialUrl,
            CreatedAt = q.CreatedAt,
            UpdatedAt = q.UpdatedAt,
        };

        private static CaregiverWorkExperienceResponse Map(CaregiverWorkExperience w) => new()
        {
            Id = w.Id.ToString(),
            CaregiverId = w.CaregiverId,
            JobTitle = w.JobTitle,
            EmploymentType = w.EmploymentType,
            OrganisationName = w.OrganisationName,
            Location = w.Location,
            StartMonth = w.StartMonth,
            StartYear = w.StartYear,
            EndMonth = w.EndMonth,
            EndYear = w.EndYear,
            CurrentlyWorkingHere = w.CurrentlyWorkingHere,
            Industry = w.Industry,
            Description = w.Description,
            CreatedAt = w.CreatedAt,
            UpdatedAt = w.UpdatedAt,
        };

        // ─────────────────────── EDUCATION ───────────────────────

        public async Task<IEnumerable<CaregiverEducationResponse>> GetEducationAsync(string caregiverId)
        {
            RequireCaregiverId(caregiverId);
            var items = await db.CaregiverEducations
                .Where(e => e.CaregiverId == caregiverId)
                .ToListAsync();
            return items
                .OrderByDescending(e => e.CurrentlyStudying)
                .ThenByDescending(e => e.EndYear ?? int.MaxValue)
                .ThenByDescending(e => e.EndMonth ?? 12)
                .ThenByDescending(e => e.StartYear)
                .ThenByDescending(e => e.StartMonth)
                .Select(Map)
                .ToList();
        }

        public async Task<CaregiverEducationResponse> AddEducationAsync(string caregiverId, AddCaregiverEducationRequest request)
        {
            RequireCaregiverId(caregiverId);
            if (request == null) throw new ArgumentException("Request body is required.");

            ValidateAllowed(request.DegreeType, AllowedDegreeTypes, "DegreeType");
            ValidatePeriod(request.StartMonth, request.StartYear, request.EndMonth, request.EndYear,
                request.CurrentlyStudying, nameof(request.CurrentlyStudying));

            var now = DateTime.UtcNow;
            var entity = new CaregiverEducation
            {
                Id = ObjectId.GenerateNewId(),
                CaregiverId = caregiverId,
                SchoolName = request.SchoolName.Trim(),
                DegreeType = request.DegreeType.Trim(),
                FieldOfStudy = request.FieldOfStudy.Trim(),
                StartMonth = request.StartMonth,
                StartYear = request.StartYear,
                EndMonth = request.CurrentlyStudying ? null : request.EndMonth,
                EndYear = request.CurrentlyStudying ? null : request.EndYear,
                CurrentlyStudying = request.CurrentlyStudying,
                Grade = string.IsNullOrWhiteSpace(request.Grade) ? null : request.Grade.Trim(),
                Activities = string.IsNullOrWhiteSpace(request.Activities) ? null : request.Activities.Trim(),
                CreatedAt = now,
                UpdatedAt = now,
            };

            db.CaregiverEducations.Add(entity);
            await db.SaveChangesAsync();
            logger.LogInformation("Education record {Id} created for caregiver {CaregiverId}", entity.Id, caregiverId);
            return Map(entity);
        }

        public async Task<CaregiverEducationResponse> UpdateEducationAsync(string caregiverId, string id, UpdateCaregiverEducationRequest request)
        {
            RequireCaregiverId(caregiverId);
            if (request == null) throw new ArgumentException("Request body is required.");
            var oid = ParseObjectId(id, "education id");

            var entity = await db.CaregiverEducations.FirstOrDefaultAsync(e => e.Id == oid)
                ?? throw new KeyNotFoundException($"Education record '{id}' not found.");

            EnsureOwnership(entity.CaregiverId, caregiverId, "education record");

            ValidateAllowed(request.DegreeType, AllowedDegreeTypes, "DegreeType");
            ValidatePeriod(request.StartMonth, request.StartYear, request.EndMonth, request.EndYear,
                request.CurrentlyStudying, nameof(request.CurrentlyStudying));

            entity.SchoolName = request.SchoolName.Trim();
            entity.DegreeType = request.DegreeType.Trim();
            entity.FieldOfStudy = request.FieldOfStudy.Trim();
            entity.StartMonth = request.StartMonth;
            entity.StartYear = request.StartYear;
            entity.EndMonth = request.CurrentlyStudying ? null : request.EndMonth;
            entity.EndYear = request.CurrentlyStudying ? null : request.EndYear;
            entity.CurrentlyStudying = request.CurrentlyStudying;
            entity.Grade = string.IsNullOrWhiteSpace(request.Grade) ? null : request.Grade.Trim();
            entity.Activities = string.IsNullOrWhiteSpace(request.Activities) ? null : request.Activities.Trim();
            entity.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync();
            return Map(entity);
        }

        public async Task DeleteEducationAsync(string caregiverId, string id)
        {
            RequireCaregiverId(caregiverId);
            var oid = ParseObjectId(id, "education id");

            var entity = await db.CaregiverEducations.FirstOrDefaultAsync(e => e.Id == oid)
                ?? throw new KeyNotFoundException($"Education record '{id}' not found.");

            EnsureOwnership(entity.CaregiverId, caregiverId, "education record");

            db.CaregiverEducations.Remove(entity);
            await db.SaveChangesAsync();
        }

        // ─────────────── CERTIFICATIONS / QUALIFICATIONS ───────────────

        public async Task<IEnumerable<CaregiverQualificationResponse>> GetQualificationsAsync(string caregiverId)
        {
            RequireCaregiverId(caregiverId);
            var items = await db.CaregiverQualifications
                .Where(q => q.CaregiverId == caregiverId)
                .ToListAsync();
            return items
                .OrderByDescending(q => q.IssueYear)
                .ThenByDescending(q => q.IssueMonth)
                .Select(Map)
                .ToList();
        }

        public async Task<CaregiverQualificationResponse> AddQualificationAsync(string caregiverId, AddCaregiverQualificationRequest request)
        {
            RequireCaregiverId(caregiverId);
            if (request == null) throw new ArgumentException("Request body is required.");

            ValidateExpiry(request.IssueMonth, request.IssueYear, request.ExpiryMonth, request.ExpiryYear, request.DoesNotExpire);
            ValidateUrl(request.CredentialUrl, nameof(request.CredentialUrl));

            var now = DateTime.UtcNow;
            var entity = new CaregiverQualification
            {
                Id = ObjectId.GenerateNewId(),
                CaregiverId = caregiverId,
                CertificationName = request.CertificationName.Trim(),
                IssuingOrganisation = request.IssuingOrganisation.Trim(),
                IssueMonth = request.IssueMonth,
                IssueYear = request.IssueYear,
                ExpiryMonth = request.DoesNotExpire ? null : request.ExpiryMonth,
                ExpiryYear = request.DoesNotExpire ? null : request.ExpiryYear,
                DoesNotExpire = request.DoesNotExpire,
                CredentialId = string.IsNullOrWhiteSpace(request.CredentialId) ? null : request.CredentialId.Trim(),
                CredentialUrl = string.IsNullOrWhiteSpace(request.CredentialUrl) ? null : request.CredentialUrl.Trim(),
                CreatedAt = now,
                UpdatedAt = now,
            };

            db.CaregiverQualifications.Add(entity);
            await db.SaveChangesAsync();
            logger.LogInformation("Qualification record {Id} created for caregiver {CaregiverId}", entity.Id, caregiverId);
            return Map(entity);
        }

        public async Task<CaregiverQualificationResponse> UpdateQualificationAsync(string caregiverId, string id, UpdateCaregiverQualificationRequest request)
        {
            RequireCaregiverId(caregiverId);
            if (request == null) throw new ArgumentException("Request body is required.");
            var oid = ParseObjectId(id, "qualification id");

            var entity = await db.CaregiverQualifications.FirstOrDefaultAsync(q => q.Id == oid)
                ?? throw new KeyNotFoundException($"Qualification record '{id}' not found.");

            EnsureOwnership(entity.CaregiverId, caregiverId, "qualification record");

            ValidateExpiry(request.IssueMonth, request.IssueYear, request.ExpiryMonth, request.ExpiryYear, request.DoesNotExpire);
            ValidateUrl(request.CredentialUrl, nameof(request.CredentialUrl));

            entity.CertificationName = request.CertificationName.Trim();
            entity.IssuingOrganisation = request.IssuingOrganisation.Trim();
            entity.IssueMonth = request.IssueMonth;
            entity.IssueYear = request.IssueYear;
            entity.ExpiryMonth = request.DoesNotExpire ? null : request.ExpiryMonth;
            entity.ExpiryYear = request.DoesNotExpire ? null : request.ExpiryYear;
            entity.DoesNotExpire = request.DoesNotExpire;
            entity.CredentialId = string.IsNullOrWhiteSpace(request.CredentialId) ? null : request.CredentialId.Trim();
            entity.CredentialUrl = string.IsNullOrWhiteSpace(request.CredentialUrl) ? null : request.CredentialUrl.Trim();
            entity.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync();
            return Map(entity);
        }

        public async Task DeleteQualificationAsync(string caregiverId, string id)
        {
            RequireCaregiverId(caregiverId);
            var oid = ParseObjectId(id, "qualification id");

            var entity = await db.CaregiverQualifications.FirstOrDefaultAsync(q => q.Id == oid)
                ?? throw new KeyNotFoundException($"Qualification record '{id}' not found.");

            EnsureOwnership(entity.CaregiverId, caregiverId, "qualification record");

            db.CaregiverQualifications.Remove(entity);
            await db.SaveChangesAsync();
        }

        // ─────────────────── WORK EXPERIENCE ───────────────────

        public async Task<IEnumerable<CaregiverWorkExperienceResponse>> GetWorkExperienceAsync(string caregiverId)
        {
            RequireCaregiverId(caregiverId);
            var items = await db.CaregiverWorkExperiences
                .Where(w => w.CaregiverId == caregiverId)
                .ToListAsync();
            return items
                .OrderByDescending(w => w.CurrentlyWorkingHere)
                .ThenByDescending(w => w.EndYear ?? int.MaxValue)
                .ThenByDescending(w => w.EndMonth ?? 12)
                .ThenByDescending(w => w.StartYear)
                .ThenByDescending(w => w.StartMonth)
                .Select(Map)
                .ToList();
        }

        public async Task<CaregiverWorkExperienceResponse> AddWorkExperienceAsync(string caregiverId, AddCaregiverWorkExperienceRequest request)
        {
            RequireCaregiverId(caregiverId);
            if (request == null) throw new ArgumentException("Request body is required.");

            ValidateAllowed(request.EmploymentType, AllowedEmploymentTypes, "EmploymentType");
            ValidatePeriod(request.StartMonth, request.StartYear, request.EndMonth, request.EndYear,
                request.CurrentlyWorkingHere, nameof(request.CurrentlyWorkingHere));

            var now = DateTime.UtcNow;
            var entity = new CaregiverWorkExperience
            {
                Id = ObjectId.GenerateNewId(),
                CaregiverId = caregiverId,
                JobTitle = request.JobTitle.Trim(),
                EmploymentType = request.EmploymentType.Trim(),
                OrganisationName = request.OrganisationName.Trim(),
                Location = request.Location.Trim(),
                StartMonth = request.StartMonth,
                StartYear = request.StartYear,
                EndMonth = request.CurrentlyWorkingHere ? null : request.EndMonth,
                EndYear = request.CurrentlyWorkingHere ? null : request.EndYear,
                CurrentlyWorkingHere = request.CurrentlyWorkingHere,
                Industry = string.IsNullOrWhiteSpace(request.Industry) ? null : request.Industry.Trim(),
                Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
                CreatedAt = now,
                UpdatedAt = now,
            };

            db.CaregiverWorkExperiences.Add(entity);
            await db.SaveChangesAsync();
            logger.LogInformation("Work experience record {Id} created for caregiver {CaregiverId}", entity.Id, caregiverId);
            return Map(entity);
        }

        public async Task<CaregiverWorkExperienceResponse> UpdateWorkExperienceAsync(string caregiverId, string id, UpdateCaregiverWorkExperienceRequest request)
        {
            RequireCaregiverId(caregiverId);
            if (request == null) throw new ArgumentException("Request body is required.");
            var oid = ParseObjectId(id, "work experience id");

            var entity = await db.CaregiverWorkExperiences.FirstOrDefaultAsync(w => w.Id == oid)
                ?? throw new KeyNotFoundException($"Work experience record '{id}' not found.");

            EnsureOwnership(entity.CaregiverId, caregiverId, "work experience record");

            ValidateAllowed(request.EmploymentType, AllowedEmploymentTypes, "EmploymentType");
            ValidatePeriod(request.StartMonth, request.StartYear, request.EndMonth, request.EndYear,
                request.CurrentlyWorkingHere, nameof(request.CurrentlyWorkingHere));

            entity.JobTitle = request.JobTitle.Trim();
            entity.EmploymentType = request.EmploymentType.Trim();
            entity.OrganisationName = request.OrganisationName.Trim();
            entity.Location = request.Location.Trim();
            entity.StartMonth = request.StartMonth;
            entity.StartYear = request.StartYear;
            entity.EndMonth = request.CurrentlyWorkingHere ? null : request.EndMonth;
            entity.EndYear = request.CurrentlyWorkingHere ? null : request.EndYear;
            entity.CurrentlyWorkingHere = request.CurrentlyWorkingHere;
            entity.Industry = string.IsNullOrWhiteSpace(request.Industry) ? null : request.Industry.Trim();
            entity.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
            entity.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync();
            return Map(entity);
        }

        public async Task DeleteWorkExperienceAsync(string caregiverId, string id)
        {
            RequireCaregiverId(caregiverId);
            var oid = ParseObjectId(id, "work experience id");

            var entity = await db.CaregiverWorkExperiences.FirstOrDefaultAsync(w => w.Id == oid)
                ?? throw new KeyNotFoundException($"Work experience record '{id}' not found.");

            EnsureOwnership(entity.CaregiverId, caregiverId, "work experience record");

            db.CaregiverWorkExperiences.Remove(entity);
            await db.SaveChangesAsync();
        }

        // ─────────────── PUBLIC READS (gig detail enrichment) ───────────────

        public Task<IEnumerable<CaregiverEducationResponse>> GetPublicEducationAsync(string caregiverId)
            => GetEducationAsync(caregiverId);

        public Task<IEnumerable<CaregiverQualificationResponse>> GetPublicQualificationsAsync(string caregiverId)
            => GetQualificationsAsync(caregiverId);

        public Task<IEnumerable<CaregiverWorkExperienceResponse>> GetPublicWorkExperienceAsync(string caregiverId)
            => GetWorkExperienceAsync(caregiverId);
    }
}
