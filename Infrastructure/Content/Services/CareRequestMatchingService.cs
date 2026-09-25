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
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Infrastructure.Content.Services
{
    public class CareRequestMatchingService : ICareRequestMatchingService
    {
        private readonly CareProDbContext _dbContext;
        private readonly IGeocodingService _geocodingService;
        private readonly IEligibilityService _eligibilityService;
        private readonly ILogger<CareRequestMatchingService> _logger;

        // Scoring weights (sum = 100)
        private const double WeightCategory = 25;
        private const double WeightProximity = 25;
        private const double WeightBudget = 15;
        private const double WeightRating = 15;
        private const double WeightPreference = 10;
        private const double WeightEngagement = 5;
        private const double WeightProfile = 5;

        private const double StrongMatchThreshold = 60;
        private const int MaxResults = 10;
        private const double DefaultMaxDistanceKm = 50;
        private const double RelaxedMaxDistanceKm = 100;

        public CareRequestMatchingService(
            CareProDbContext dbContext,
            IGeocodingService geocodingService,
            IEligibilityService eligibilityService,
            ILogger<CareRequestMatchingService> logger)
        {
            _dbContext = dbContext;
            _geocodingService = geocodingService;
            _eligibilityService = eligibilityService;
            _logger = logger;
        }

        // ─────────────────────────────────────────────────────────────
        //  Phase 4: internal assignment entry point
        //
        //  Reuses the exact scoring pipeline (RunMatchingPipelineAsync) and the
        //  existing IEligibilityService checks — no changes to either. Adds only a
        //  hard filter on the package's RequiredCaregiverType / RequiredSpecialty.
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
            // Phase 1: Hard Filters — get candidate caregivers
            var candidates = await GetCandidateCaregivers(careRequest.ServiceCategory);

            // Phase 4: internal package assignment adds a hard filter on the package's
            // RequiredCaregiverType / RequiredSpecialty. Scoring and eligibility below are unchanged.
            if (extraHardFilter != null)
                candidates = candidates.Where(extraHardFilter).ToList();

            _logger.LogInformation("Phase 1: {Count} candidates after hard filters for category '{Category}'",
                candidates.Count, careRequest.ServiceCategory);

            if (candidates.Count == 0)
                return new List<CaregiverMatchDTO>();

            // Load supporting data in batch
            var candidateIds = candidates.Select(c => c.Id.ToString()).ToList();
            var allGigs = await _dbContext.Gigs
                .Where(g => candidateIds.Contains(g.CaregiverId)
                            && (g.IsDeleted == null || g.IsDeleted == false)
                            && g.Status == "Active")
                .ToListAsync();

            var allReviews = await _dbContext.Reviews
                .Where(r => candidateIds.Contains(r.CaregiverId))
                .ToListAsync();

            var clientPreferences = await _dbContext.ClientPreferences
                .Where(p => p.ClientId == careRequest.ClientId)
                .FirstOrDefaultAsync();

            var recentOrders = await _dbContext.ClientOrders
                .Where(o => candidateIds.Contains(o.CaregiverId))
                .Select(o => new { o.CaregiverId, o.OrderCreatedAt })
                .ToListAsync();

            var parsedBudget = ParseBudget(careRequest.Budget);

            // Phase 2: Score each candidate
            var scoredMatches = new List<CaregiverMatchDTO>();

            foreach (var caregiver in candidates)
            {
                var cgId = caregiver.Id.ToString();
                var caregiverGigs = allGigs.Where(g => g.CaregiverId == cgId).ToList();
                var caregiverReviews = allReviews.Where(r => r.CaregiverId == cgId).ToList();
                var caregiverOrders = recentOrders.Where(o => o.CaregiverId == cgId).ToList();

                // Must have at least one gig in the category
                var matchingGigs = caregiverGigs
                    .Where(g => string.Equals(g.Category, careRequest.ServiceCategory, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (matchingGigs.Count == 0) continue;

                // Check eligibility
                var eligibilityError = await _eligibilityService.ValidateGigEligibilityAsync(cgId, careRequest.ServiceCategory);
                if (eligibilityError != null) continue;

                // Calculate each score factor
                var categoryScore = CalculateCategoryScore(careRequest, matchingGigs);
                var proximityResult = CalculateProximityScore(caregiver, requestLat, requestLng, maxDistanceKm);
                if (proximityResult == null) continue; // Outside max distance or no coordinates

                var budgetScore = CalculateBudgetScore(matchingGigs, parsedBudget);
                var ratingScore = CalculateRatingScore(caregiverReviews);
                var preferenceScore = CalculatePreferenceScore(caregiverGigs, clientPreferences);
                var engagementScore = CalculateEngagementScore(caregiverGigs, caregiverOrders.Select(o => o.OrderCreatedAt).ToList());
                var profileScore = CalculateProfileScore(caregiver, caregiverGigs);

                var totalScore = (categoryScore * WeightCategory / 100)
                    + (proximityResult.Value.score * WeightProximity / 100)
                    + (budgetScore * WeightBudget / 100)
                    + (ratingScore * WeightRating / 100)
                    + (preferenceScore * WeightPreference / 100)
                    + (engagementScore * WeightEngagement / 100)
                    + (profileScore * WeightProfile / 100);

                // Normalize to 0-100
                var finalScore = Math.Round(totalScore * 100, 1);

                var bestGig = matchingGigs.OrderBy(g => g.Price).First();
                var avgRating = caregiverReviews.Count > 0 ? Math.Round(caregiverReviews.Average(r => r.Rating), 1) : 0;

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
                    GigTitle = bestGig.Title,
                    GigPrice = bestGig.Price,
                    DistanceKm = proximityResult.Value.distance,
                    AverageRating = avgRating,
                    ReviewCount = caregiverReviews.Count,
                    ScoreBreakdown = new MatchScoreBreakdownDTO
                    {
                        CategoryScore = Math.Round(categoryScore * WeightCategory, 1),
                        ProximityScore = Math.Round(proximityResult.Value.score * WeightProximity, 1),
                        BudgetScore = Math.Round(budgetScore * WeightBudget, 1),
                        RatingScore = Math.Round(ratingScore * WeightRating, 1),
                        PreferenceScore = Math.Round(preferenceScore * WeightPreference, 1),
                        EngagementScore = Math.Round(engagementScore * WeightEngagement, 1),
                        ProfileScore = Math.Round(profileScore * WeightProfile, 1)
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

        private double CalculateCategoryScore(TransientMatchRequest request, List<Gig> matchingGigs)
        {
            // Base: has matching category = 0.7
            double score = 0.7;

            // Bonus for subcategory/tag keyword overlap with request title/description
            var requestKeywords = ExtractKeywords(request.ServiceCategory + " " + (request.Notes ?? string.Empty));
            foreach (var gig in matchingGigs)
            {
                var gigKeywords = ExtractKeywords(gig.SubCategory + " " + gig.Tags + " " + gig.Title);
                var overlap = requestKeywords.Intersect(gigKeywords, StringComparer.OrdinalIgnoreCase).Count();
                if (overlap > 0)
                {
                    score += Math.Min(0.3, overlap * 0.1);
                    break;
                }
            }

            return Math.Min(1.0, score);
        }

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

        private double CalculateBudgetScore(List<Gig> matchingGigs, decimal? parsedBudget)
        {
            if (!parsedBudget.HasValue) return 0.7; // Neutral if no budget specified

            var lowestPrice = matchingGigs.Min(g => g.Price);
            if (lowestPrice <= (double)parsedBudget.Value)
                return 1.0; // Within budget

            var overPercentage = ((double)lowestPrice - (double)parsedBudget.Value) / (double)parsedBudget.Value;
            if (overPercentage <= 0.2) return 0.5; // Up to 20% over
            return 0.1; // More than 20% over
        }

        private double CalculateRatingScore(List<Review> reviews)
        {
            if (reviews.Count == 0) return 0.4; // Neutral for new caregivers

            var avgRating = reviews.Average(r => r.Rating);
            var ratingNormalized = avgRating / 5.0;
            var volumeFactor = Math.Min(1.0, reviews.Count / 10.0);

            return 0.7 * ratingNormalized + 0.3 * volumeFactor;
        }

        private double CalculatePreferenceScore(List<Gig> allCaregiverGigs, ClientPreference? preferences)
        {
            if (preferences == null || preferences.Data == null || preferences.Data.Count == 0)
                return 0.5; // Neutral

            var gigCategories = allCaregiverGigs
                .SelectMany(g => new[] { g.Category, g.SubCategory })
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(s => s.ToLower())
                .Distinct()
                .ToHashSet();

            var prefTags = preferences.Data.Select(d => d.ToLower()).ToHashSet();

            var intersection = gigCategories.Intersect(prefTags).Count();
            var union = gigCategories.Union(prefTags).Count();

            if (union == 0) return 0.5;
            return (double)intersection / union;
        }

        private double CalculateEngagementScore(List<Gig> gigs, List<DateTime> orderDates)
        {
            // Most recent activity
            var latestGigUpdate = gigs.Where(g => g.UpdatedOn.HasValue).Max(g => (DateTime?)g.UpdatedOn);
            var latestOrder = orderDates.Count > 0 ? (DateTime?)orderDates.Max() : null;

            var lastActivity = new[] { latestGigUpdate, latestOrder }
                .Where(d => d.HasValue)
                .Select(d => d!.Value)
                .DefaultIfEmpty(DateTime.MinValue)
                .Max();

            if (lastActivity == DateTime.MinValue) return 0.3; // No activity

            var daysSince = (DateTime.UtcNow - lastActivity).TotalDays;
            return Math.Max(0, 1.0 - daysSince / 90.0);
        }

        private double CalculateProfileScore(Caregiver caregiver, List<Gig> gigs)
        {
            double score = 0;
            if (!string.IsNullOrEmpty(caregiver.ProfileImage)) score += 0.2;
            if (!string.IsNullOrEmpty(caregiver.AboutMe)) score += 0.2;
            if (!string.IsNullOrEmpty(caregiver.IntroVideo)) score += 0.2;
            if (caregiver.Latitude.HasValue && caregiver.Longitude.HasValue) score += 0.2;
            if (gigs.Count > 0) score += 0.2;
            return score;
        }

        #endregion

        #region Helpers

        private static decimal? ParseBudget(string? budget)
        {
            if (string.IsNullOrEmpty(budget)) return null;
            var match = Regex.Match(budget, @"[\d,]+\.?\d*");
            if (match.Success && decimal.TryParse(match.Value.Replace(",", ""), out var value))
                return value;
            return null;
        }

        private static HashSet<string> ExtractKeywords(string text)
        {
            if (string.IsNullOrEmpty(text)) return new HashSet<string>();
            return text.Split(new[] { ' ', ',', ';', '-', '/', '&', '(', ')' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 2)
                .Select(w => w.ToLower())
                .ToHashSet();
        }

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
