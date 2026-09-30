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
    public class CareRequestMatchingService : ICareRequestMatchingService
    {
        private readonly CareProDbContext _dbContext;
        private readonly IGeocodingService _geocodingService;
        private readonly ICaregiverReadinessService _readinessService;
        private readonly ILogger<CareRequestMatchingService> _logger;

        // Scoring weights (sum = 100). Proximity, experience and vetting-completeness only —
        // gigs, budget, rating, preferences, engagement and profile do not influence ranking.
        private const double WeightProximity = 45;
        private const double WeightExperience = 30;
        private const double WeightVetting = 25;

        private const double StrongMatchThreshold = 60;
        private const int MaxResults = 10;
        private const double DefaultMaxDistanceKm = 50;
        private const double RelaxedMaxDistanceKm = 100;

        public CareRequestMatchingService(
            CareProDbContext dbContext,
            IGeocodingService geocodingService,
            ICaregiverReadinessService readinessService,
            ILogger<CareRequestMatchingService> logger)
        {
            _dbContext = dbContext;
            _geocodingService = geocodingService;
            _readinessService = readinessService;
            _logger = logger;
        }

        // ─────────────────────────────────────────────────────────────
        //  Phase 4: internal assignment entry point
        //
        //  Hard-filters only on availability/active status and the package's
        //  RequiredCaregiverType / RequiredSpecialty (plus a distance cap when both sides
        //  have coordinates). Gig existence never excludes a caregiver, and unmet
        //  assessment/certificate requirements are surfaced on the result
        //  (AssessmentReady / ReadinessGaps), not used to hide the caregiver.
        //  Internal assignment picks a single caregiver — no persistence beyond the
        //  ranked candidate list, no competitive-flow notifications.
        // ─────────────────────────────────────────────────────────────
        public async Task<List<CaregiverMatchDTO>> FindCandidatesForPackageAsync(PackageAssignmentMatchQuery query)
        {
            if (query == null) throw new ArgumentException("Query is required.");
            if (string.IsNullOrWhiteSpace(query.ServiceCategory))
                throw new ArgumentException("ServiceCategory is required.");

            if (!Enum.TryParse<CaregiverType>(query.RequiredCaregiverType, ignoreCase: false, out var requiredType)
                || !Enum.IsDefined(typeof(CaregiverType), requiredType))
            {
                throw new ArgumentException(
                    "RequiredCaregiverType must be one of: AuxiliaryNurse, CHEW, RegisteredNurse.");
            }

            var transientRequest = new TransientMatchRequest
            {
                ClientId = query.ClientId ?? string.Empty,
                ServiceCategory = query.ServiceCategory,
                Location = query.Location,
                Latitude = query.Latitude,
                Longitude = query.Longitude,
                Budget = query.Budget?.ToString("0", System.Globalization.CultureInfo.InvariantCulture)
            };

            var (lat, lng) = await ResolveCoordinatesNoPersistAsync(transientRequest);

            Func<Caregiver, bool> hardFilter = c =>
                c.CaregiverType == requiredType
                && (string.IsNullOrWhiteSpace(query.RequiredSpecialty)
                    || string.Equals(c.Specialty?.Trim(), query.RequiredSpecialty.Trim(), StringComparison.OrdinalIgnoreCase));

            var matches = await RunMatchingPipelineAsync(transientRequest, lat, lng, DefaultMaxDistanceKm, hardFilter);

            if (matches.Count(m => m.MatchScore >= StrongMatchThreshold) < 3)
            {
                var relaxed = await RunMatchingPipelineAsync(transientRequest, lat, lng, RelaxedMaxDistanceKm, hardFilter);
                var seen = new HashSet<string>(matches.Select(m => m.CaregiverId));
                matches.AddRange(relaxed.Where(m => !seen.Contains(m.CaregiverId)));
                matches = matches.OrderByDescending(m => m.MatchScore).ToList();
            }

            var top = matches.Take(MaxResults).ToList();
            for (int i = 0; i < top.Count; i++) top[i].Rank = i + 1;

            _logger.LogInformation(
                "Package assignment matching: {Count} eligible {Type} candidates for category '{Category}'",
                top.Count, requiredType, query.ServiceCategory);
            return top;
        }

        private async Task<(double? lat, double? lng)> ResolveCoordinatesNoPersistAsync(TransientMatchRequest request)
        {
            if (request.Latitude.HasValue && request.Longitude.HasValue)
                return (request.Latitude, request.Longitude);

            if (!string.IsNullOrEmpty(request.Location))
            {
                try
                {
                    var geocode = await _geocodingService.GeocodeAsync(request.Location);
                    return (geocode.Latitude, geocode.Longitude);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to geocode package-request location '{Location}'", request.Location);
                }
            }

            if (ObjectId.TryParse(request.ClientId, out var clientOid))
            {
                var client = await _dbContext.Clients.FindAsync(clientOid);
                if (client?.Latitude != null && client?.Longitude != null)
                    return (client.Latitude, client.Longitude);
            }

            return (null, null);
        }

        #region Matching Pipeline

        private sealed class TransientMatchRequest
        {
            public string ClientId { get; set; } = string.Empty;
            public string ServiceCategory { get; set; } = string.Empty;
            public string? Location { get; set; }
            public double? Latitude { get; set; }
            public double? Longitude { get; set; }
            public string? Budget { get; set; }
            public string? Notes { get; set; }
        }

        private async Task<List<CaregiverMatchDTO>> RunMatchingPipelineAsync(
            TransientMatchRequest careRequest, double? requestLat, double? requestLng, double maxDistanceKm,
            Func<Caregiver, bool>? extraHardFilter = null)
        {
            // Hard filters: available, non-deleted, active caregivers of the required type/specialty.
            var candidates = await GetCandidateCaregivers(careRequest.ServiceCategory);
            if (extraHardFilter != null)
                candidates = candidates.Where(extraHardFilter).ToList();

            _logger.LogInformation("Phase 1: {Count} candidates after hard filters for category '{Category}'",
                candidates.Count, careRequest.ServiceCategory);

            if (candidates.Count == 0)
                return new List<CaregiverMatchDTO>();

            var candidateIds = candidates.Select(c => c.Id.ToString()).ToList();

            // Reviews are display-only (shown to staff); they do not affect the score.
            var allReviews = await _dbContext.Reviews
                .Where(r => candidateIds.Contains(r.CaregiverId))
                .ToListAsync();

            // Readiness is used twice, from this one batched read: as a visible flag on the result,
            // and (via vetting completeness) as a ranking input. It never excludes a candidate.
            var readiness = await _readinessService.GetReadinessBulkAsync(candidates, careRequest.ServiceCategory);

            var scoredMatches = new List<CaregiverMatchDTO>();

            foreach (var caregiver in candidates)
            {
                var cgId = caregiver.Id.ToString();

                var proximityResult = CalculateProximityScore(caregiver, requestLat, requestLng, maxDistanceKm);
                if (proximityResult == null) continue; // Outside max distance

                var experienceScore = CalculateExperienceScore(caregiver.ExperienceTier);
                var r = readiness[cgId];
                var vettingScore = CalculateVettingScore(r);

                var finalScore = Math.Round(
                    (proximityResult.Value.score * WeightProximity
                     + experienceScore * WeightExperience
                     + vettingScore * WeightVetting), 1);

                var caregiverReviews = allReviews.Where(x => x.CaregiverId == cgId).ToList();
                var avgRating = caregiverReviews.Count > 0 ? Math.Round(caregiverReviews.Average(x => x.Rating), 1) : 0;

                var gaps = new List<string>();
                if (!r.AssessmentPassed) gaps.Add("assessment");
                if (r.IneligibilityReasons.Contains(CaregiverReadinessReasons.CertificateMissing)) gaps.Add("certificate");

                scoredMatches.Add(new CaregiverMatchDTO
                {
                    CaregiverId = cgId,
                    CaregiverName = $"{caregiver.FirstName} {caregiver.LastName}",
                    ProfileImage = caregiver.ProfileImage,
                    IsAvailable = caregiver.IsAvailable,
                    AboutMe = caregiver.AboutMe,
                    Location = caregiver.ServiceAddress ?? caregiver.ServiceCity,
                    MatchScore = finalScore,
                    MatchedServiceCategory = careRequest.ServiceCategory,
                    AssessmentReady = gaps.Count == 0,
                    ReadinessGaps = gaps,
                    ReadinessMessage = gaps.Count == 0
                        ? null
                        : $"{careRequest.ServiceCategory}: {string.Join(" and ", gaps)} not yet completed",
                    DistanceKm = proximityResult.Value.distance,
                    AverageRating = avgRating,
                    ReviewCount = caregiverReviews.Count,
                    ScoreBreakdown = new MatchScoreBreakdownDTO
                    {
                        ProximityScore = Math.Round(proximityResult.Value.score * WeightProximity, 1),
                        ExperienceScore = Math.Round(experienceScore * WeightExperience, 1),
                        VettingScore = Math.Round(vettingScore * WeightVetting, 1)
                    }
                });
            }

            return scoredMatches.OrderByDescending(m => m.MatchScore).ToList();
        }

        private async Task<List<Caregiver>> GetCandidateCaregivers(string serviceCategory)
        {
            // Get all available, non-deleted caregivers
            return await _dbContext.CareGivers
                .Where(c => c.IsAvailable && !c.IsDeleted && c.Status)
                .ToListAsync();
        }

        #endregion

        #region Scoring Factors

        private (double score, double distance)? CalculateProximityScore(
            Caregiver caregiver, double? requestLat, double? requestLng, double maxDistanceKm)
        {
            if (!requestLat.HasValue || !requestLng.HasValue)
            {
                // No request coordinates — give neutral score if caregiver has location
                if (caregiver.Latitude.HasValue) return (0.5, 0);
                return (0.3, 0);
            }

            if (!caregiver.Latitude.HasValue || !caregiver.Longitude.HasValue)
                return (0.2, 0); // No caregiver coords — low but not disqualified

            var distance = CalculateHaversineDistance(
                requestLat.Value, requestLng.Value,
                caregiver.Latitude.Value, caregiver.Longitude.Value);

            if (distance > maxDistanceKm) return null; // Outside range

            // Score: 1.0 at 0km, degrades linearly to 0.1 at maxDistance
            var score = Math.Max(0.1, 1.0 - (distance / maxDistanceKm) * 0.9);
            return (score, Math.Round(distance, 2));
        }

        /// <summary>Senior 1.0, Mid 0.6, Junior 0.3. An unset tier is missing information, not a
        /// confirmed status, so it gets no benefit of the doubt: it scores as the lowest known tier.</summary>
        private static double CalculateExperienceScore(ExperienceTier? tier) => tier switch
        {
            ExperienceTier.Senior => 1.0,
            ExperienceTier.Mid => 0.6,
            ExperienceTier.Junior => 0.3,
            _ => 0.3
        };

        /// <summary>Fraction of the five vetting criteria satisfied (identity, two confirmed guarantors,
        /// address history, category assessment, category certificates). Active-gig existence is
        /// deliberately not counted. Proportional so "nearly there" outranks "barely started".</summary>
        private static double CalculateVettingScore(CaregiverReadinessResult r)
        {
            int met = 0;
            if (r.IsIdentityVerified) met++;
            if (r.HasTwoConfirmedGuarantors) met++;
            if (r.AddressHistoryComplete) met++;
            if (r.AssessmentPassed) met++;
            if (!r.IneligibilityReasons.Contains(CaregiverReadinessReasons.CertificateMissing)) met++;
            return met / 5.0;
        }

        #endregion

        #region Helpers

        private static double CalculateHaversineDistance(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371;
            var dLat = (lat2 - lat1) * (Math.PI / 180);
            var dLon = (lon2 - lon1) * (Math.PI / 180);
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(lat1 * (Math.PI / 180)) * Math.Cos(lat2 * (Math.PI / 180)) *
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return R * c;
        }

        #endregion
    }
}
