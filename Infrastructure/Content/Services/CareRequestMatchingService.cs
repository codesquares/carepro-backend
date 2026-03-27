using Application.DTOs;
using Application.Interfaces.Content;
using Application.Interfaces.Email;
using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
        private readonly IClientRecommendationService _recommendationService;
        private readonly INotificationService _notificationService;
        private readonly IEmailService _emailService;
        private readonly IConfiguration _configuration;
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
            IClientRecommendationService recommendationService,
            INotificationService notificationService,
            IEmailService emailService,
            IConfiguration configuration,
            ILogger<CareRequestMatchingService> logger)
        {
            _dbContext = dbContext;
            _geocodingService = geocodingService;
            _eligibilityService = eligibilityService;
            _recommendationService = recommendationService;
            _notificationService = notificationService;
            _emailService = emailService;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<CareRequestMatchResponse> FindMatchesForCareRequestAsync(string careRequestId)
        {
            _logger.LogInformation("Starting matching for CareRequest {CareRequestId}", careRequestId);

            if (!ObjectId.TryParse(careRequestId, out var objectId))
                throw new ArgumentException("Invalid care request ID format.");

            var careRequest = await _dbContext.CareRequests.FindAsync(objectId);
            if (careRequest == null)
                throw new KeyNotFoundException($"Care request '{careRequestId}' not found.");

            if (careRequest.Status != "pending" && careRequest.Status != "unmatched")
            {
                _logger.LogInformation("CareRequest {CareRequestId} is not pending (status: {Status}), skipping matching", careRequestId, careRequest.Status);
                return new CareRequestMatchResponse
                {
                    Success = true,
                    Message = $"Care request is already '{careRequest.Status}'.",
                    CareRequestId = careRequestId,
                    Status = careRequest.Status
                };
            }

            // Resolve coordinates for the care request if not already set
            var (requestLat, requestLng) = await ResolveCoordinatesAsync(careRequest);

            // Run matching pipeline
            var matches = await RunMatchingPipelineAsync(careRequest, requestLat, requestLng, DefaultMaxDistanceKm);

            bool hasAlternatives = false;

            // If fewer than 3 strong matches, run relaxed pass
            if (matches.Count(m => m.MatchScore >= StrongMatchThreshold) < 3)
            {
                _logger.LogInformation("Fewer than 3 strong matches for CareRequest {CareRequestId}, running relaxed pass", careRequestId);
                var relaxedMatches = await RunMatchingPipelineAsync(careRequest, requestLat, requestLng, RelaxedMaxDistanceKm);

                // Merge: keep original strong matches, add new ones from relaxed pass
                var existingIds = new HashSet<string>(matches.Select(m => m.CaregiverId));
                var additional = relaxedMatches.Where(m => !existingIds.Contains(m.CaregiverId)).ToList();
                matches.AddRange(additional);
                matches = matches.OrderByDescending(m => m.MatchScore).ToList();
                hasAlternatives = additional.Count > 0;
            }

            // Take top 10 and assign ranks
            var topMatches = matches.Take(MaxResults).ToList();
            for (int i = 0; i < topMatches.Count; i++)
                topMatches[i].Rank = i + 1;

            // Persist to ClientRecommendation
            await StoreRecommendationsAsync(careRequest.ClientId, careRequestId, topMatches);

            // Update CareRequest status
            if (topMatches.Count > 0)
            {
                careRequest.Status = "matched";
                careRequest.MatchedAt = DateTime.UtcNow;
            }
            else
            {
                careRequest.Status = "unmatched";
            }
            careRequest.MatchCount = topMatches.Count;
            careRequest.UpdatedAt = DateTime.UtcNow;
            _dbContext.CareRequests.Update(careRequest);
            await _dbContext.SaveChangesAsync();

            // Send notifications
            await SendMatchNotificationsAsync(careRequest, topMatches, hasAlternatives);

            var response = new CareRequestMatchResponse
            {
                Success = true,
                CareRequestId = careRequestId,
                Status = careRequest.Status,
                TotalMatches = topMatches.Count,
                HasAlternatives = hasAlternatives,
                Matches = topMatches,
                Message = topMatches.Count > 0
                    ? $"Found {topMatches.Count} matching caregivers."
                    : "No matches found. The CarePro team is working to find the right caregiver for you."
            };

            _logger.LogInformation("Matching completed for CareRequest {CareRequestId}: {Count} matches", careRequestId, topMatches.Count);
            return response;
        }

        public async Task<CareRequestMatchResponse> GetMatchesForCareRequestAsync(string careRequestId, string requestingUserId)
        {
            if (!ObjectId.TryParse(careRequestId, out var objectId))
                throw new ArgumentException("Invalid care request ID format.");

            var careRequest = await _dbContext.CareRequests.FindAsync(objectId);
            if (careRequest == null)
                throw new KeyNotFoundException($"Care request '{careRequestId}' not found.");

            // Authorization: only the owning client can view matches
            if (careRequest.ClientId != requestingUserId)
            {
                // Check if user is admin
                if (!ObjectId.TryParse(requestingUserId, out var adminOid))
                    throw new UnauthorizedAccessException("You are not authorized to view these matches.");

                var isAdmin = await _dbContext.AdminUsers.AnyAsync(a => a.Id == adminOid);
                if (!isAdmin)
                    throw new UnauthorizedAccessException("You are not authorized to view these matches.");
            }

            // Get the latest recommendation for this client
            var recommendation = await _dbContext.ClientRecommendations
                .Where(r => r.ClientId == careRequest.ClientId && r.IsActive && !r.IsArchived
                            && r.PreferenceSnapshot == careRequestId)
                .OrderByDescending(r => r.CreatedAt)
                .FirstOrDefaultAsync();

            if (recommendation == null)
            {
                return new CareRequestMatchResponse
                {
                    Success = true,
                    Message = careRequest.Status == "pending"
                        ? "Matching is still in progress. Please check back shortly."
                        : "No matches have been generated for this care request.",
                    CareRequestId = careRequestId,
                    Status = careRequest.Status,
                    TotalMatches = 0
                };
            }

            // Mark as viewed
            if (!recommendation.ViewedAt.HasValue)
            {
                recommendation.ViewedAt = DateTime.UtcNow;
                _dbContext.ClientRecommendations.Update(recommendation);
                await _dbContext.SaveChangesAsync();
            }

            // Build match DTOs from stored recommendations
            var matches = new List<CaregiverMatchDTO>();
            int rank = 1;
            foreach (var item in recommendation.Recommendations.OrderByDescending(r => r.MatchScore))
            {
                var caregiverId = item.CaregiverId ?? item.ProviderId;
                var caregiver = await GetCaregiverBasicInfo(caregiverId);

                matches.Add(new CaregiverMatchDTO
                {
                    Rank = rank++,
                    CaregiverId = caregiverId,
                    CaregiverName = caregiver?.Name ?? "Caregiver",
                    ProfileImage = caregiver?.ProfileImage,
                    IsAvailable = caregiver?.IsAvailable ?? false,
                    AboutMe = caregiver?.AboutMe,
                    Location = item.Location,
                    MatchScore = item.MatchScore,
                    MatchedServiceCategory = item.ServiceType,
                    GigPrice = (int?)item.Price,
                    AverageRating = item.Rating,
                    ReviewCount = item.ReviewCount
                });
            }

            return new CareRequestMatchResponse
            {
                Success = true,
                Message = $"Found {matches.Count} matches for your care request.",
                CareRequestId = careRequestId,
                Status = careRequest.Status,
                TotalMatches = matches.Count,
                Matches = matches
            };
        }

        #region Matching Pipeline

        private async Task<List<CaregiverMatchDTO>> RunMatchingPipelineAsync(
            CareRequest careRequest, double? requestLat, double? requestLng, double maxDistanceKm)
        {
            // Phase 1: Hard Filters — get candidate caregivers
            var candidates = await GetCandidateCaregivers(careRequest.ServiceCategory);

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

        private double CalculateCategoryScore(CareRequest request, List<Gig> matchingGigs)
        {
            // Base: has matching category = 0.7
            double score = 0.7;

            // Bonus for subcategory/tag keyword overlap with request title/description
            var requestKeywords = ExtractKeywords(request.Title + " " + (request.Notes ?? string.Empty));
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

        private async Task<(double? lat, double? lng)> ResolveCoordinatesAsync(CareRequest careRequest)
        {
            // If already geocoded, use stored coordinates
            if (careRequest.Latitude.HasValue && careRequest.Longitude.HasValue)
                return (careRequest.Latitude, careRequest.Longitude);

            // Try to geocode the care request's Location field
            if (!string.IsNullOrEmpty(careRequest.Location))
            {
                try
                {
                    var geocode = await _geocodingService.GeocodeAsync(careRequest.Location);
                    careRequest.Latitude = geocode.Latitude;
                    careRequest.Longitude = geocode.Longitude;
                    _dbContext.CareRequests.Update(careRequest);
                    await _dbContext.SaveChangesAsync();
                    return (geocode.Latitude, geocode.Longitude);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to geocode CareRequest location '{Location}'", careRequest.Location);
                }
            }

            // Fall back to client's stored coordinates
            if (ObjectId.TryParse(careRequest.ClientId, out var clientOid))
            {
                var client = await _dbContext.Clients.FindAsync(clientOid);
                if (client?.Latitude != null && client?.Longitude != null)
                    return (client.Latitude, client.Longitude);
            }

            return (null, null);
        }

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

        private async Task StoreRecommendationsAsync(string clientId, string careRequestId, List<CaregiverMatchDTO> matches)
        {
            // Archive existing recommendations for this care request
            var existing = await _dbContext.ClientRecommendations
                .Where(r => r.ClientId == clientId && r.PreferenceSnapshot == careRequestId && r.IsActive)
                .ToListAsync();

            foreach (var rec in existing)
            {
                rec.IsActive = false;
                rec.IsArchived = true;
                rec.ArchivedAt = DateTime.UtcNow;
                _dbContext.ClientRecommendations.Update(rec);
            }

            var recommendation = new ClientRecommendation
            {
                Id = ObjectId.GenerateNewId(),
                ClientId = clientId,
                Recommendations = matches.Select(m => new RecommendationItem
                {
                    ProviderId = m.CaregiverId,
                    CaregiverId = m.CaregiverId,
                    MatchScore = m.MatchScore,
                    ServiceType = m.MatchedServiceCategory,
                    Location = m.Location ?? "",
                    Price = m.GigPrice ?? 0,
                    PriceUnit = "NGN",
                    Rating = m.AverageRating,
                    ReviewCount = m.ReviewCount
                }).ToList(),
                GeneratedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                IsActive = true,
                IsArchived = false,
                PreferenceSnapshot = careRequestId
            };

            await _dbContext.ClientRecommendations.AddAsync(recommendation);
            await _dbContext.SaveChangesAsync();
        }

        private async Task SendMatchNotificationsAsync(CareRequest careRequest, List<CaregiverMatchDTO> matches, bool hasAlternatives)
        {
            var careRequestId = careRequest.Id.ToString();

            if (matches.Count > 0)
            {
                // ── Notify matched CAREGIVERS (in-app + email) ──
                foreach (var match in matches)
                {
                    // Track notification in CareRequestNotifiedCaregivers
                    var alreadyNotified = await _dbContext.CareRequestNotifiedCaregivers
                        .AnyAsync(n => n.CareRequestId == careRequestId && n.CaregiverId == match.CaregiverId);

                    if (alreadyNotified) continue;

                    var notifiedRecord = new CareRequestNotifiedCaregiver
                    {
                        Id = MongoDB.Bson.ObjectId.GenerateNewId(),
                        CareRequestId = careRequestId,
                        CaregiverId = match.CaregiverId,
                        NotifiedAt = DateTime.UtcNow,
                        MatchScore = match.MatchScore
                    };
                    await _dbContext.CareRequestNotifiedCaregivers.AddAsync(notifiedRecord);

                    // In-app notification to caregiver
                    await _notificationService.CreateNotificationAsync(
                        match.CaregiverId,
                        "system",
                        NotificationTypes.CareRequestNewMatch,
                        $"A client needs {careRequest.ServiceCategory} in {careRequest.Location ?? "your area"}. Budget: {careRequest.Budget ?? "Not specified"}. Tap to view.",
                        "A new care request matches your profile",
                        careRequestId);

                    // Email notification to caregiver
                    try
                    {
                        await SendMatchNotificationEmailToCaregiverAsync(careRequest, match);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to send match email to caregiver {CaregiverId} for CareRequest {CareRequestId}",
                            match.CaregiverId, careRequestId);
                    }
                }

                await _dbContext.SaveChangesAsync();

                // Notify admins — match success (keep this)
                await NotifyAdminsAsync(
                    NotificationTypes.CareRequestAdminMatchUpdate,
                    $"Care request '{careRequest.Title}' matched {matches.Count} caregiver{(matches.Count > 1 ? "s" : "")}. Category: {careRequest.ServiceCategory}. Caregivers have been notified.",
                    "Care Request Matched",
                    careRequestId);
            }
            else
            {
                // No matches — only notify admins, NOT the client
                await NotifyAdminsAsync(
                    NotificationTypes.CareRequestAdminNoMatch,
                    $"No matches found for care request '{careRequest.Title}' (Category: {careRequest.ServiceCategory}, Location: {careRequest.Location ?? "Not specified"}). Review required.",
                    "No Match — Action Required",
                    careRequestId);

                try
                {
                    await SendNoMatchEmailToAdminsAsync(careRequest);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send no-match admin email for CareRequest {CareRequestId}.", careRequestId);
                }
            }
        }

        private async Task SendMatchNotificationEmailToCaregiverAsync(CareRequest careRequest, CaregiverMatchDTO match)
        {
            if (!ObjectId.TryParse(match.CaregiverId, out var caregiverOid)) return;
            var caregiver = await _dbContext.CareGivers.FindAsync(caregiverOid);
            if (caregiver == null) return;

            var subject = $"New Care Request Matches Your Profile — {careRequest.ServiceCategory}";
            var htmlContent = $@"
                <h3>Hi {caregiver.FirstName},</h3>
                <p>A client has posted a care request that matches your profile!</p>
                <div style='background-color: #f8f9fa; padding: 15px; border-radius: 5px; margin: 20px 0;'>
                    <p><strong>Request:</strong> {careRequest.Title}</p>
                    <p><strong>Category:</strong> {careRequest.ServiceCategory}</p>
                    <p><strong>Urgency:</strong> {careRequest.Urgency}</p>
                    <p><strong>Location:</strong> {careRequest.Location ?? "Not specified"}</p>
                    <p><strong>Budget:</strong> {careRequest.Budget ?? "Not specified"}</p>
                </div>
                <p>Log in to your dashboard to <strong>view the request details</strong> and respond if you're interested.</p>
                <p>— The CarePro Team</p>";

            await _emailService.SendGenericNotificationEmailAsync(caregiver.Email, caregiver.FirstName, subject, htmlContent);
        }

        private async Task NotifyAdminsAsync(string type, string content, string title, string relatedEntityId)
        {
            var admins = await _dbContext.AdminUsers.Where(a => !a.IsDeleted).ToListAsync();
            foreach (var admin in admins)
            {
                await _notificationService.CreateNotificationAsync(
                    admin.Id.ToString(), "system", type, content, title, relatedEntityId);
            }
        }

        // NOTE: SendMatchFoundEmailToClientAsync and SendNoMatchEmailToClientAsync removed.
        // Clients are no longer emailed on match/no-match. They only get notified when a caregiver responds.

        private async Task SendNoMatchEmailToAdminsAsync(CareRequest careRequest)
        {
            var admins = await _dbContext.AdminUsers.Where(a => !a.IsDeleted).ToListAsync();
            foreach (var admin in admins)
            {
                var subject = $"Action Required: No Match for Care Request '{careRequest.Title}'";
                var content = $@"
                    <p>A care request has <strong>no matches</strong> and needs attention.</p>
                    <div style='background-color: #fff3cd; padding: 15px; border-radius: 5px; margin: 15px 0; border-left: 4px solid #ffc107;'>
                        <p><strong>Request ID:</strong> {careRequest.Id}</p>
                        <p><strong>Client ID:</strong> {careRequest.ClientId}</p>
                        <p><strong>Title:</strong> {careRequest.Title}</p>
                        <p><strong>Category:</strong> {careRequest.ServiceCategory}</p>
                        <p><strong>Urgency:</strong> {careRequest.Urgency}</p>
                        <p><strong>Location:</strong> {careRequest.Location ?? "Not specified"}</p>
                        <p><strong>Budget:</strong> {careRequest.Budget ?? "Not specified"}</p>
                    </div>
                    <p>Please review and take action — the client has been notified that the team is working on it.</p>";

                await _emailService.SendGenericNotificationEmailAsync(admin.Email, admin.FirstName, subject, content);
            }
        }

        private async Task<(string Name, string? ProfileImage, bool IsAvailable, string? AboutMe)?> GetCaregiverBasicInfo(string caregiverId)
        {
            if (!ObjectId.TryParse(caregiverId, out var oid)) return null;
            var cg = await _dbContext.CareGivers.FindAsync(oid);
            if (cg == null) return null;
            return ($"{cg.FirstName} {cg.LastName}", cg.ProfileImage, cg.IsAvailable, cg.AboutMe);
        }

        #endregion
    }
}
