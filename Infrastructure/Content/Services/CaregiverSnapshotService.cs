using Application.DTOs;
using Application.Interfaces.Content;
using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;

namespace Infrastructure.Content.Services
{
    public class CaregiverSnapshotService : ICaregiverSnapshotService
    {
        private readonly CareProDbContext _context;
        private readonly ILogger<CaregiverSnapshotService> _logger;

        public CaregiverSnapshotService(CareProDbContext context, ILogger<CaregiverSnapshotService> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Full batch rebuild — loads all source collections once, computes and upserts
        /// every caregiver's snapshot. Called by CaregiverSnapshotProcessor every 15 minutes.
        /// </summary>
        public async Task RebuildAllSnapshotsAsync()
        {
            _logger.LogInformation("CaregiverSnapshotService: Starting full rebuild");

            // ── Load all source data in parallel ──────────────────────────────
            var caregivers = await _context.CareGivers
                .Where(c => !c.IsDeleted)
                .ToListAsync();

            if (!caregivers.Any())
            {
                _logger.LogInformation("CaregiverSnapshotService: No active caregivers found");
                return;
            }

            var assessmentsTask = _context.Assessments
                .Where(a => a.Passed)
                .ToListAsync();

            var certificationsTask = _context.Certifications
                .ToListAsync();

            var gigsTask = _context.Gigs
                .ToListAsync();

            var workExperienceTask = _context.CaregiverWorkExperiences
                .ToListAsync();

            var qualificationsTask = _context.CaregiverQualifications
                .ToListAsync();

            var educationTask = _context.CaregiverEducations
                .ToListAsync();

            var existingSnapshotsTask = _context.CaregiverJourneySnapshots
                .ToListAsync();

            await Task.WhenAll(
                assessmentsTask, certificationsTask, gigsTask,
                workExperienceTask, qualificationsTask, educationTask,
                existingSnapshotsTask);

            // ── Group by CaregiverId for O(1) lookup ──────────────────────────
            var passedAssessmentsByCaregiver = assessmentsTask.Result
                .GroupBy(a => a.CaregiverId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var certsByCaregiver = certificationsTask.Result
                .GroupBy(c => c.CaregiverId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var gigsByCaregiver = gigsTask.Result
                .GroupBy(g => g.CaregiverId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var workExpByCaregiver = workExperienceTask.Result
                .GroupBy(w => w.CaregiverId)
                .ToDictionary(g => g.Key, g => g.Count());

            var qualsByCaregiver = qualificationsTask.Result
                .GroupBy(q => q.CaregiverId)
                .ToDictionary(g => g.Key, g => g.Count());

            var educationByCaregiver = educationTask.Result
                .GroupBy(e => e.CaregiverId)
                .ToDictionary(g => g.Key, g => g.Count());

            var snapshotsByCaregiverId = existingSnapshotsTask.Result
                .ToDictionary(s => s.CaregiverId);

            // ── Compute and upsert ────────────────────────────────────────────
            int created = 0, updated = 0;

            foreach (var caregiver in caregivers)
            {
                var caregiverId = caregiver.Id.ToString();

                try
                {
                    var passedAssessments = passedAssessmentsByCaregiver.GetValueOrDefault(caregiverId)
                        ?? new List<Assessment>();
                    var certs = certsByCaregiver.GetValueOrDefault(caregiverId)
                        ?? new List<Certification>();
                    var gigs = gigsByCaregiver.GetValueOrDefault(caregiverId)
                        ?? new List<Gig>();

                    var passedCategories = passedAssessments
                        .Where(a => !string.IsNullOrEmpty(a.ServiceCategory))
                        .Select(a => a.ServiceCategory!)
                        .Distinct()
                        .ToList();

                    var latestAssessmentScore = passedAssessments.Count > 0
                        ? passedAssessments.OrderByDescending(a => a.AssessedDate).First().Score
                        : (int?)null;

                    var certsUploaded = certs.Count;
                    var certsVerified = certs.Count(c => c.IsVerified);

                    var gigsPublished = gigs.Count(g =>
                        (g.Status == "Published" || g.Status == "Active") && g.IsDeleted != true);
                    var gigsDraft = gigs.Count(g =>
                        g.Status == "Draft" && g.IsDeleted != true);
                    var gigsDeleted = gigs.Count(g => g.IsDeleted == true);

                    var hasWorkExp = workExpByCaregiver.GetValueOrDefault(caregiverId) > 0;
                    var hasQuals = qualsByCaregiver.GetValueOrDefault(caregiverId) > 0;
                    var hasEdu = educationByCaregiver.GetValueOrDefault(caregiverId) > 0;

                    var journeyStage = ComputeJourneyStage(
                        caregiver, passedAssessments.Count > 0, certsVerified > 0, gigsPublished > 0);

                    if (snapshotsByCaregiverId.TryGetValue(caregiverId, out var existing))
                    {
                        // Update existing snapshot
                        existing.FirstName = caregiver.FirstName;
                        existing.LastName = caregiver.LastName;
                        existing.Email = caregiver.Email;
                        existing.PhoneNo = caregiver.PhoneNo;
                        existing.ServiceCity = caregiver.ServiceCity;
                        existing.ServiceState = caregiver.ServiceState;
                        existing.AuthProvider = caregiver.AuthProvider;
                        existing.CaregiverCreatedAt = caregiver.CreatedAt;
                        existing.IsIdentityVerified = caregiver.IsIdentityVerified ?? false;
                        existing.IdentityVerificationStatus = caregiver.IdentityVerificationStatus;
                        existing.IdentityVerifiedAt = caregiver.IdentityVerifiedAt;
                        existing.HasPassedAnyAssessment = passedAssessments.Count > 0;
                        existing.PassedAssessmentCategories = passedCategories;
                        existing.LatestAssessmentScore = latestAssessmentScore;
                        existing.CertificatesUploadedCount = certsUploaded;
                        existing.CertificatesVerifiedCount = certsVerified;
                        existing.HasProfilePicture = !string.IsNullOrEmpty(caregiver.ProfileImage);
                        existing.HasAboutMe = !string.IsNullOrWhiteSpace(caregiver.AboutMe);
                        existing.HasWorkExperience = hasWorkExp;
                        existing.HasQualifications = hasQuals;
                        existing.HasEducation = hasEdu;
                        existing.GigsDraftCount = gigsDraft;
                        existing.GigsPublishedCount = gigsPublished;
                        existing.GigsDeletedCount = gigsDeleted;
                        existing.JourneyStage = journeyStage;
                        existing.LastRebuildAt = DateTime.UtcNow;
                        _context.CaregiverJourneySnapshots.Update(existing);
                        updated++;
                    }
                    else
                    {
                        // Create new snapshot
                        var snapshot = new CaregiverJourneySnapshot
                        {
                            CaregiverId = caregiverId,
                            FirstName = caregiver.FirstName,
                            LastName = caregiver.LastName,
                            Email = caregiver.Email,
                            PhoneNo = caregiver.PhoneNo,
                            ServiceCity = caregiver.ServiceCity,
                            ServiceState = caregiver.ServiceState,
                            AuthProvider = caregiver.AuthProvider,
                            CaregiverCreatedAt = caregiver.CreatedAt,
                            IsIdentityVerified = caregiver.IsIdentityVerified ?? false,
                            IdentityVerificationStatus = caregiver.IdentityVerificationStatus,
                            IdentityVerifiedAt = caregiver.IdentityVerifiedAt,
                            HasPassedAnyAssessment = passedAssessments.Count > 0,
                            PassedAssessmentCategories = passedCategories,
                            LatestAssessmentScore = latestAssessmentScore,
                            CertificatesUploadedCount = certsUploaded,
                            CertificatesVerifiedCount = certsVerified,
                            HasProfilePicture = !string.IsNullOrEmpty(caregiver.ProfileImage),
                            HasAboutMe = !string.IsNullOrWhiteSpace(caregiver.AboutMe),
                            HasWorkExperience = hasWorkExp,
                            HasQualifications = hasQuals,
                            HasEducation = hasEdu,
                            GigsDraftCount = gigsDraft,
                            GigsPublishedCount = gigsPublished,
                            GigsDeletedCount = gigsDeleted,
                            JourneyStage = journeyStage,
                            LastRebuildAt = DateTime.UtcNow
                        };
                        _context.CaregiverJourneySnapshots.Add(snapshot);
                        created++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "CaregiverSnapshotService: Failed to process caregiver {CaregiverId}", caregiverId);
                }
            }

            await _context.SaveChangesAsync();
            _logger.LogInformation(
                "CaregiverSnapshotService: Rebuild complete — {Created} created, {Updated} updated",
                created, updated);
        }

        public async Task<CaregiverSnapshotResponse> GetSnapshotsAsync(CaregiverSnapshotQuery query)
        {
            var dbQuery = _context.CaregiverJourneySnapshots.AsQueryable();

            if (!string.IsNullOrWhiteSpace(query.JourneyStage))
                dbQuery = dbQuery.Where(s => s.JourneyStage == query.JourneyStage);

            if (query.IsIdentityVerified.HasValue)
                dbQuery = dbQuery.Where(s => s.IsIdentityVerified == query.IsIdentityVerified.Value);

            if (query.HasProfilePicture.HasValue)
                dbQuery = dbQuery.Where(s => s.HasProfilePicture == query.HasProfilePicture.Value);

            if (query.HasPassedAssessment.HasValue)
                dbQuery = dbQuery.Where(s => s.HasPassedAnyAssessment == query.HasPassedAssessment.Value);

            if (query.HasPublishedGig.HasValue)
            {
                if (query.HasPublishedGig.Value)
                    dbQuery = dbQuery.Where(s => s.GigsPublishedCount > 0);
                else
                    dbQuery = dbQuery.Where(s => s.GigsPublishedCount == 0);
            }

            if (query.HasCertificate.HasValue)
            {
                if (query.HasCertificate.Value)
                    dbQuery = dbQuery.Where(s => s.CertificatesUploadedCount > 0);
                else
                    dbQuery = dbQuery.Where(s => s.CertificatesUploadedCount == 0);
            }

            if (query.RegisteredFrom.HasValue)
                dbQuery = dbQuery.Where(s => s.CaregiverCreatedAt >= query.RegisteredFrom.Value);

            if (query.RegisteredTo.HasValue)
                dbQuery = dbQuery.Where(s => s.CaregiverCreatedAt <= query.RegisteredTo.Value);

            var all = await dbQuery.ToListAsync();

            var byStage = all
                .GroupBy(s => s.JourneyStage)
                .ToDictionary(g => g.Key, g => g.Count());

            var pageSize = Math.Max(1, Math.Min(query.PageSize, 200));
            var pageNumber = Math.Max(1, query.PageNumber);

            var paged = all
                .OrderBy(s => s.CaregiverCreatedAt)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .Select(s => new CaregiverJourneySnapshotDTO
                {
                    CaregiverId = s.CaregiverId,
                    FirstName = s.FirstName,
                    LastName = s.LastName,
                    Email = s.Email,
                    PhoneNo = s.PhoneNo,
                    ServiceCity = s.ServiceCity,
                    ServiceState = s.ServiceState,
                    AuthProvider = s.AuthProvider,
                    CaregiverCreatedAt = s.CaregiverCreatedAt,
                    IsIdentityVerified = s.IsIdentityVerified,
                    IdentityVerificationStatus = s.IdentityVerificationStatus,
                    IdentityVerifiedAt = s.IdentityVerifiedAt,
                    HasPassedAnyAssessment = s.HasPassedAnyAssessment,
                    PassedAssessmentCategories = s.PassedAssessmentCategories,
                    LatestAssessmentScore = s.LatestAssessmentScore,
                    CertificatesUploadedCount = s.CertificatesUploadedCount,
                    CertificatesVerifiedCount = s.CertificatesVerifiedCount,
                    HasProfilePicture = s.HasProfilePicture,
                    HasAboutMe = s.HasAboutMe,
                    HasWorkExperience = s.HasWorkExperience,
                    HasQualifications = s.HasQualifications,
                    HasEducation = s.HasEducation,
                    GigsDraftCount = s.GigsDraftCount,
                    GigsPublishedCount = s.GigsPublishedCount,
                    GigsDeletedCount = s.GigsDeletedCount,
                    JourneyStage = s.JourneyStage,
                    LastRebuildAt = s.LastRebuildAt
                })
                .ToList();

            return new CaregiverSnapshotResponse
            {
                Snapshots = paged,
                Page = pageNumber,
                PageSize = pageSize,
                TotalCount = all.Count,
                ByJourneyStage = byStage
            };
        }

        // ── Journey stage computation ──────────────────────────────────────────

        private static string ComputeJourneyStage(
            Caregiver c, bool passedAssessment, bool certVerified, bool hasPublishedGig)
        {
            if (hasPublishedGig) return "Published";

            var isVerified = c.IsIdentityVerified == true;
            if (isVerified && passedAssessment && certVerified) return "ReadyToPublish";
            if (passedAssessment) return "AssessmentPassed";
            if (isVerified) return "Verified";

            var hasProfessionalData =
                !string.IsNullOrWhiteSpace(c.AboutMe);   // base proxy; full check is cross-collection

            if (!string.IsNullOrEmpty(c.ProfileImage) || !string.IsNullOrWhiteSpace(c.AboutMe))
                return "ProfileStarted";

            return "Registered";
        }
    }
}
