using Application.Commands;
using Application.DTOs;
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
    public class ObservationReportService : IObservationReportService
    {
        private readonly CareProDbContext _dbContext;
        private readonly CloudinaryService _cloudinaryService;
        private readonly IMediator _mediator;
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
            IMediator mediator,
            ILogger<ObservationReportService> logger)
        {
            _dbContext = dbContext;
            _cloudinaryService = cloudinaryService;
            _mediator = mediator;
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

            var hasOrder = !string.IsNullOrWhiteSpace(request.OrderId);
            var hasAssignment = !string.IsNullOrWhiteSpace(request.AssignmentId);
            if (hasOrder == hasAssignment)
                throw new ArgumentException("Provide exactly one of OrderId or AssignmentId.");

            ClientOrder? order = null;
            Assignment? assignment = null;
            if (hasAssignment)
            {
                // Package-assignment path: ownership + the sheet belonging to this assignment are checked up front.
                assignment = await PackageReportAccess.LoadForCaregiverAsync(
                    _dbContext, request.AssignmentId!, caregiverId, request.TaskSheetId);
            }
            else
            {
                // Validate order and caregiver assignment
                if (!ObjectId.TryParse(request.OrderId, out var orderObjectId))
                    throw new ArgumentException("Invalid order ID format.");

                order = await _dbContext.ClientOrders.FirstOrDefaultAsync(o => o.Id == orderObjectId);
                if (order == null)
                    throw new KeyNotFoundException($"Order '{request.OrderId}' not found.");

                if (order.CaregiverId != caregiverId)
                    throw new UnauthorizedAccessException("Only the assigned caregiver can create observation reports for this order.");
            }

            // Validate photos (max 3)
            if (request.Photos != null && request.Photos.Count > 3)
                throw new ArgumentException("Maximum 3 photos allowed per observation report.");

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
                    var url = await _cloudinaryService.UploadImageAsync(photoBytes, $"obs_report_{caregiverId}_{DateTime.UtcNow:yyyyMMddHHmmss}_{i}");
                    photoUrls.Add(url);
                }
            }

            var report = new ObservationReport
            {
                Id = ObjectId.GenerateNewId(),
                OrderId = hasAssignment ? string.Empty : request.OrderId!,
                AssignmentId = assignment?.Id.ToString(),
                PackageRequestId = assignment?.PackageRequestId,
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

            _logger.LogInformation("Observation report created: {ReportId} for {Target} by Caregiver: {CaregiverId}",
                report.Id, hasAssignment ? $"Assignment {report.AssignmentId}" : $"Order {request.OrderId}", caregiverId);

            // ── Notify client that a new observation report was filed ──
            try
            {
                // Package visits carry the PackageRequestId as RelatedEntityId (OrderId stays null) so the client
                // notification resolves to the request page — the same convention as the visit notifications.
                var recipientClientId = hasAssignment
                    ? await PackageReportAccess.GetClientIdAsync(_dbContext, assignment!.PackageRequestId)
                    : order!.ClientId;
                if (!string.IsNullOrEmpty(recipientClientId))
                {
                    await _mediator.Send(new SendNotificationCommand(
                        RecipientId: recipientClientId,
                        SenderId: caregiverId,
                        Type: NotificationTypes.ObservationReportFiled,
                        Content: $"Your caregiver filed a {report.Severity}-severity observation report for your care session.",
                        Title: "Observation Report Filed",
                        RelatedEntityId: hasAssignment ? assignment!.PackageRequestId : report.Id.ToString(),
                        OrderId: hasAssignment ? null : request.OrderId));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send observation-report notification for ReportId {ReportId}", report.Id);
            }

            return MapToDTO(report);
        }

        public async Task<List<ObservationReportDTO>> GetByOrderAsync(string orderId, string? taskSheetId, string userId, bool isAdmin, bool isClient = false)
        {
            // Validate order access
            if (!ObjectId.TryParse(orderId, out var orderObjectId))
                throw new ArgumentException("Invalid order ID format.");

            var order = await _dbContext.ClientOrders.FirstOrDefaultAsync(o => o.Id == orderObjectId);
            if (order == null)
                throw new KeyNotFoundException($"Order '{orderId}' not found.");

            if (!isAdmin && !(isClient && order.ClientId == userId) && order.CaregiverId != userId)
                throw new UnauthorizedAccessException("You are not authorized to view observation reports for this order.");

            var query = _dbContext.ObservationReports.Where(r => r.OrderId == orderId);

            if (!string.IsNullOrEmpty(taskSheetId))
                query = query.Where(r => r.TaskSheetId == taskSheetId);

            var reports = await query.OrderByDescending(r => r.CreatedAt).ToListAsync();
            return reports.Select(MapToDTO).ToList();
        }

        public async Task<List<ObservationReportDTO>> GetByAssignmentAsync(string assignmentId, string? taskSheetId, string userId, bool isAdmin, bool isClient = false)
        {
            await PackageReportAccess.AuthorizeViewAsync(_dbContext, assignmentId, userId, isAdmin, isClient);

            var query = _dbContext.ObservationReports.Where(r => r.AssignmentId == assignmentId);
            if (!string.IsNullOrEmpty(taskSheetId))
                query = query.Where(r => r.TaskSheetId == taskSheetId);

            var reports = await query.OrderByDescending(r => r.CreatedAt).ToListAsync();
            return reports.Select(MapToDTO).ToList();
        }

        public async Task<List<ObservationReportDTO>> GetByPackageRequestAsClientAsync(string packageRequestId, string? taskSheetId, string clientId)
        {
            if (!ObjectId.TryParse(packageRequestId, out var oid))
                throw new ArgumentException("Invalid package request ID format.");
            var pr = await _dbContext.PackageRequests.FirstOrDefaultAsync(r => r.Id == oid)
                ?? throw new KeyNotFoundException($"Package request '{packageRequestId}' not found.");
            if (pr.ClientId != clientId)
                throw new UnauthorizedAccessException("This package request doesn't belong to you.");

            var query = _dbContext.ObservationReports.Where(r => r.PackageRequestId == packageRequestId);
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
                AssignmentId = entity.AssignmentId,
                PackageRequestId = entity.PackageRequestId,
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
