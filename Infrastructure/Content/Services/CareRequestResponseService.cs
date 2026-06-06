using Application.Commands;
using Application.DTOs;
using Application.Interfaces.Content;
using Application.Interfaces.Email;
using Domain.Entities;
using Infrastructure.Content.Data;
using MediatR;
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
        private readonly IMediator _mediator;
        private readonly IEmailService _emailService;
        private readonly IGigPriceNegotiationService _negotiationService;
        private readonly ILogger<CareRequestResponseService> _logger;

        private static readonly string[] BrowsableStatuses = new[] { "pending", "matched", "unmatched", "active", "escalated" };

        public CareRequestResponseService(
            CareProDbContext dbContext,
            IMediator mediator,
            IEmailService emailService,
            IGigPriceNegotiationService negotiationService,
            ILogger<CareRequestResponseService> logger)
        {
            _dbContext = dbContext;
            _mediator = mediator;
            _emailService = emailService;
            _negotiationService = negotiationService;
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
            if (statusLower != "pending" && statusLower != "matched" && statusLower != "unmatched" && statusLower != "active" && statusLower != "escalated")
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

            await _mediator.Send(new SendNotificationCommand(
                careRequest.ClientId,
                caregiverId,
                NotificationTypes.CareRequestNewResponder,
                $"{caregiverName} is interested in your \"{careRequest.Title}\" request. View their profile.",
                $"{caregiverName} responded to your request",
                careRequestId));

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
            await _mediator.Send(new SendNotificationCommand(
                response.CaregiverId,
                clientId,
                NotificationTypes.CareRequestShortlisted,
                $"A client shortlisted you for their \"{careRequest.Title}\" request.",
                "You've been shortlisted!",
                careRequestId));

            return new ShortlistResult { Success = true, ResponseId = responseId, Status = "shortlisted" };
        }

        public async Task<ShortlistResult> RemoveShortlistAsync(string careRequestId, string responseId, string clientId)
        {
            var (careRequest, response) = await ValidateClientResponseAccessAsync(careRequestId, responseId, clientId);

            if (response.Status != "shortlisted")
                throw new InvalidOperationException("This responder is not currently shortlisted.");

            response.Status = "pending";
            response.ShortlistedAt = null;
            _dbContext.CareRequestResponses.Update(response);
            await _dbContext.SaveChangesAsync();

            // ── Notify caregiver that they were removed from the shortlist ──
            try
            {
                if (!string.IsNullOrEmpty(response.CaregiverId))
                {
                    var requestTitle = careRequest?.Title ?? "a care request";
                    await _mediator.Send(new SendNotificationCommand(
                        RecipientId: response.CaregiverId,
                        SenderId: clientId,
                        Type: NotificationTypes.ShortlistRemoved,
                        Content: $"You have been removed from the shortlist for \"{requestTitle}\". You may still be considered.",
                        Title: "Removed from Shortlist",
                        RelatedEntityId: responseId));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send shortlist-removed notification for ResponseId {ResponseId}", responseId);
            }

            return new ShortlistResult { Success = true, ResponseId = responseId, Status = "pending" };
        }

        // ─────────────────────────────────────────────────────────────
        //  Hire Responder — Initiate Price Negotiation
        //
        //  CHANGED: No longer creates a special gig immediately.
        //  Instead, a GigPriceNegotiation record is created using the
        //  caregiver's ProposedRate as the opening price. The special gig
        //  is only created when both parties agree on a final per-visit price.
        //  HireResult now returns NegotiationId instead of SpecialGigId.
        // ─────────────────────────────────────────────────────────────

        public async Task<HireResult> HireResponderAsync(string careRequestId, string responseId, string clientId)
        {
            var (careRequest, response) = await ValidateClientResponseAccessAsync(careRequestId, responseId, clientId);

            if (response.Status == "hired")
                throw new InvalidOperationException("This responder has already been hired.");

            // Check that only one caregiver can be hired per request
            var existingHire = await _dbContext.CareRequestResponses
                .AnyAsync(r => r.CareRequestId == careRequestId && r.Status == "hired");
            if (existingHire)
                throw new InvalidOperationException("You have already hired a caregiver for this request. Only one hire is allowed per request.");

            // Determine the opening price for the negotiation.
            // Priority: caregiver's proposed rate → client's budget max → budget min → 10,000 (minimum floor)
            var openingRate = response.ProposedRate.HasValue ? response.ProposedRate.Value
                            : careRequest.BudgetMax.HasValue ? careRequest.BudgetMax.Value
                            : careRequest.BudgetMin.HasValue ? careRequest.BudgetMin.Value
                            : 10_000m;

            // Mark response as hired
            response.Status = "hired";
            response.HiredAt = DateTime.UtcNow;
            // SpecialGigId stays null — it will be populated when negotiation reaches Agreed status
            _dbContext.CareRequestResponses.Update(response);
            await _dbContext.SaveChangesAsync();

            // Initiate the price negotiation (idempotent — safe to call again if needed)
            var negotiationDTO = await _negotiationService.InitiateFromCareRequestHireAsync(
                clientId: clientId,
                caregiverId: response.CaregiverId,
                careRequestId: careRequestId,
                responseId: responseId,
                caregiverProposedRate: openingRate,
                gigTitleSnapshot: careRequest.Title,
                gigCategorySnapshot: careRequest.ServiceCategory,
                gigPackageDetailsSnapshot: careRequest.Tasks ?? new List<string>());

            // Notify caregiver: you've been selected — proceed to price negotiation
            var client = await _dbContext.Clients.FindAsync(ObjectId.Parse(clientId));
            var clientName = client?.FirstName ?? "A client";

            await _mediator.Send(new SendNotificationCommand(
                RecipientId: response.CaregiverId,
                SenderId: clientId,
                Type: NotificationTypes.CareRequestHired,
                Content: $"{clientName} selected you for their \"{careRequest.Title}\" request. Review the pricing and confirm to get started.",
                Title: "You've been selected!",
                RelatedEntityId: negotiationDTO.NegotiationId));

            // Send hire email to caregiver
            try
            {
                var caregiver = await _dbContext.CareGivers.FindAsync(ObjectId.Parse(response.CaregiverId));
                if (caregiver != null)
                {
                    var subject = $"You've been selected for \"{careRequest.Title}\"!";
                    var html = $@"
                        <h3>Congratulations {caregiver.FirstName}!</h3>
                        <p>{clientName} has selected you for their care request: <strong>{careRequest.Title}</strong>.</p>
                        <div style='background-color:#f8f9fa;padding:15px;border-radius:5px;margin:20px 0;'>
                            <p><strong>Category:</strong> {careRequest.ServiceCategory}</p>
                            <p><strong>Location:</strong> {careRequest.Location ?? "Not specified"}</p>
                            <p><strong>Opening rate (per visit):</strong> ₦{openingRate:N0}</p>
                        </div>
                        <p>Log in to review the pricing and confirm or negotiate before the booking is finalised.</p>
                        <p style='color:#666;font-size:13px;'>Note: the price shown is a per-visit rate. The client will choose the service type and visit frequency at checkout.</p>
                        <p>— The CarePro Team</p>";
                    await _emailService.SendGenericNotificationEmailAsync(caregiver.Email, caregiver.FirstName, subject, html);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send hire email for response {ResponseId}", responseId);
            }

            _logger.LogInformation(
                "Client {ClientId} hired caregiver {CaregiverId} for CareRequest {CareRequestId}. Negotiation {NegotiationId} initiated.",
                clientId, response.CaregiverId, careRequestId, negotiationDTO.NegotiationId);

            return new HireResult
            {
                Success = true,
                ResponseId = responseId,
                NegotiationId = negotiationDTO.NegotiationId,
                CaregiverProposedRate = openingRate,
                SpecialGigId = null, // Null at hire time — set only after negotiation agreement
                CaregiverId = response.CaregiverId,
                Message = "Caregiver selected. Proceed to the price negotiation component to agree on a per-visit rate before payment."
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
            // Include "unmatched" — the recommendation engine may find zero matches, but the request
            // should still be visible on the caregiver browse page so any caregiver can respond.
            var query = _dbContext.CareRequests
                .Where(cr => cr.Status.ToLower() == "pending"
                             || cr.Status.ToLower() == "matched"
                             || cr.Status.ToLower() == "unmatched"
                             || cr.Status.ToLower() == "active"
                             || cr.Status.ToLower() == "escalated");

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
                AboutMe = caregiver.AboutMe,
                SpecialGigId = resp.SpecialGigId
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

        // ─────────────────────────────────────────────────────────────
        //  Caregiver: My Submitted Responses
        // ─────────────────────────────────────────────────────────────

        public async Task<List<CaregiverMyResponseDTO>> GetCaregiverMyResponsesAsync(string caregiverId)
        {
            var responses = await _dbContext.CareRequestResponses
                .Where(r => r.CaregiverId == caregiverId)
                .OrderByDescending(r => r.RespondedAt)
                .ToListAsync();

            var result = new List<CaregiverMyResponseDTO>();

            foreach (var response in responses)
            {
                var careRequest = await _dbContext.CareRequests
                    .FirstOrDefaultAsync(cr => cr.Id.ToString() == response.CareRequestId);

                result.Add(new CaregiverMyResponseDTO
                {
                    ResponseId = response.Id.ToString(),
                    CareRequestId = response.CareRequestId,
                    CareRequestTitle = careRequest?.Title ?? "(Care Request)",
                    CareRequestStatus = careRequest?.Status ?? "unknown",
                    ResponseStatus = response.Status,
                    Message = response.Message,
                    ProposedRate = response.ProposedRate,
                    RespondedAt = response.RespondedAt,
                    ShortlistedAt = response.ShortlistedAt
                });
            }

            return result;
        }
    }
}
