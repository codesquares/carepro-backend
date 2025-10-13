using Application.DTOs;
using Application.Interfaces;
using Application.Interfaces.Content;
using Domain.Entities;
using Infrastructure.Content.Data;
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
        private readonly ILogger<ReviewService> logger;

        public ReviewService(CareProDbContext careProDbContext, IGigServices gigServices, IClientService clientService, ILogger<ReviewService> logger)
        {
            this.careProDbContext = careProDbContext;
            this.gigServices = gigServices;
            this.clientService = clientService;
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

                var gig = await gigServices.GetGigAsync(gigId);
                if (gig == null)
                {
                    throw new KeyNotFoundException($"Gig with ID:{gigId} Not found");
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
                   .CountAsync(g => g.GigId == gigId );
                        
        }
    }
}
