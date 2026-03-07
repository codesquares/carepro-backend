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
    public class ObservationReportService : IObservationReportService
    {
        private readonly CareProDbContext _dbContext;
        private readonly CloudinaryService _cloudinaryService;
        private readonly ILogger<ObservationReportService> _logger;

        private static readonly HashSet<string> ValidCategories = new(StringComparer.OrdinalIgnoreCase)
        {
            "client_behavior", "environment_concern", "health_observation", "care_plan_concern", "other"
        };

        private static readonly HashSet<string> ValidSeverities = new(StringComparer.OrdinalIgnoreCase)
        {
            "low", "medium", "high"
        };

        public ObservationReportService(
            CareProDbContext dbContext,
            CloudinaryService cloudinaryService,
            ILogger<ObservationReportService> logger)
        {
            _dbContext = dbContext;
            _cloudinaryService = cloudinaryService;
            _logger = logger;
        }

        public async Task<ObservationReportDTO> CreateAsync(CreateObservationReportRequest request, string caregiverId)
        {
            // Validate enums
            if (!ValidCategories.Contains(request.Category))
                throw new ArgumentException($"Invalid category. Must be one of: {string.Join(", ", ValidCategories)}");

            if (!ValidSeverities.Contains(request.Severity))
                throw new ArgumentException($"Invalid severity. Must be one of: {string.Join(", ", ValidSeverities)}");

            if (string.IsNullOrWhiteSpace(request.Description))
                throw new ArgumentException("Description is required.");

            if (request.Description.Length > 2000)
                throw new ArgumentException("Description must not exceed 2000 characters.");

            // Validate order and caregiver assignment
            if (!ObjectId.TryParse(request.OrderId, out var orderObjectId))
                throw new ArgumentException("Invalid order ID format.");

            var order = await _dbContext.ClientOrders.FirstOrDefaultAsync(o => o.Id == orderObjectId);
            if (order == null)
                throw new KeyNotFoundException($"Order '{request.OrderId}' not found.");

            if (order.CaregiverId != caregiverId)
                throw new UnauthorizedAccessException("Only the assigned caregiver can create observation reports for this order.");

            // Validate photos (max 3)
            if (request.Photos != null && request.Photos.Count > 3)
                throw new ArgumentException("Maximum 3 photos allowed per observation report.");

            // Upload photos to Cloudinary
            var photoUrls = new List<string>();
            if (request.Photos != null)
            {
                for (int i = 0; i < request.Photos.Count; i++)
                {
                    var photoBytes = Convert.FromBase64String(request.Photos[i]);
                    var url = await _cloudinaryService.UploadImageAsync(photoBytes, $"obs_report_{caregiverId}_{DateTime.UtcNow:yyyyMMddHHmmss}_{i}");
                    photoUrls.Add(url);
                }
            }

            var report = new ObservationReport
            {
                Id = ObjectId.GenerateNewId(),
                OrderId = request.OrderId,
                TaskSheetId = request.TaskSheetId,
                CaregiverId = caregiverId,
                Category = request.Category.ToLowerInvariant(),
                Description = request.Description,
                Severity = request.Severity.ToLowerInvariant(),
                PhotoUrls = photoUrls,
                ReportedAt = request.ReportedAt,
                CreatedAt = DateTime.UtcNow
            };

            await _dbContext.ObservationReports.AddAsync(report);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Observation report created: {ReportId} for Order: {OrderId} by Caregiver: {CaregiverId}",
                report.Id, request.OrderId, caregiverId);

            return MapToDTO(report);
        }

        public async Task<List<ObservationReportDTO>> GetByOrderAsync(string orderId, string? taskSheetId, string caregiverId, bool isAdmin)
        {
            // Validate order access
            if (!ObjectId.TryParse(orderId, out var orderObjectId))
                throw new ArgumentException("Invalid order ID format.");

            var order = await _dbContext.ClientOrders.FirstOrDefaultAsync(o => o.Id == orderObjectId);
            if (order == null)
                throw new KeyNotFoundException($"Order '{orderId}' not found.");

            if (!isAdmin && order.CaregiverId != caregiverId)
                throw new UnauthorizedAccessException("You are not authorized to view observation reports for this order.");

            var query = _dbContext.ObservationReports.Where(r => r.OrderId == orderId);

            if (!string.IsNullOrEmpty(taskSheetId))
                query = query.Where(r => r.TaskSheetId == taskSheetId);

            var reports = await query.OrderByDescending(r => r.CreatedAt).ToListAsync();
            return reports.Select(MapToDTO).ToList();
        }

        public async Task<int> GetCountByTaskSheetIdAsync(string taskSheetId)
        {
            return await _dbContext.ObservationReports
                .Where(r => r.TaskSheetId == taskSheetId)
                .CountAsync();
        }

        private static ObservationReportDTO MapToDTO(ObservationReport entity)
        {
            return new ObservationReportDTO
            {
                Id = entity.Id.ToString(),
                OrderId = entity.OrderId,
                TaskSheetId = entity.TaskSheetId,
                CaregiverId = entity.CaregiverId,
                Category = entity.Category,
                Description = entity.Description,
                Severity = entity.Severity,
                PhotoUrls = entity.PhotoUrls,
                ReportedAt = entity.ReportedAt,
                CreatedAt = entity.CreatedAt
            };
        }
    }
}
