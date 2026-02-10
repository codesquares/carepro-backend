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
    public class CareRequestService : ICareRequestService
    {
        private readonly CareProDbContext _dbContext;
        private readonly IClientService _clientService;
        private readonly ILogger<CareRequestService> _logger;

        // Valid service categories
        private static readonly HashSet<string> ValidServiceCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Adult Care", "Child Care", "Pet Care", "Home Care", "Post Surgery Care",
            "Special Needs Care", "Medical Support", "Mobility Support", "Therapy & Wellness", "Palliative"
        };

        // Valid urgency values
        private static readonly HashSet<string> ValidUrgencyValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "within-24h", "within-week", "within-month", "no-rush"
        };

        // Valid frequency values
        private static readonly HashSet<string> ValidFrequencyValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "One-time", "Daily", "A few times a week", "Weekly", "As needed"
        };

        // Valid status values
        private static readonly HashSet<string> ValidStatusValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "pending", "matched", "accepted", "completed", "cancelled"
        };

        public CareRequestService(CareProDbContext dbContext, IClientService clientService, ILogger<CareRequestService> logger)
        {
            _dbContext = dbContext;
            _clientService = clientService;
            _logger = logger;
        }

        public async Task<CareRequestDTO> CreateCareRequestAsync(CreateCareRequestDTO createCareRequestDTO)
        {
            _logger.LogInformation($"CreateCareRequestAsync called for ClientId: {createCareRequestDTO.ClientId}");

            // Validate client exists
            var client = await _clientService.GetClientUserAsync(createCareRequestDTO.ClientId);
            if (client == null)
            {
                _logger.LogError($"Client with ID '{createCareRequestDTO.ClientId}' not found");
                throw new KeyNotFoundException($"Client with ID '{createCareRequestDTO.ClientId}' not found.");
            }

            // Validate service category
            if (!ValidServiceCategories.Contains(createCareRequestDTO.ServiceCategory))
            {
                throw new ArgumentException($"Invalid service category. Valid values are: {string.Join(", ", ValidServiceCategories)}");
            }

            // Validate urgency
            if (!ValidUrgencyValues.Contains(createCareRequestDTO.Urgency))
            {
                throw new ArgumentException($"Invalid urgency value. Valid values are: {string.Join(", ", ValidUrgencyValues)}");
            }

            // Validate frequency
            if (!ValidFrequencyValues.Contains(createCareRequestDTO.Frequency))
            {
                throw new ArgumentException($"Invalid frequency value. Valid values are: {string.Join(", ", ValidFrequencyValues)}");
            }

            // Create entity
            var careRequest = new CareRequest
            {
                Id = ObjectId.GenerateNewId(),
                ClientId = createCareRequestDTO.ClientId,
                ServiceCategory = createCareRequestDTO.ServiceCategory,
                Title = createCareRequestDTO.Title,
                Description = createCareRequestDTO.Description,
                Urgency = createCareRequestDTO.Urgency,
                Schedule = createCareRequestDTO.Schedule ?? new List<string>(),
                Frequency = createCareRequestDTO.Frequency,
                Duration = createCareRequestDTO.Duration,
                Location = createCareRequestDTO.Location,
                Budget = createCareRequestDTO.Budget,
                SpecialRequirements = createCareRequestDTO.SpecialRequirements,
                Status = "pending",
                CreatedAt = DateTime.UtcNow
            };

            await _dbContext.CareRequests.AddAsync(careRequest);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation($"CareRequest created with ID: {careRequest.Id}");

            return MapToDTO(careRequest);
        }

        public async Task<List<CareRequestDTO>> GetCareRequestsByClientIdAsync(string clientId)
        {
            _logger.LogInformation($"GetCareRequestsByClientIdAsync called for ClientId: {clientId}");

            var careRequests = await _dbContext.CareRequests
                .Where(cr => cr.ClientId == clientId)
                .OrderByDescending(cr => cr.CreatedAt)
                .ToListAsync();

            _logger.LogInformation($"Found {careRequests.Count} care requests for ClientId: {clientId}");

            return careRequests.Select(MapToDTO).ToList();
        }

        public async Task<CareRequestDTO> GetCareRequestByIdAsync(string requestId)
        {
            _logger.LogInformation($"GetCareRequestByIdAsync called for RequestId: {requestId}");

            if (!ObjectId.TryParse(requestId, out var objectId))
            {
                throw new ArgumentException("Invalid care request ID format.");
            }

            var careRequest = await _dbContext.CareRequests.FindAsync(objectId);

            if (careRequest == null)
            {
                _logger.LogWarning($"CareRequest with ID '{requestId}' not found");
                throw new KeyNotFoundException($"Care request with ID '{requestId}' not found.");
            }

            return MapToDTO(careRequest);
        }

        public async Task<CareRequestDTO> UpdateCareRequestAsync(string requestId, UpdateCareRequestDTO updateCareRequestDTO)
        {
            _logger.LogInformation($"UpdateCareRequestAsync called for RequestId: {requestId}");

            if (!ObjectId.TryParse(requestId, out var objectId))
            {
                throw new ArgumentException("Invalid care request ID format.");
            }

            var careRequest = await _dbContext.CareRequests.FindAsync(objectId);

            if (careRequest == null)
            {
                throw new KeyNotFoundException($"Care request with ID '{requestId}' not found.");
            }

            // Only allow updates if status is pending
            if (careRequest.Status != "pending")
            {
                throw new InvalidOperationException($"Cannot update care request with status '{careRequest.Status}'. Only pending requests can be updated.");
            }

            // Update fields if provided
            if (!string.IsNullOrEmpty(updateCareRequestDTO.ServiceCategory))
            {
                if (!ValidServiceCategories.Contains(updateCareRequestDTO.ServiceCategory))
                {
                    throw new ArgumentException($"Invalid service category. Valid values are: {string.Join(", ", ValidServiceCategories)}");
                }
                careRequest.ServiceCategory = updateCareRequestDTO.ServiceCategory;
            }

            if (!string.IsNullOrEmpty(updateCareRequestDTO.Title))
                careRequest.Title = updateCareRequestDTO.Title;

            if (!string.IsNullOrEmpty(updateCareRequestDTO.Description))
                careRequest.Description = updateCareRequestDTO.Description;

            if (!string.IsNullOrEmpty(updateCareRequestDTO.Urgency))
            {
                if (!ValidUrgencyValues.Contains(updateCareRequestDTO.Urgency))
                {
                    throw new ArgumentException($"Invalid urgency value. Valid values are: {string.Join(", ", ValidUrgencyValues)}");
                }
                careRequest.Urgency = updateCareRequestDTO.Urgency;
            }

            if (updateCareRequestDTO.Schedule != null)
                careRequest.Schedule = updateCareRequestDTO.Schedule;

            if (!string.IsNullOrEmpty(updateCareRequestDTO.Frequency))
            {
                if (!ValidFrequencyValues.Contains(updateCareRequestDTO.Frequency))
                {
                    throw new ArgumentException($"Invalid frequency value. Valid values are: {string.Join(", ", ValidFrequencyValues)}");
                }
                careRequest.Frequency = updateCareRequestDTO.Frequency;
            }

            if (updateCareRequestDTO.Duration != null)
                careRequest.Duration = updateCareRequestDTO.Duration;

            if (updateCareRequestDTO.Location != null)
                careRequest.Location = updateCareRequestDTO.Location;

            if (updateCareRequestDTO.Budget != null)
                careRequest.Budget = updateCareRequestDTO.Budget;

            if (updateCareRequestDTO.SpecialRequirements != null)
                careRequest.SpecialRequirements = updateCareRequestDTO.SpecialRequirements;

            careRequest.UpdatedAt = DateTime.UtcNow;

            _dbContext.CareRequests.Update(careRequest);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation($"CareRequest with ID '{requestId}' updated successfully");

            return MapToDTO(careRequest);
        }

        public async Task<CareRequestDTO> CancelCareRequestAsync(string requestId)
        {
            _logger.LogInformation($"CancelCareRequestAsync called for RequestId: {requestId}");

            if (!ObjectId.TryParse(requestId, out var objectId))
            {
                throw new ArgumentException("Invalid care request ID format.");
            }

            var careRequest = await _dbContext.CareRequests.FindAsync(objectId);

            if (careRequest == null)
            {
                throw new KeyNotFoundException($"Care request with ID '{requestId}' not found.");
            }

            // Don't allow cancelling already completed or cancelled requests
            if (careRequest.Status == "completed" || careRequest.Status == "cancelled")
            {
                throw new InvalidOperationException($"Cannot cancel care request with status '{careRequest.Status}'.");
            }

            careRequest.Status = "cancelled";
            careRequest.UpdatedAt = DateTime.UtcNow;

            _dbContext.CareRequests.Update(careRequest);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation($"CareRequest with ID '{requestId}' cancelled successfully");

            return MapToDTO(careRequest);
        }

        public async Task<List<CareRequestDTO>> GetPendingCareRequestsAsync()
        {
            _logger.LogInformation("GetPendingCareRequestsAsync called");

            var pendingRequests = await _dbContext.CareRequests
                .Where(cr => cr.Status == "pending")
                .OrderByDescending(cr => cr.CreatedAt)
                .ToListAsync();

            _logger.LogInformation($"Found {pendingRequests.Count} pending care requests");

            return pendingRequests.Select(MapToDTO).ToList();
        }

        public async Task<CareRequestDTO> UpdateCareRequestStatusAsync(string requestId, string status)
        {
            _logger.LogInformation($"UpdateCareRequestStatusAsync called for RequestId: {requestId}, Status: {status}");

            if (!ObjectId.TryParse(requestId, out var objectId))
            {
                throw new ArgumentException("Invalid care request ID format.");
            }

            if (!ValidStatusValues.Contains(status))
            {
                throw new ArgumentException($"Invalid status value. Valid values are: {string.Join(", ", ValidStatusValues)}");
            }

            var careRequest = await _dbContext.CareRequests.FindAsync(objectId);

            if (careRequest == null)
            {
                throw new KeyNotFoundException($"Care request with ID '{requestId}' not found.");
            }

            careRequest.Status = status.ToLower();
            careRequest.UpdatedAt = DateTime.UtcNow;

            _dbContext.CareRequests.Update(careRequest);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation($"CareRequest with ID '{requestId}' status updated to '{status}'");

            return MapToDTO(careRequest);
        }

        private static CareRequestDTO MapToDTO(CareRequest careRequest)
        {
            return new CareRequestDTO
            {
                Id = careRequest.Id.ToString(),
                ClientId = careRequest.ClientId,
                ServiceCategory = careRequest.ServiceCategory,
                Title = careRequest.Title,
                Description = careRequest.Description,
                Urgency = careRequest.Urgency,
                Schedule = careRequest.Schedule,
                Frequency = careRequest.Frequency,
                Duration = careRequest.Duration,
                Location = careRequest.Location,
                Budget = careRequest.Budget,
                SpecialRequirements = careRequest.SpecialRequirements,
                Status = careRequest.Status,
                CreatedAt = careRequest.CreatedAt,
                UpdatedAt = careRequest.UpdatedAt
            };
        }
    }
}
