using Application.DTOs;
using Application.Interfaces.Content;
using Application.Interfaces.Email;
using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CareRequestResponseEntity = Domain.Entities.CareRequestResponse;

namespace Infrastructure.Content.Services
{
    public class CareRequestResponseService : ICareRequestResponseService
    {
        private readonly CareProDbContext _dbContext;
        private readonly INotificationService _notificationService;
        private readonly IEmailService _emailService;
        private readonly ILogger<CareRequestResponseService> _logger;

        private static readonly string[] BrowsableStatuses = new[] { "pending", "matched", "active" };

        public CareRequestResponseService(
            CareProDbContext dbContext,
            INotificationService notificationService,
            IEmailService emailService,
            ILogger<CareRequestResponseService> logger)
        {
            _dbContext = dbContext;
            _notificationService = notificationService;
            _emailService = emailService;
            _logger = logger;
        }

        // ─────────────────────────────────────────────────────────────
        //  Caregiver Responds to a Care Request
        // ─────────────────────────────────────────────────────────────

        public async Task<CareRequestRespondResult> RespondToRequestAsync(
            string careRequestId, string caregiverId, RespondToCareRequestDTO dto)
        {
            if (!ObjectId.TryParse(careRequestId, out var requestOid))
                throw new ArgumentException("Invalid care request ID format.");
            if (!ObjectId.TryParse(caregiverId, out _))
                throw new ArgumentException("Invalid caregiver ID format.");

            var careRequest = await _dbContext.CareRequests.FindAsync(requestOid);
            if (careRequest == null)
                throw new KeyNotFoundException("Care request not found.");

            var statusLower = careRequest.Status?.ToLower();
            if (statusLower != "pending" && statusLower != "matched" && statusLower != "active")
                throw new InvalidOperationException($"Cannot respond to a request with status '{careRequest.Status}'.");

            // Check for duplicate response
            var alreadyResponded = await _dbContext.CareRequestResponses
                .AnyAsync(r => r.CareRequestId == careRequestId && r.CaregiverId == caregiverId);
            if (alreadyResponded)
                throw new InvalidOperationException("You have already responded to this request.");

            // Soft enforcement: check if caregiver was notified (matched), attach score if so
            double? matchScore = null;
            var notified = await _dbContext.CareRequestNotifiedCaregivers
                .FirstOrDefaultAsync(n => n.CareRequestId == careRequestId && n.CaregiverId == caregiverId);
            if (notified != null)
                matchScore = notified.MatchScore;

            var response = new CareRequestResponseEntity
            {
                Id = ObjectId.GenerateNewId(),
                CareRequestId = careRequestId,
                CaregiverId = caregiverId,
                Status = "pending",
                Message = dto.Message,
                ProposedRate = dto.ProposedRate,
                MatchScore = matchScore,
                RespondedAt = DateTime.UtcNow
            };

            await _dbContext.CareRequestResponses.AddAsync(response);

            // Increment responders count
            careRequest.RespondersCount = (careRequest.RespondersCount ?? 0) + 1;
            careRequest.UpdatedAt = DateTime.UtcNow;
            _dbContext.CareRequests.Update(careRequest);

            await _dbContext.SaveChangesAsync();

            // Notify the client
            var caregiver = await _dbContext.CareGivers.FindAsync(ObjectId.Parse(caregiverId));
            var caregiverName = caregiver != null ? caregiver.FirstName : "A caregiver";

            await _notificationService.CreateNotificationAsync(
                careRequest.ClientId,
                caregiverId,
                NotificationTypes.CareRequestNewResponder,
                $"{caregiverName} is interested in your \"{careRequest.Title}\" request. View their profile.",
                $"{caregiverName} responded to your request",
                careRequestId);

            _logger.LogInformation("Caregiver {CaregiverId} responded to CareRequest {CareRequestId}", caregiverId, careRequestId);

            return new CareRequestRespondResult
            {
                Success = true,
                ResponseId = response.Id.ToString(),
                Message = "Your interest has been sent to the client."
            };
        }

        // ─────────────────────────────────────────────────────────────
        //  Client Request Detail with Responders Tabs
        // ─────────────────────────────────────────────────────────────

        public async Task<CareRequestDetailDTO> GetRequestDetailForClientAsync(string careRequestId, string clientId)
        {
            if (!ObjectId.TryParse(careRequestId, out var requestOid))
                throw new ArgumentException("Invalid care request ID format.");

            var careRequest = await _dbContext.CareRequests.FindAsync(requestOid);
            if (careRequest == null)
                throw new KeyNotFoundException("Care request not found.");

            if (careRequest.ClientId != clientId)
                throw new UnauthorizedAccessException("You are not authorized to view this request.");

            // Get all responses for this request
            var responses = await _dbContext.CareRequestResponses
                .Where(r => r.CareRequestId == careRequestId)
                .OrderByDescending(r => r.RespondedAt)
                .ToListAsync();

            // Build response cards with caregiver details
            var cards = new List<CaregiverResponseCardDTO>();
            foreach (var resp in responses)
            {
                var card = await BuildResponseCardAsync(resp);
                if (card != null) cards.Add(card);
            }

            var requestDto = MapToDTO(careRequest);

            return new CareRequestDetailDTO
            {
                Request = requestDto,
                Responders = new CareRequestRespondersDTO
                {
                    All = cards.Where(c => c.Status == "pending").ToList(),
                    Shortlisted = cards.Where(c => c.Status == "shortlisted").ToList(),
                    Hired = cards.Where(c => c.Status == "hired").ToList()
                },
                Counts = new CareRequestCountsDTO
                {
                    Responders = cards.Count(c => c.Status == "pending"),
                    Shortlisted = cards.Count(c => c.Status == "shortlisted"),
                    Hired = cards.Count(c => c.Status == "hired")
                }
            };
        }

        // ─────────────────────────────────────────────────────────────
        //  Shortlist / Remove Shortlist
        // ─────────────────────────────────────────────────────────────

        public async Task<ShortlistResult> ShortlistResponseAsync(string careRequestId, string responseId, string clientId)
        {
            var (careRequest, response) = await ValidateClientResponseAccessAsync(careRequestId, responseId, clientId);

            if (response.Status == "shortlisted")
                throw new InvalidOperationException("This responder is already shortlisted.");
            if (response.Status == "hired")
                throw new InvalidOperationException("This responder has already been hired.");

            response.Status = "shortlisted";
            response.ShortlistedAt = DateTime.UtcNow;
            _dbContext.CareRequestResponses.Update(response);
            await _dbContext.SaveChangesAsync();

            // Notify caregiver
            await _notificationService.CreateNotificationAsync(
                response.CaregiverId,
                clientId,
                NotificationTypes.CareRequestShortlisted,
                $"A client shortlisted you for their \"{careRequest.Title}\" request.",
                "You've been shortlisted!",
                careRequestId);

            return new ShortlistResult { Success = true, ResponseId = responseId, Status = "shortlisted" };
        }

        public async Task<ShortlistResult> RemoveShortlistAsync(string careRequestId, string responseId, string clientId)
        {
            var (_, response) = await ValidateClientResponseAccessAsync(careRequestId, responseId, clientId);

            if (response.Status != "shortlisted")
                throw new InvalidOperationException("This responder is not currently shortlisted.");

            response.Status = "pending";
            response.ShortlistedAt = null;
            _dbContext.CareRequestResponses.Update(response);
            await _dbContext.SaveChangesAsync();

            return new ShortlistResult { Success = true, ResponseId = responseId, Status = "pending" };
        }

        // ─────────────────────────────────────────────────────────────
        //  Hire Responder (Generate Special Gig)
        // ─────────────────────────────────────────────────────────────

        public async Task<HireResult> HireResponderAsync(string careRequestId, string responseId, string clientId)
        {
            var (careRequest, response) = await ValidateClientResponseAccessAsync(careRequestId, responseId, clientId);

            if (response.Status == "hired")
                throw new InvalidOperationException("This responder has already been hired.");

            // Check that only one can be hired per request
            var existingHire = await _dbContext.CareRequestResponses
                .AnyAsync(r => r.CareRequestId == careRequestId && r.Status == "hired");
            if (existingHire)
                throw new InvalidOperationException("You have already hired a caregiver for this request. Only one hire is allowed per request.");

            // Generate special gig from care request details
            var specialGig = new Gig
            {
                Id = ObjectId.GenerateNewId(),
                Title = $"[Care Request] {careRequest.Title}",
                Category = careRequest.ServiceCategory,
                SubCategory = careRequest.ServicePackageType ?? "General",
                Tags = string.Join(", ", careRequest.Tasks ?? new List<string>()),
                PackageType = careRequest.ServicePackageType ?? "Custom",
                PackageName = "Care Request Package",
                PackageDetails = careRequest.Tasks ?? new List<string>(),
                DeliveryTime = careRequest.Duration ?? "As agreed",
                Price = careRequest.BudgetMax.HasValue ? (int)careRequest.BudgetMax.Value
                      : careRequest.BudgetMin.HasValue ? (int)careRequest.BudgetMin.Value
                      : 0,
                Status = "Active",
                CaregiverId = response.CaregiverId,
                CreatedAt = DateTime.UtcNow,
                IsSpecialGig = true,
                CareRequestId = careRequestId,
                CareRequestResponseId = responseId,
                ScopedClientId = clientId
            };

            await _dbContext.Gigs.AddAsync(specialGig);

            // Update response status
            response.Status = "hired";
            response.HiredAt = DateTime.UtcNow;
            response.SpecialGigId = specialGig.Id.ToString();
            _dbContext.CareRequestResponses.Update(response);

            await _dbContext.SaveChangesAsync();

            // Notify caregiver
            var client = await _dbContext.Clients.FindAsync(ObjectId.Parse(clientId));
            var clientName = client?.FirstName ?? "A client";

            await _notificationService.CreateNotificationAsync(
                response.CaregiverId,
                clientId,
                NotificationTypes.CareRequestHired,
                $"{clientName} selected you for their \"{careRequest.Title}\" request. View the booking details and proceed.",
                "You've been selected!",
                specialGig.Id.ToString());

            // Send email to caregiver
            try
            {
                var caregiver = await _dbContext.CareGivers.FindAsync(ObjectId.Parse(response.CaregiverId));
                if (caregiver != null)
                {
                    var subject = $"You've been selected for \"{careRequest.Title}\"!";
                    var html = $@"
                        <h3>Congratulations {caregiver.FirstName}!</h3>
                        <p>{clientName} has selected you for their care request: <strong>{careRequest.Title}</strong>.</p>
                        <div style='background-color: #f8f9fa; padding: 15px; border-radius: 5px; margin: 20px 0;'>
                            <p><strong>Category:</strong> {careRequest.ServiceCategory}</p>
                            <p><strong>Location:</strong> {careRequest.Location ?? "Not specified"}</p>
                        </div>
                        <p>Log in to view the booking details and start the process.</p>
                        <p>— The CarePro Team</p>";
                    await _emailService.SendGenericNotificationEmailAsync(caregiver.Email, caregiver.FirstName, subject, html);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send hire email for response {ResponseId}", responseId);
            }

            _logger.LogInformation("Client {ClientId} hired caregiver {CaregiverId} for CareRequest {CareRequestId}. SpecialGig {GigId} created.",
                clientId, response.CaregiverId, careRequestId, specialGig.Id);

            return new HireResult
            {
                Success = true,
                ResponseId = responseId,
                SpecialGigId = specialGig.Id.ToString(),
                CaregiverId = response.CaregiverId,
                Message = "Caregiver has been hired. A special gig has been created for the booking process."
            };
        }

        // ─────────────────────────────────────────────────────────────
        //  Caregiver Browse — Get Matched Requests
        // ─────────────────────────────────────────────────────────────

        public async Task<CaregiverMatchedRequestsResponse> GetMatchedRequestsForCaregiverAsync(
            string caregiverId, string? serviceType, decimal? budgetMin, decimal? budgetMax,
            string? location, int page, int pageSize)
        {
            if (!ObjectId.TryParse(caregiverId, out var caregiverOid))
                throw new ArgumentException("Invalid caregiver ID format.");

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 50);

            // Get all browsable care requests (any caregiver can see and respond)
            // Use ToLower() for case-insensitive matching — existing DB records may vary in casing
            var query = _dbContext.CareRequests
                .Where(cr => cr.Status.ToLower() == "pending" || cr.Status.ToLower() == "matched" || cr.Status.ToLower() == "active");

            // Optional filters
            if (!string.IsNullOrEmpty(serviceType))
                query = query.Where(cr => cr.ServiceCategory == serviceType);

            if (budgetMin.HasValue)
                query = query.Where(cr => cr.BudgetMax >= budgetMin || cr.BudgetMax == null);

            if (budgetMax.HasValue)
                query = query.Where(cr => cr.BudgetMin <= budgetMax || cr.BudgetMin == null);

            if (!string.IsNullOrEmpty(location))
                query = query.Where(cr => cr.Location != null && cr.Location.Contains(location));

            var totalCount = await query.CountAsync();

            var requests = await query
                .OrderByDescending(cr => cr.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // Get caregiver's existing responses to check hasResponded
            var requestIds = requests.Select(r => r.Id.ToString()).ToList();
            var respondedIds = await _dbContext.CareRequestResponses
                .Where(r => r.CaregiverId == caregiverId && requestIds.Contains(r.CareRequestId))
                .Select(r => r.CareRequestId)
                .ToListAsync();
            var respondedSet = new HashSet<string>(respondedIds);

            // Build items with anonymized client info
            var items = new List<CaregiverMatchedRequestDTO>();
            foreach (var cr in requests)
            {
                string? clientFirstName = null;
                if (ObjectId.TryParse(cr.ClientId, out var clientOid))
                {
                    var client = await _dbContext.Clients.FindAsync(clientOid);
                    clientFirstName = client?.FirstName;
                }

                items.Add(new CaregiverMatchedRequestDTO
                {
                    Id = cr.Id.ToString(),
                    Title = cr.Title,
                    ServiceGroup = cr.ServiceGroup ?? string.Empty,
                    ServiceCategory = cr.ServiceCategory,
                    Location = cr.Location,
                    Budget = cr.Budget,
                    BudgetMin = cr.BudgetMin,
                    BudgetMax = cr.BudgetMax,
                    BudgetType = cr.BudgetType,
                    Urgency = cr.Urgency,
                    PostedAt = cr.CreatedAt,
                    RespondersCount = cr.RespondersCount ?? 0,
                    HasResponded = respondedSet.Contains(cr.Id.ToString()),
                    ClientFirstName = clientFirstName,
                    Notes = cr.Notes != null && cr.Notes.Length > 200 ? cr.Notes.Substring(0, 200) + "..." : cr.Notes
                });
            }

            return new CaregiverMatchedRequestsResponse
            {
                Items = items,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            };
        }

        // ─────────────────────────────────────────────────────────────
        //  Caregiver View — Single Request Detail (Anonymized)
        // ─────────────────────────────────────────────────────────────

        public async Task<CaregiverRequestDetailDTO> GetCaregiverViewAsync(string careRequestId, string caregiverId)
        {
            if (!ObjectId.TryParse(careRequestId, out var requestOid))
                throw new ArgumentException("Invalid care request ID format.");

            var careRequest = await _dbContext.CareRequests.FindAsync(requestOid);
            if (careRequest == null)
                throw new KeyNotFoundException("Care request not found.");

            var hasResponded = await _dbContext.CareRequestResponses
                .AnyAsync(r => r.CareRequestId == careRequestId && r.CaregiverId == caregiverId);

            return new CaregiverRequestDetailDTO
            {
                Id = careRequest.Id.ToString(),
                Title = careRequest.Title,
                ServiceGroup = careRequest.ServiceGroup ?? string.Empty,
                ServiceCategory = careRequest.ServiceCategory,
                ServicePackageType = careRequest.ServicePackageType,
                ServiceMode = careRequest.ServiceMode,
                Tasks = careRequest.Tasks ?? new List<string>(),
                Notes = careRequest.Notes,
                ExperiencePreference = careRequest.ExperiencePreference,
                CertificationPreference = careRequest.CertificationPreference,
                LanguagePreference = careRequest.LanguagePreference,
                Urgency = careRequest.Urgency,
                Schedule = careRequest.Schedule,
                Frequency = careRequest.Frequency,
                Duration = careRequest.Duration,
                Location = careRequest.Location,
                Budget = careRequest.Budget,
                BudgetMin = careRequest.BudgetMin,
                BudgetMax = careRequest.BudgetMax,
                BudgetType = careRequest.BudgetType,
                PostedAt = careRequest.CreatedAt,
                RespondersCount = careRequest.RespondersCount ?? 0,
                HasResponded = hasResponded,
                Status = careRequest.Status
            };
        }

        // ─────────────────────────────────────────────────────────────
        //  Private Helpers
        // ─────────────────────────────────────────────────────────────

        private async Task<(CareRequest careRequest, CareRequestResponseEntity response)> ValidateClientResponseAccessAsync(
            string careRequestId, string responseId, string clientId)
        {
            if (!ObjectId.TryParse(careRequestId, out var requestOid))
                throw new ArgumentException("Invalid care request ID format.");
            if (!ObjectId.TryParse(responseId, out var responseOid))
                throw new ArgumentException("Invalid response ID format.");

            var careRequest = await _dbContext.CareRequests.FindAsync(requestOid);
            if (careRequest == null)
                throw new KeyNotFoundException("Care request not found.");

            if (careRequest.ClientId != clientId)
                throw new UnauthorizedAccessException("You are not authorized to manage this request.");

            var response = await _dbContext.CareRequestResponses.FindAsync(responseOid);
            if (response == null || response.CareRequestId != careRequestId)
                throw new KeyNotFoundException("Response not found for this request.");

            return (careRequest, response);
        }

        private async Task<CaregiverResponseCardDTO?> BuildResponseCardAsync(CareRequestResponseEntity resp)
        {
            if (!ObjectId.TryParse(resp.CaregiverId, out var caregiverOid))
                return null;

            var caregiver = await _dbContext.CareGivers.FindAsync(caregiverOid);
            if (caregiver == null) return null;

            var reviews = await _dbContext.Reviews
                .Where(r => r.CaregiverId == resp.CaregiverId)
                .ToListAsync();
            var avgRating = reviews.Count > 0 ? Math.Round(reviews.Average(r => r.Rating), 1) : 0;

            // Check if caregiver has verified certifications
            var hasVerifiedCert = await _dbContext.Certifications
                .AnyAsync(c => c.CaregiverId == resp.CaregiverId && c.IsVerified);

            return new CaregiverResponseCardDTO
            {
                ResponseId = resp.Id.ToString(),
                CaregiverId = resp.CaregiverId,
                CaregiverName = $"{caregiver.FirstName} {caregiver.LastName}",
                ProfileImage = caregiver.ProfileImage,
                Location = caregiver.ServiceAddress ?? caregiver.ServiceCity,
                AverageRating = avgRating,
                ReviewCount = reviews.Count,
                MatchScore = resp.MatchScore,
                Status = resp.Status,
                RespondedAt = resp.RespondedAt,
                Message = resp.Message,
                ProposedRate = resp.ProposedRate,
                IsVerified = hasVerifiedCert,
                AboutMe = caregiver.AboutMe
            };
        }

        private static CareRequestDTO MapToDTO(CareRequest cr)
        {
            return new CareRequestDTO
            {
                Id = cr.Id.ToString(),
                ClientId = cr.ClientId,
                ServiceCategory = cr.ServiceCategory,
                Title = cr.Title,
                ServiceGroup = cr.ServiceGroup ?? string.Empty,
                Notes = cr.Notes,
                Urgency = cr.Urgency,
                Schedule = cr.Schedule,
                Frequency = cr.Frequency,
                Duration = cr.Duration,
                Location = cr.Location,
                Budget = cr.Budget,
                BudgetMin = cr.BudgetMin,
                BudgetMax = cr.BudgetMax,
                BudgetType = cr.BudgetType,
                SpecialRequirements = cr.SpecialRequirements,
                Tasks = cr.Tasks ?? new List<string>(),
                ExperiencePreference = cr.ExperiencePreference,
                CertificationPreference = cr.CertificationPreference,
                LanguagePreference = cr.LanguagePreference,
                ServicePackageType = cr.ServicePackageType,
                ServiceMode = cr.ServiceMode,
                Status = cr.Status,
                CreatedAt = cr.CreatedAt,
                UpdatedAt = cr.UpdatedAt,
                MatchedAt = cr.MatchedAt,
                MatchCount = cr.MatchCount ?? 0,
                RespondersCount = cr.RespondersCount ?? 0
            };
        }
    }
}
