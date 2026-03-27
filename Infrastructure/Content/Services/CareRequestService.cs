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
        private readonly INotificationService _notificationService;
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

        // Valid service group values
        private static readonly HashSet<string> ValidServiceGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Medical", "Non-Medical"
        };

        // Valid status values
        private static readonly HashSet<string> ValidStatusValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "pending", "matched", "unmatched", "accepted", "completed", "cancelled", "paused", "closed"
        };

        public CareRequestService(CareProDbContext dbContext, IClientService clientService, INotificationService notificationService, ILogger<CareRequestService> logger)
        {
            _dbContext = dbContext;
            _clientService = clientService;
            _notificationService = notificationService;
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

            // Validate service group
            if (!ValidServiceGroups.Contains(createCareRequestDTO.ServiceGroup))
            {
                throw new ArgumentException($"Invalid service group. Valid values are: {string.Join(", ", ValidServiceGroups)}");
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
                ServiceGroup = createCareRequestDTO.ServiceGroup,
                Notes = createCareRequestDTO.Notes,
                Urgency = createCareRequestDTO.Urgency,
                Schedule = createCareRequestDTO.Schedule ?? new List<string>(),
                Frequency = createCareRequestDTO.Frequency,
                Duration = createCareRequestDTO.Duration,
                Location = createCareRequestDTO.Location,
                Budget = createCareRequestDTO.Budget,
                BudgetMin = createCareRequestDTO.BudgetMin,
                BudgetMax = createCareRequestDTO.BudgetMax,
                BudgetType = createCareRequestDTO.BudgetType,
                SpecialRequirements = createCareRequestDTO.SpecialRequirements,
                Tasks = createCareRequestDTO.Tasks ?? new List<string>(),
                ExperiencePreference = createCareRequestDTO.ExperiencePreference,
                CertificationPreference = createCareRequestDTO.CertificationPreference,
                LanguagePreference = createCareRequestDTO.LanguagePreference,
                ServicePackageType = createCareRequestDTO.ServicePackageType,
                ServiceMode = createCareRequestDTO.ServiceMode,
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

            if (!string.IsNullOrEmpty(updateCareRequestDTO.ServiceGroup))
            {
                if (!ValidServiceGroups.Contains(updateCareRequestDTO.ServiceGroup))
                {
                    throw new ArgumentException($"Invalid service group. Valid values are: {string.Join(", ", ValidServiceGroups)}");
                }
                careRequest.ServiceGroup = updateCareRequestDTO.ServiceGroup;
            }

            if (updateCareRequestDTO.Notes != null)
                careRequest.Notes = updateCareRequestDTO.Notes;

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

            if (updateCareRequestDTO.BudgetMin.HasValue)
                careRequest.BudgetMin = updateCareRequestDTO.BudgetMin;

            if (updateCareRequestDTO.BudgetMax.HasValue)
                careRequest.BudgetMax = updateCareRequestDTO.BudgetMax;

            if (updateCareRequestDTO.BudgetType != null)
                careRequest.BudgetType = updateCareRequestDTO.BudgetType;

            if (updateCareRequestDTO.SpecialRequirements != null)
                careRequest.SpecialRequirements = updateCareRequestDTO.SpecialRequirements;

            if (updateCareRequestDTO.Tasks != null)
                careRequest.Tasks = updateCareRequestDTO.Tasks;

            if (updateCareRequestDTO.ExperiencePreference != null)
                careRequest.ExperiencePreference = updateCareRequestDTO.ExperiencePreference;

            if (updateCareRequestDTO.CertificationPreference != null)
                careRequest.CertificationPreference = updateCareRequestDTO.CertificationPreference;

            if (updateCareRequestDTO.LanguagePreference != null)
                careRequest.LanguagePreference = updateCareRequestDTO.LanguagePreference;

            if (updateCareRequestDTO.ServicePackageType != null)
                careRequest.ServicePackageType = updateCareRequestDTO.ServicePackageType;

            if (updateCareRequestDTO.ServiceMode != null)
                careRequest.ServiceMode = updateCareRequestDTO.ServiceMode;

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

            // Notify pending responders that the request was cancelled
            await NotifyPendingRespondersAsync(requestId, careRequest.Title, NotificationTypes.CareRequestNotSelected,
                "Request Cancelled", $"The care request \"{careRequest.Title}\" has been cancelled by the client.");

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
                ServiceGroup = careRequest.ServiceGroup ?? string.Empty,
                Notes = careRequest.Notes,
                Urgency = careRequest.Urgency,
                Schedule = careRequest.Schedule,
                Frequency = careRequest.Frequency,
                Duration = careRequest.Duration,
                Location = careRequest.Location,
                Budget = careRequest.Budget,
                BudgetMin = careRequest.BudgetMin,
                BudgetMax = careRequest.BudgetMax,
                BudgetType = careRequest.BudgetType,
                SpecialRequirements = careRequest.SpecialRequirements,
                Tasks = careRequest.Tasks ?? new List<string>(),
                ExperiencePreference = careRequest.ExperiencePreference,
                CertificationPreference = careRequest.CertificationPreference,
                LanguagePreference = careRequest.LanguagePreference,
                ServicePackageType = careRequest.ServicePackageType,
                ServiceMode = careRequest.ServiceMode,
                Status = careRequest.Status,
                CreatedAt = careRequest.CreatedAt,
                UpdatedAt = careRequest.UpdatedAt,
                MatchedAt = careRequest.MatchedAt,
                MatchCount = careRequest.MatchCount ?? 0,
                RespondersCount = careRequest.RespondersCount ?? 0
            };
        }

        // ─────────────────────────────────────────────────────────────
        //  Request Lifecycle: Pause, Reopen, Close, Soft-Delete
        // ─────────────────────────────────────────────────────────────

        public async Task<CareRequestDTO> PauseCareRequestAsync(string requestId, string clientId)
        {
            var careRequest = await GetOwnedRequestAsync(requestId, clientId);

            if (careRequest.Status == "paused")
                throw new InvalidOperationException("Request is already paused.");
            if (careRequest.Status == "closed" || careRequest.Status == "cancelled" || careRequest.Status == "completed")
                throw new InvalidOperationException($"Cannot pause a request with status '{careRequest.Status}'.");

            careRequest.Status = "paused";
            careRequest.UpdatedAt = DateTime.UtcNow;
            _dbContext.CareRequests.Update(careRequest);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("CareRequest {RequestId} paused by client {ClientId}", requestId, clientId);
            return MapToDTO(careRequest);
        }

        public async Task<CareRequestDTO> ReopenCareRequestAsync(string requestId, string clientId)
        {
            var careRequest = await GetOwnedRequestAsync(requestId, clientId);

            if (careRequest.Status != "paused")
                throw new InvalidOperationException($"Only paused requests can be reopened. Current status: '{careRequest.Status}'.");

            // Restore to the status before pause: if it had matches, go to matched, otherwise pending
            careRequest.Status = (careRequest.MatchCount ?? 0) > 0 ? "matched" : "pending";
            careRequest.UpdatedAt = DateTime.UtcNow;
            _dbContext.CareRequests.Update(careRequest);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("CareRequest {RequestId} reopened by client {ClientId}", requestId, clientId);
            return MapToDTO(careRequest);
        }

        public async Task<CareRequestDTO> CloseCareRequestAsync(string requestId, string clientId)
        {
            var careRequest = await GetOwnedRequestAsync(requestId, clientId);

            if (careRequest.Status == "closed")
                throw new InvalidOperationException("Request is already closed.");
            if (careRequest.Status == "cancelled")
                throw new InvalidOperationException("Cannot close a cancelled request.");

            careRequest.Status = "closed";
            careRequest.UpdatedAt = DateTime.UtcNow;
            _dbContext.CareRequests.Update(careRequest);
            await _dbContext.SaveChangesAsync();

            // Notify all pending responders that the position has been filled
            await NotifyPendingRespondersAsync(requestId, careRequest.Title, NotificationTypes.CareRequestClosed,
                "Request Closed", $"The care request \"{careRequest.Title}\" has been closed by the client.");

            _logger.LogInformation("CareRequest {RequestId} closed by client {ClientId}", requestId, clientId);
            return MapToDTO(careRequest);
        }

        public async Task SoftDeleteCareRequestAsync(string requestId, string clientId)
        {
            var careRequest = await GetOwnedRequestAsync(requestId, clientId);

            // Only allow delete if no active hires
            var hasActiveHires = await _dbContext.CareRequestResponses
                .AnyAsync(r => r.CareRequestId == requestId && r.Status == "hired");
            if (hasActiveHires)
                throw new InvalidOperationException("Cannot delete a request that has active hires.");

            var allowedForDelete = new HashSet<string> { "pending", "paused", "closed", "unmatched", "cancelled" };
            if (!allowedForDelete.Contains(careRequest.Status))
                throw new InvalidOperationException($"Cannot delete a request with status '{careRequest.Status}'.");

            careRequest.DeletedAt = DateTime.UtcNow;
            careRequest.UpdatedAt = DateTime.UtcNow;
            _dbContext.CareRequests.Update(careRequest);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("CareRequest {RequestId} soft-deleted by client {ClientId}", requestId, clientId);
        }

        private async Task<CareRequest> GetOwnedRequestAsync(string requestId, string clientId)
        {
            if (!ObjectId.TryParse(requestId, out var objectId))
                throw new ArgumentException("Invalid care request ID format.");

            var careRequest = await _dbContext.CareRequests.FindAsync(objectId);
            if (careRequest == null)
                throw new KeyNotFoundException($"Care request with ID '{requestId}' not found.");

            if (careRequest.ClientId != clientId)
                throw new UnauthorizedAccessException("You are not authorized to manage this request.");

            return careRequest;
        }

        private async Task NotifyPendingRespondersAsync(string careRequestId, string requestTitle, string notificationType, string title, string body)
        {
            var pendingResponders = await _dbContext.CareRequestResponses
                .Where(r => r.CareRequestId == careRequestId && (r.Status == "pending" || r.Status == "shortlisted"))
                .Select(r => r.CaregiverId)
                .ToListAsync();

            foreach (var caregiverId in pendingResponders)
            {
                try
                {
                    await _notificationService.CreateNotificationAsync(
                        caregiverId, "system", notificationType, body, title, careRequestId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to notify caregiver {CaregiverId} about request {RequestId} status change", caregiverId, careRequestId);
                }
            }
        }
    }
}
