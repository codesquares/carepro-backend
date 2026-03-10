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
    public class IncidentReportService : IIncidentReportService
    {
        private readonly CareProDbContext _dbContext;
        private readonly CloudinaryService _cloudinaryService;
        private readonly INotificationService _notificationService;
        private readonly ILogger<IncidentReportService> _logger;

        private static readonly HashSet<string> ValidIncidentTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "fall", "injury", "medication_error", "property_damage", "behavioral", "health_emergency", "other"
        };

        private static readonly HashSet<string> ValidSeverities = new(StringComparer.OrdinalIgnoreCase)
        {
            "minor", "moderate", "serious", "critical"
        };

        public IncidentReportService(
            CareProDbContext dbContext,
            CloudinaryService cloudinaryService,
            INotificationService notificationService,
            ILogger<IncidentReportService> logger)
        {
            _dbContext = dbContext;
            _cloudinaryService = cloudinaryService;
            _notificationService = notificationService;
            _logger = logger;
        }

        public async Task<IncidentReportDTO> CreateAsync(CreateIncidentReportRequest request, string caregiverId)
        {
            // Validate enums
            if (!ValidIncidentTypes.Contains(request.IncidentType))
                throw new ArgumentException($"Invalid incident type. Must be one of: {string.Join(", ", ValidIncidentTypes)}");

            if (!ValidSeverities.Contains(request.Severity))
                throw new ArgumentException($"Invalid severity. Must be one of: {string.Join(", ", ValidSeverities)}");

            if (string.IsNullOrWhiteSpace(request.Description))
                throw new ArgumentException("Description is required.");

            if (request.Description.Length > 3000)
                throw new ArgumentException("Description must not exceed 3000 characters.");

            if (request.ActionsTaken != null && request.ActionsTaken.Length > 2000)
                throw new ArgumentException("Actions taken must not exceed 2000 characters.");

            // Validate order and caregiver assignment
            if (!ObjectId.TryParse(request.OrderId, out var orderObjectId))
                throw new ArgumentException("Invalid order ID format.");

            var order = await _dbContext.ClientOrders.FirstOrDefaultAsync(o => o.Id == orderObjectId);
            if (order == null)
                throw new KeyNotFoundException($"Order '{request.OrderId}' not found.");

            if (order.CaregiverId != caregiverId)
                throw new UnauthorizedAccessException("Only the assigned caregiver can create incident reports for this order.");

            // Validate photos (max 5)
            if (request.Photos != null && request.Photos.Count > 5)
                throw new ArgumentException("Maximum 5 photos allowed per incident report.");

            // Upload photos to Cloudinary
            var photoUrls = new List<string>();
            if (request.Photos != null)
            {
                for (int i = 0; i < request.Photos.Count; i++)
                {
                    var base64Data = request.Photos[i];
                    // Strip data URL prefix if present (e.g., "data:image/png;base64,...")
                    var commaIndex = base64Data.IndexOf(',');
                    if (commaIndex >= 0)
                    {
                        base64Data = base64Data[(commaIndex + 1)..];
                    }
                    var photoBytes = Convert.FromBase64String(base64Data);
                    var url = await _cloudinaryService.UploadImageAsync(photoBytes, $"incident_report_{caregiverId}_{DateTime.UtcNow:yyyyMMddHHmmss}_{i}");
                    photoUrls.Add(url);
                }
            }

            var report = new IncidentReport
            {
                Id = ObjectId.GenerateNewId(),
                OrderId = request.OrderId,
                TaskSheetId = request.TaskSheetId,
                CaregiverId = caregiverId,
                IncidentType = request.IncidentType.ToLowerInvariant(),
                IncidentDateTime = request.DateTime,
                Description = request.Description,
                ActionsTaken = request.ActionsTaken,
                Witnesses = request.Witnesses,
                Severity = request.Severity.ToLowerInvariant(),
                PhotoUrls = photoUrls,
                ReportedAt = request.ReportedAt,
                CreatedAt = DateTime.UtcNow
            };

            await _dbContext.IncidentReports.AddAsync(report);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Incident report created: {ReportId} for Order: {OrderId}, Severity: {Severity}",
                report.Id, request.OrderId, report.Severity);

            // Notify admins for serious and critical incidents
            var severity = request.Severity.ToLowerInvariant();
            if (severity == "serious" || severity == "critical")
            {
                await NotifyAdminsAsync(report, caregiverId);
            }

            return MapToDTO(report);
        }

        public async Task<List<IncidentReportDTO>> GetByOrderAsync(string orderId, string userId, bool isAdmin, bool isClient = false)
        {
            if (!ObjectId.TryParse(orderId, out var orderObjectId))
                throw new ArgumentException("Invalid order ID format.");

            var order = await _dbContext.ClientOrders.FirstOrDefaultAsync(o => o.Id == orderObjectId);
            if (order == null)
                throw new KeyNotFoundException($"Order '{orderId}' not found.");

            if (!isAdmin && !(isClient && order.ClientId == userId) && order.CaregiverId != userId)
                throw new UnauthorizedAccessException("You are not authorized to view incident reports for this order.");

            var reports = await _dbContext.IncidentReports
                .Where(r => r.OrderId == orderId)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            return reports.Select(MapToDTO).ToList();
        }

        public async Task<int> GetCountByTaskSheetIdAsync(string taskSheetId)
        {
            return await _dbContext.IncidentReports
                .Where(r => r.TaskSheetId == taskSheetId)
                .CountAsync();
        }

        private async System.Threading.Tasks.Task NotifyAdminsAsync(IncidentReport report, string caregiverId)
        {
            try
            {
                var admins = await _dbContext.AdminUsers
                    .Where(a => !a.IsDeleted)
                    .ToListAsync();

                var caregiver = await _dbContext.CareGivers.FirstOrDefaultAsync(c => c.Id.ToString() == caregiverId);
                var caregiverName = caregiver != null ? $"{caregiver.FirstName} {caregiver.LastName}" : "A caregiver";

                var title = $"⚠️ {report.Severity.ToUpperInvariant()} Incident Report";
                var content = $"{caregiverName} reported a {report.Severity} {report.IncidentType.Replace("_", " ")} incident for Order {report.OrderId}. Immediate attention may be required.";

                foreach (var admin in admins)
                {
                    await _notificationService.CreateNotificationAsync(
                        recipientId: admin.Id.ToString(),
                        senderId: caregiverId,
                        type: "IncidentReport",
                        content: content,
                        Title: title,
                        relatedEntityId: report.Id.ToString(),
                        orderId: report.OrderId
                    );
                }

                _logger.LogInformation("Admin notifications sent for {Severity} incident report {ReportId} to {AdminCount} admins",
                    report.Severity, report.Id, admins.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send admin notifications for incident report {ReportId}", report.Id);
            }
        }

        private static IncidentReportDTO MapToDTO(IncidentReport entity)
        {
            return new IncidentReportDTO
            {
                Id = entity.Id.ToString(),
                OrderId = entity.OrderId,
                TaskSheetId = entity.TaskSheetId,
                CaregiverId = entity.CaregiverId,
                IncidentType = entity.IncidentType,
                DateTime = entity.IncidentDateTime,
                Description = entity.Description,
                ActionsTaken = entity.ActionsTaken,
                Witnesses = entity.Witnesses,
                Severity = entity.Severity,
                PhotoUrls = entity.PhotoUrls,
                ReportedAt = entity.ReportedAt,
                CreatedAt = entity.CreatedAt
            };
        }
    }
}
