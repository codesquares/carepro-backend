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
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Content.Services
{
    public class ClientRecommendationService : IClientRecommendationService
    {
        private readonly CareProDbContext careProDbContext;
        private readonly IClientService clientService;
        private readonly ILogger<ClientRecommendationService> logger;

        public ClientRecommendationService(CareProDbContext careProDbContext, IClientService clientService, ILogger<ClientRecommendationService> logger)
        {
            this.careProDbContext = careProDbContext;
            this.clientService = clientService;
            this.logger = logger;
        }

        public async Task<ClientRecommendationDTO> GetClientRecommendationAsync(string clientId)
        {
            try
            {
                logger.LogInformation($"Retrieving active recommendation for client: {clientId}");

                // Validate client exists
                var client = await clientService.GetClientUserAsync(clientId);
                if (client == null)
                {
                    throw new KeyNotFoundException($"Client with ID '{clientId}' not found");
                }

                // Find the active recommendation for this client
                var recommendation = await careProDbContext.ClientRecommendations
                    .Where(r => r.ClientId == clientId && r.IsActive && !r.IsArchived)
                    .OrderByDescending(r => r.CreatedAt)
                    .FirstOrDefaultAsync();

                if (recommendation == null)
                {
                    logger.LogInformation($"No active recommendations found for client: {clientId}");
                    return new ClientRecommendationDTO
                    {
                        ClientId = clientId,
                        Recommendations = new List<RecommendationItemDTO>(),
                        IsActive = false
                    };
                }

                // Map to DTO
                return MapToDTO(recommendation);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error retrieving recommendations for client: {clientId}");
                throw;
            }
        }

        public async Task<ClientRecommendationResponse> CreateClientRecommendationAsync(string clientId, CreateClientRecommendationRequest request)
        {
            try
            {
                logger.LogInformation($"Creating new recommendation for client: {clientId}");

                // Validate client exists
                var client = await clientService.GetClientUserAsync(clientId);
                if (client == null)
                {
                    throw new KeyNotFoundException($"Client with ID '{clientId}' not found");
                }

                // Validate clientId matches request
                if (request.ClientId != clientId)
                {
                    throw new ArgumentException("ClientId in URL does not match ClientId in request body");
                }

                // Archive any existing active recommendations for this client
                await ArchiveExistingRecommendationsAsync(clientId);

                // Create new recommendation entity
                var recommendation = new ClientRecommendation
                {
                    Id = ObjectId.GenerateNewId(),
                    ClientId = clientId,
                    Recommendations = request.Recommendations.Select(r => new RecommendationItem
                    {
                        ProviderId = r.ProviderId,
                        CaregiverId = r.CaregiverId,
                        MatchScore = r.MatchScore,
                        ServiceType = r.ServiceType,
                        Location = r.Location,
                        Price = r.Price,
                        PriceUnit = r.PriceUnit,
                        Rating = r.Rating,
                        ReviewCount = r.ReviewCount
                    }).ToList(),
                    GeneratedAt = request.GeneratedAt,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true,
                    IsArchived = false,
                    ActedUpon = false
                };

                // Save to database
                await careProDbContext.ClientRecommendations.AddAsync(recommendation);
                await careProDbContext.SaveChangesAsync();

                logger.LogInformation($"Successfully created recommendation {recommendation.Id} for client: {clientId}");

                return new ClientRecommendationResponse
                {
                    Success = true,
                    Message = "Recommendations saved successfully",
                    RecommendationId = recommendation.Id.ToString(),
                    ClientId = clientId,
                    TotalRecommendations = recommendation.Recommendations.Count,
                    SavedAt = recommendation.CreatedAt
                };
            }
            catch (KeyNotFoundException)
            {
                throw;
            }
            catch (ArgumentException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error creating recommendations for client: {clientId}");
                throw new ApplicationException("Failed to save recommendations due to server error", ex);
            }
        }

        public async Task<ClientRecommendationResponse> UpdateClientRecommendationAsync(string clientId, UpdateClientRecommendationRequest request)
        {
            try
            {
                logger.LogInformation($"Updating recommendation for client: {clientId}");

                // Validate client exists
                var client = await clientService.GetClientUserAsync(clientId);
                if (client == null)
                {
                    throw new KeyNotFoundException($"Client with ID '{clientId}' not found");
                }

                // Validate clientId matches request
                if (request.ClientId != clientId)
                {
                    throw new ArgumentException("ClientId in URL does not match ClientId in request body");
                }

                // Find existing active recommendation
                var existingRecommendation = await careProDbContext.ClientRecommendations
                    .Where(r => r.ClientId == clientId && r.IsActive && !r.IsArchived)
                    .OrderByDescending(r => r.CreatedAt)
                    .FirstOrDefaultAsync();

                if (existingRecommendation == null)
                {
                    throw new KeyNotFoundException($"No active recommendation found for client '{clientId}'");
                }

                // Update the recommendation
                existingRecommendation.Recommendations = request.Recommendations.Select(item => new RecommendationItem
                {
                    ProviderId = item.ProviderId,
                    CaregiverId = item.CaregiverId,
                    MatchScore = item.MatchScore,
                    ServiceType = item.ServiceType,
                    Location = item.Location,
                    Price = item.Price,
                    PriceUnit = item.PriceUnit,
                    Rating = item.Rating,
                    ReviewCount = item.ReviewCount
                }).ToList();
                
                existingRecommendation.GeneratedAt = request.GeneratedAt;
                existingRecommendation.UpdatedAt = DateTime.UtcNow;

                careProDbContext.ClientRecommendations.Update(existingRecommendation);
                await careProDbContext.SaveChangesAsync();

                logger.LogInformation($"Successfully updated recommendation {existingRecommendation.Id} for client: {clientId}");

                return new ClientRecommendationResponse
                {
                    Success = true,
                    Message = "Recommendations updated successfully",
                    RecommendationId = existingRecommendation.Id.ToString(),
                    ClientId = clientId,
                    TotalRecommendations = request.Recommendations.Count,
                    UpdatedAt = DateTime.UtcNow
                };
            }
            catch (KeyNotFoundException)
            {
                throw;
            }
            catch (ArgumentException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error updating recommendations for client: {clientId}");
                throw new ApplicationException("Failed to update recommendations due to server error", ex);
            }
        }

        public async Task<bool> DeleteClientRecommendationAsync(string recommendationId)
        {
            try
            {
                logger.LogInformation($"Deleting recommendation: {recommendationId}");

                if (!ObjectId.TryParse(recommendationId, out var objectId))
                {
                    throw new ArgumentException("Invalid recommendation ID format");
                }

                // Find the recommendation
                var recommendation = await careProDbContext.ClientRecommendations
                    .FirstOrDefaultAsync(r => r.Id == objectId);

                if (recommendation == null)
                {
                    throw new KeyNotFoundException($"Recommendation with ID '{recommendationId}' not found");
                }

                // Soft delete - archive the recommendation
                recommendation.IsActive = false;
                recommendation.IsArchived = true;
                recommendation.ArchivedAt = DateTime.UtcNow;

                careProDbContext.ClientRecommendations.Update(recommendation);
                await careProDbContext.SaveChangesAsync();

                logger.LogInformation($"Successfully deleted recommendation: {recommendationId}");
                return true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error deleting recommendation: {recommendationId}");
                throw;
            }
        }

        // Helper method to archive existing recommendations
        private async Task ArchiveExistingRecommendationsAsync(string clientId)
        {
            try
            {
                var existingRecommendations = await careProDbContext.ClientRecommendations
                    .Where(r => r.ClientId == clientId && r.IsActive && !r.IsArchived)
                    .ToListAsync();

                if (existingRecommendations.Any())
                {
                    foreach (var rec in existingRecommendations)
                    {
                        rec.IsActive = false;
                        rec.IsArchived = true;
                        rec.ArchivedAt = DateTime.UtcNow;
                    }

                    careProDbContext.ClientRecommendations.UpdateRange(existingRecommendations);
                    await careProDbContext.SaveChangesAsync();

                    logger.LogInformation($"Archived {existingRecommendations.Count} existing recommendations for client: {clientId}");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error archiving existing recommendations for client: {clientId}");
                throw;
            }
        }

        // Helper method to map entity to DTO
        private ClientRecommendationDTO MapToDTO(ClientRecommendation recommendation)
        {
            return new ClientRecommendationDTO
            {
                RecommendationId = recommendation.Id.ToString(),
                ClientId = recommendation.ClientId,
                Recommendations = recommendation.Recommendations?.Select(r => new RecommendationItemDTO
                {
                    ProviderId = r.ProviderId,
                    CaregiverId = r.CaregiverId,
                    MatchScore = r.MatchScore,
                    ServiceType = r.ServiceType,
                    Location = r.Location,
                    Price = r.Price,
                    PriceUnit = r.PriceUnit,
                    Rating = r.Rating,
                    ReviewCount = r.ReviewCount
                }).ToList(),
                GeneratedAt = recommendation.GeneratedAt,
                CreatedAt = recommendation.CreatedAt,
                UpdatedAt = recommendation.UpdatedAt,
                IsActive = recommendation.IsActive,
                ViewedAt = recommendation.ViewedAt,
                ActedUpon = recommendation.ActedUpon,
                SelectedProviderId = recommendation.SelectedProviderId
            };
        }
    }
}
