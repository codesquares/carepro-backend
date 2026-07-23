using Application.DTOs;
using Application.Interfaces.Content;
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
    public class CaregiverReadinessService : ICaregiverReadinessService
    {
        private readonly CareProDbContext _dbContext;
        private readonly IEligibilityService _eligibilityService;
        private readonly ILogger<CaregiverReadinessService> _logger;

        public CaregiverReadinessService(
            CareProDbContext dbContext,
            IEligibilityService eligibilityService,
            ILogger<CaregiverReadinessService> logger)
        {
            _dbContext = dbContext;
            _eligibilityService = eligibilityService;
            _logger = logger;
        }

        public async Task<CaregiverReadinessResult> GetReadinessAsync(
            string caregiverId, string? category = null, bool? knownHasActiveGig = null)
        {
            if (!ObjectId.TryParse(caregiverId, out var caregiverOid))
            {
                return new CaregiverReadinessResult
                {
                    IsReady = false,
                    IneligibilityReasons = new List<string> { CaregiverReadinessReasons.NoActiveGig }
                };
            }

            var caregiver = await _dbContext.CareGivers.FindAsync(caregiverOid);
            if (caregiver == null)
            {
                return new CaregiverReadinessResult
                {
                    IsReady = false,
                    IneligibilityReasons = new List<string> { CaregiverReadinessReasons.NoActiveGig }
                };
            }

            var isIdentityVerified = caregiver.IsIdentityVerified == true;

            var hasActiveGig = knownHasActiveGig ?? await HasActiveGigAsync(caregiverId, category);

            // Assessment/certificate eligibility delegates to the existing EligibilityService,
            // which already treats "general" tier categories (or categories with no configured
            // ServiceRequirement) as requiring nothing beyond identity + gig — preserved here
            // deliberately so we don't over-gate categories admins haven't configured.
            var assessmentPassed = true;
            var certificateMissing = false;
            if (!string.IsNullOrWhiteSpace(category))
            {
                var eligibilityError = await _eligibilityService.ValidateGigEligibilityAsync(caregiverId, category);
                if (eligibilityError != null)
                {
                    assessmentPassed = !eligibilityError.Missing.Contains("assessment");
                    certificateMissing = eligibilityError.Missing.Contains("certificate");
                }
            }

            var reasons = new List<string>();
            if (!isIdentityVerified) reasons.Add(CaregiverReadinessReasons.NotIdentityVerified);
            if (!hasActiveGig) reasons.Add(CaregiverReadinessReasons.NoActiveGig);
            if (!assessmentPassed) reasons.Add(CaregiverReadinessReasons.AssessmentNotPassed);
            if (certificateMissing) reasons.Add(CaregiverReadinessReasons.CertificateMissing);

            return new CaregiverReadinessResult
            {
                IsReady = reasons.Count == 0,
                IneligibilityReasons = reasons,
                IsIdentityVerified = isIdentityVerified,
                HasActiveGig = hasActiveGig,
                AssessmentPassed = assessmentPassed
            };
        }

        public async Task<Dictionary<string, CaregiverReadinessResult>> GetReadinessBulkAsync(
            IEnumerable<string> caregiverIds, string? category)
        {
            var ids = caregiverIds.Distinct().ToList();
            if (ids.Count == 0) return new Dictionary<string, CaregiverReadinessResult>();

            var oids = ids
                .Select(id => ObjectId.TryParse(id, out var oid) ? oid : (ObjectId?)null)
                .Where(oid => oid.HasValue)
                .Select(oid => oid!.Value)
                .ToList();

            var caregivers = await _dbContext.CareGivers
                .Where(c => oids.Contains(c.Id))
                .ToListAsync();

            return await GetReadinessBulkAsync(caregivers, category);
        }

        public async Task<Dictionary<string, CaregiverReadinessResult>> GetReadinessBulkAsync(
            List<Domain.Entities.Caregiver> caregivers, string? category)
        {
            var result = new Dictionary<string, CaregiverReadinessResult>();
            var ids = caregivers.Select(c => c.Id.ToString()).Distinct().ToList();
            if (ids.Count == 0) return result;

            // Category is matched case-insensitively in-memory (mirrors CareRequestMatchingService),
            // since the Mongo LINQ provider doesn't reliably translate StringComparison overloads.
            var candidateGigs = await _dbContext.Gigs
                .Where(g => ids.Contains(g.CaregiverId)
                            && (g.IsDeleted == null || g.IsDeleted == false)
                            && g.Status == "Active")
                .ToListAsync();
            var caregiversWithActiveGig = (string.IsNullOrWhiteSpace(category)
                    ? candidateGigs
                    : candidateGigs.Where(g => string.Equals(g.Category, category, StringComparison.OrdinalIgnoreCase)))
                .Select(g => g.CaregiverId)
                .ToHashSet();

            foreach (var id in ids)
            {
                var caregiver = caregivers.FirstOrDefault(c => c.Id.ToString() == id);
                if (caregiver == null)
                {
                    result[id] = new CaregiverReadinessResult
                    {
                        IsReady = false,
                        IneligibilityReasons = new List<string> { CaregiverReadinessReasons.NoActiveGig }
                    };
                    continue;
                }

                var isIdentityVerified = caregiver.IsIdentityVerified == true;
                var hasActiveGig = caregiversWithActiveGig.Contains(id);

                var assessmentPassed = true;
                var certificateMissing = false;
                if (!string.IsNullOrWhiteSpace(category))
                {
                    var eligibilityError = await _eligibilityService.ValidateGigEligibilityAsync(id, category);
                    if (eligibilityError != null)
                    {
                        assessmentPassed = !eligibilityError.Missing.Contains("assessment");
                        certificateMissing = eligibilityError.Missing.Contains("certificate");
                    }
                }

                var reasons = new List<string>();
                if (!isIdentityVerified) reasons.Add(CaregiverReadinessReasons.NotIdentityVerified);
                if (!hasActiveGig) reasons.Add(CaregiverReadinessReasons.NoActiveGig);
                if (!assessmentPassed) reasons.Add(CaregiverReadinessReasons.AssessmentNotPassed);
                if (certificateMissing) reasons.Add(CaregiverReadinessReasons.CertificateMissing);

                result[id] = new CaregiverReadinessResult
                {
                    IsReady = reasons.Count == 0,
                    IneligibilityReasons = reasons,
                    IsIdentityVerified = isIdentityVerified,
                    HasActiveGig = hasActiveGig,
                    AssessmentPassed = assessmentPassed
                };
            }

            return result;
        }

        private async Task<bool> HasActiveGigAsync(string caregiverId, string? category)
        {
            var gigs = await _dbContext.Gigs
                .Where(g => g.CaregiverId == caregiverId
                            && (g.IsDeleted == null || g.IsDeleted == false)
                            && g.Status == "Active")
                .ToListAsync();

            if (string.IsNullOrWhiteSpace(category))
                return gigs.Count > 0;

            // Case-insensitive match, mirroring CareRequestMatchingService's category comparison.
            return gigs.Any(g => string.Equals(g.Category, category, StringComparison.OrdinalIgnoreCase));
        }
    }
}
