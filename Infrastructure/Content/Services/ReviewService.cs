using Application.Commands;
using Application.DTOs;
using Application.Interfaces;
using Application.Interfaces.Content;
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

namespace Infrastructure.Content.Services
{
    public class ReviewService : IReviewService
    {
        private readonly CareProDbContext careProDbContext;
        private readonly ICareGiverService careGiverService;
        private readonly IClientService clientService;
        private readonly IMediator _mediator;
        private readonly ILogger<ReviewService> logger;

        public ReviewService(CareProDbContext careProDbContext, ICareGiverService careGiverService, IClientService clientService, IMediator mediator, ILogger<ReviewService> logger)
        {
            this.careProDbContext = careProDbContext;
            this.careGiverService = careGiverService;
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
                    AssignmentId = addReviewRequest.AssignmentId,
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

        public async Task<IEnumerable<ReviewResponse>> GetReviewsByAssignmentAsync(string assignmentId)
        {
            try
            {
                var reviews = await careProDbContext.Reviews
                    .Where(r => r.AssignmentId == assignmentId)
                    .OrderByDescending(r => r.ReviewedOn)
                    .ToListAsync();

                var reviewsDTO = new List<ReviewResponse>();

                foreach (var review in reviews)
                {
                    var client = await clientService.GetClientUserAsync(review.ClientId);
                    if (client == null)
                    {
                        throw new KeyNotFoundException($"Client with ID:{review.ClientId} Not found");
                    }

                    CaregiverResponse? caregiver;
                    try { caregiver = await careGiverService.GetCaregiverUserAsync(review.CaregiverId); }
                    catch (KeyNotFoundException) { caregiver = null; }

                    reviewsDTO.Add(new ReviewResponse
                    {
                        ReviewId = review.ReviewId.ToString(),
                        ClientId = review.ClientId,
                        ClientName = client.FirstName + " " + client.LastName,
                        CaregiverId = review.CaregiverId,
                        CaregiverName = caregiver != null ? $"{caregiver.FirstName} {caregiver.LastName}" : string.Empty,
                        AssignmentId = review.AssignmentId,
                        Message = review.Message,
                        Rating = review.Rating,
                        ReviewedOn = review.ReviewedOn,
                    });
                }

                return reviewsDTO;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error retrieving reviews for Assignment {AssignmentId}", assignmentId);
                throw;
            }
        }

        public async Task<ReviewResponse> GetReviewAsync(string reviewId)
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

            var caregiver = await careGiverService.GetCaregiverUserAsync(review.CaregiverId);
            if (caregiver == null)
            {
                throw new KeyNotFoundException($"Caregiver with ID:{review.CaregiverId} Not found");
            }

            return new ReviewResponse
            {
                ReviewId = review.ReviewId.ToString(),
                ClientId = review.ClientId,
                ClientName = client.FirstName + " " + client.LastName,
                CaregiverId = review.CaregiverId,
                CaregiverName = $"{caregiver.FirstName} {caregiver.LastName}",
                AssignmentId = review.AssignmentId,
                Message = review.Message,
                Rating = review.Rating,
                ReviewedOn = review.ReviewedOn,
            };
        }

        public async Task<int> GetReviewCountAsync(string assignmentId)
        {
            return await careProDbContext.Reviews
                   .CountAsync(r => r.AssignmentId == assignmentId);
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
                CaregiverResponse? caregiver;
                try { caregiver = await careGiverService.GetCaregiverUserAsync(caregiverId); }
                catch (KeyNotFoundException) { caregiver = null; }

                foreach (var review in reviews)
                {
                    var client = await clientService.GetClientUserAsync(review.ClientId);
                    if (client == null) continue;

                    result.Add(new ReviewResponse
                    {
                        ReviewId = review.ReviewId.ToString(),
                        ClientId = review.ClientId,
                        ClientName = client.FirstName + " " + client.LastName,
                        CaregiverId = review.CaregiverId,
                        CaregiverName = caregiver != null ? $"{caregiver.FirstName} {caregiver.LastName}" : string.Empty,
                        AssignmentId = review.AssignmentId,
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
