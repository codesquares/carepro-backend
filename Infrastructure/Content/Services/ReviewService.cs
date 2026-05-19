using Application.Commands;
using Application.DTOs;
using Application.Interfaces;
using Application.Interfaces.Content;
using Domain.Entities;
using Infrastructure.Content.Data;
using MediatR;
using Microsoft.Build.Framework;
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
    public class ReviewService : IReviewService
    {
        private readonly CareProDbContext careProDbContext;
        private readonly IGigServices gigServices;
        private readonly IClientService clientService;
        private readonly IMediator _mediator;
        private readonly ILogger<ReviewService> logger;

        public ReviewService(CareProDbContext careProDbContext, IGigServices gigServices, IClientService clientService, IMediator mediator, ILogger<ReviewService> logger)
        {
            this.careProDbContext = careProDbContext;
            this.gigServices = gigServices;
            this.clientService = clientService;
            _mediator = mediator;
            this.logger = logger;
        }

        public async Task<string> CreateReviewAsync(AddReviewRequest addReviewRequest)
        {
            try
            {
                /// CONVERT DTO TO DOMAIN OBJECT            
                var review = new Review
                {
                    ClientId = addReviewRequest.ClientId,
                    CaregiverId = addReviewRequest.CaregiverId,
                    GigId = addReviewRequest.GigId,
                    Message = addReviewRequest.Message,
                    Rating = addReviewRequest.Rating,

                    // Assign new ID
                    ReviewId = ObjectId.GenerateNewId(),
                    ReviewedOn = DateTime.Now,
                };

                await careProDbContext.Reviews.AddAsync(review);
                await careProDbContext.SaveChangesAsync();

                // ── Notify caregiver that they received a new review ──
                try
                {
                    await _mediator.Send(new SendNotificationCommand(
                        RecipientId: addReviewRequest.CaregiverId,
                        SenderId: addReviewRequest.ClientId,
                        Type: NotificationTypes.NewReview,
                        Content: $"You received a {addReviewRequest.Rating}-star review from a client.",
                        Title: "New Review Received",
                        RelatedEntityId: review.ReviewId.ToString()));
                }
                catch (Exception notifEx)
                {
                    logger.LogError(notifEx, "Failed to send new-review notification for ReviewId {ReviewId}", review.ReviewId);
                }

                return review.ReviewId.ToString();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error creating review");
                throw;
            }

        }

        public async Task<IEnumerable<ReviewResponse>> GetAllGigReviewAsync(string gigId)
        {
            try
            {
                var reviews = await careProDbContext.Reviews
                    .Where(g => g.GigId == gigId)
                    .OrderByDescending(n => n.ReviewedOn)
                    .ToListAsync();

                var reviewsDTO = new List<ReviewResponse>();

                GigDTO? gig;
                try
                {
                    gig = await gigServices.GetGigAsync(gigId);
                }
                catch (KeyNotFoundException)
                {
                    // Gig was soft-deleted — return empty reviews rather than crashing
                    return reviewsDTO;
                }
                if (gig == null)
                {
                    return reviewsDTO;
                }


                foreach (var review in reviews)
                {
                    var client = await clientService.GetClientUserAsync(review.ClientId);
                    if (client == null)
                    {
                        throw new KeyNotFoundException($"Client with ID:{review.ClientId} Not found");
                    }


                    var reviewDTO = new ReviewResponse()
                    {
                        ReviewId = review.ReviewId.ToString(),
                        ClientId = review.ClientId,
                        ClientName = client.FirstName + " " + client.LastName,
                        CaregiverId = review.CaregiverId,
                        CaregiverName = gig.CaregiverName,
                        GigId = review.GigId,
                        Message = review.Message,
                        Rating = review.Rating,
                        ReviewedOn = review.ReviewedOn,
                    };


                    reviewsDTO.Add(reviewDTO);
                }

                return reviewsDTO;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error retrieving reviews for Gig {GigId}", gigId);
                throw;
            }
        }

        public async Task<ReviewResponse> GetGigReviewAsync(string reviewId)
        {
            var review = await careProDbContext.Reviews.FirstOrDefaultAsync(x => x.ReviewId.ToString() == reviewId);

            if (review == null)
            {
                throw new KeyNotFoundException($"Review with ID '{reviewId}' not found.");
            }

            var client = await clientService.GetClientUserAsync(review.ClientId);
            if (client == null)
            {
                throw new KeyNotFoundException($"Client with ID:{review.ClientId} Not found");
            }

            var gig = await gigServices.GetGigAsync(review.GigId);
            if (gig == null)
            {
                throw new KeyNotFoundException($"Gig with ID:{review.GigId} Not found");
            }

            var reviewDTO = new ReviewResponse()
            {
                ReviewId = review.ReviewId.ToString(),
                ClientId = review.ClientId,
                ClientName = client.FirstName + " " + client.LastName,
                CaregiverId = review.CaregiverId,
                CaregiverName = gig.CaregiverName,
                GigId = review.GigId,
                Message = review.Message,
                Rating = review.Rating,
                ReviewedOn = review.ReviewedOn,
            };

            return reviewDTO;
        }

        public async Task<int> GetReviewCountAsync(string gigId)
        {
            return await careProDbContext.Reviews
                   .CountAsync(g => g.GigId == gigId);
        }

        public async Task<IEnumerable<ReviewResponse>> GetCaregiverReviewsAsync(string caregiverId)
        {
            try
            {
                var reviews = await careProDbContext.Reviews
                    .Where(r => r.CaregiverId == caregiverId)
                    .OrderByDescending(r => r.ReviewedOn)
                    .ToListAsync();

                var result = new List<ReviewResponse>();
                var gigCache = new Dictionary<string, GigDTO?>();

                foreach (var review in reviews)
                {
                    var client = await clientService.GetClientUserAsync(review.ClientId);
                    if (client == null) continue;

                    if (!gigCache.TryGetValue(review.GigId, out var gig))
                    {
                        try { gig = await gigServices.GetGigAsync(review.GigId); }
                        catch (KeyNotFoundException) { gig = null; }
                        gigCache[review.GigId] = gig;
                    }

                    result.Add(new ReviewResponse
                    {
                        ReviewId = review.ReviewId.ToString(),
                        ClientId = review.ClientId,
                        ClientName = client.FirstName + " " + client.LastName,
                        CaregiverId = review.CaregiverId,
                        CaregiverName = gig?.CaregiverName ?? string.Empty,
                        GigId = review.GigId,
                        Message = review.Message,
                        Rating = review.Rating,
                        ReviewedOn = review.ReviewedOn,
                    });
                }

                return result;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error retrieving reviews for caregiver {CaregiverId}", caregiverId);
                throw;
            }
        }
    }
}
