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
    public class TaskSheetService : ITaskSheetService
    {
        private readonly CareProDbContext _dbContext;
        private readonly CloudinaryService _cloudinaryService;
        private readonly ILogger<TaskSheetService> _logger;

        public TaskSheetService(CareProDbContext dbContext, CloudinaryService cloudinaryService, ILogger<TaskSheetService> logger)
        {
            _dbContext = dbContext;
            _cloudinaryService = cloudinaryService;
            _logger = logger;
        }

        public async Task<TaskSheetListResponse> GetTaskSheetsByOrderAsync(string orderId, int? billingCycleNumber, string caregiverId, bool isAdmin)
        {
            var order = await GetOrderOrThrow(orderId);

            _logger.LogInformation("GetTaskSheets - Order {OrderId} has ClientOrderStatus: '{Status}', CaregiverId: '{CaregiverId}'",
                orderId, order.ClientOrderStatus ?? "(null)", order.CaregiverId);

            // Authorization: only assigned caregiver, order client, or admin
            if (!isAdmin && order.CaregiverId != caregiverId && order.ClientId != caregiverId)
            {
                throw new UnauthorizedAccessException("You are not authorized to view task sheets for this order.");
            }

            var query = _dbContext.TaskSheets
                .Where(ts => ts.OrderId == orderId);

            if (billingCycleNumber.HasValue)
            {
                query = query.Where(ts => ts.BillingCycleNumber == billingCycleNumber.Value);
            }

            var sheets = await query
                .OrderBy(ts => ts.SheetNumber)
                .ToListAsync();

            int maxSheets = CalculateMaxSheets(order);

            // Count sheets for the current billing cycle
            int currentBillingCycle = order.BillingCycleNumber ?? 1;
            int currentSheetCount = billingCycleNumber.HasValue
                ? sheets.Count
                : await _dbContext.TaskSheets
                    .Where(ts => ts.OrderId == orderId && ts.BillingCycleNumber == currentBillingCycle)
                    .CountAsync();

            // Enrich each sheet with check-in, signature, and report counts
            var sheetDtos = new List<TaskSheetDTO>();
            foreach (var sheet in sheets)
            {
                var dto = MapToDTO(sheet);

                var sheetIdStr = sheet.Id.ToString();

                // Check-in data
                var checkin = await _dbContext.VisitCheckins
                    .FirstOrDefaultAsync(vc => vc.TaskSheetId == sheetIdStr);
                if (checkin != null)
                {
                    dto.Checkin = new VisitCheckinDTO
                    {
                        CheckinId = checkin.Id.ToString(),
                        Latitude = checkin.Latitude,
                        Longitude = checkin.Longitude,
                        Accuracy = checkin.Accuracy,
                        DistanceFromServiceAddress = checkin.DistanceFromServiceAddress,
                        CheckinTimestamp = checkin.CheckinTimestamp
                    };
                }

                // Client signature data
                if (sheet.ClientSignatureUrl != null)
                {
                    dto.ClientSignature = new ClientSignatureDTO
                    {
                        SignatureUrl = sheet.ClientSignatureUrl,
                        SignedAt = sheet.ClientSignatureSignedAt ?? sheet.SubmittedAt ?? DateTime.UtcNow
                    };
                }

                // Report counts for UI badges
                dto.ObservationReportCount = await _dbContext.ObservationReports
                    .Where(r => r.TaskSheetId == sheetIdStr).CountAsync();
                dto.IncidentReportCount = await _dbContext.IncidentReports
                    .Where(r => r.TaskSheetId == sheetIdStr).CountAsync();

                sheetDtos.Add(dto);
            }

            return new TaskSheetListResponse
            {
                Sheets = sheetDtos,
                MaxSheets = maxSheets,
                CurrentSheetCount = currentSheetCount
            };
        }

        public async Task<TaskSheetDTO> CreateTaskSheetAsync(string orderId, string caregiverId)
        {
            var order = await GetOrderOrThrow(orderId);

            _logger.LogInformation("CreateTaskSheet - Order {OrderId} has ClientOrderStatus: '{Status}', CaregiverId: '{CaregiverId}'",
                orderId, order.ClientOrderStatus ?? "(null)", order.CaregiverId);

            // Block completed orders
            if (string.Equals(order.ClientOrderStatus, "Completed", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("CreateTaskSheet blocked - Order {OrderId} is completed (status: '{Status}')",
                    orderId, order.ClientOrderStatus);
                throw new InvalidOperationException("This order has been completed. You cannot create new task sheets.");
            }

            // Verify the authenticated user is the assigned caregiver
            if (order.CaregiverId != caregiverId)
            {
                throw new UnauthorizedAccessException("You are not authorized to create task sheets for this order.");
            }

            int currentBillingCycle = order.BillingCycleNumber ?? 1;
            int maxSheets = CalculateMaxSheets(order);

            // Count existing sheets for this order and billing cycle
            int existingCount = await _dbContext.TaskSheets
                .Where(ts => ts.OrderId == orderId && ts.BillingCycleNumber == currentBillingCycle)
                .CountAsync();

            if (existingCount >= maxSheets)
            {
                throw new InvalidOperationException("Maximum task sheets reached for this order.");
            }

            // Get gigPackageDetails from the gig
            var gig = await _dbContext.Gigs.FirstOrDefaultAsync(g => g.Id.ToString() == order.GigId);
            var packageDetails = gig?.PackageDetails ?? new List<string>();

            // Create task items from package details
            var tasks = packageDetails.Select(detail => new TaskSheetItem
            {
                Id = ObjectId.GenerateNewId().ToString(),
                Text = detail,
                Completed = false,
                AddedByCaregiver = false
            }).ToList();

            var taskSheet = new TaskSheet
            {
                Id = ObjectId.GenerateNewId(),
                OrderId = orderId,
                CaregiverId = caregiverId,
                SheetNumber = existingCount + 1,
                BillingCycleNumber = currentBillingCycle,
                Tasks = tasks,
                Status = "in-progress",
                SubmittedAt = null,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _dbContext.TaskSheets.AddAsync(taskSheet);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("TaskSheet created: {TaskSheetId} for Order: {OrderId}, Sheet #{SheetNumber}",
                taskSheet.Id, orderId, taskSheet.SheetNumber);

            return MapToDTO(taskSheet);
        }

        public async Task<TaskSheetDTO> UpdateTaskSheetAsync(string taskSheetId, UpdateTaskSheetRequest request, string caregiverId)
        {
            var taskSheet = await GetTaskSheetOrThrow(taskSheetId);

            // Block completed orders
            var order = await GetOrderOrThrow(taskSheet.OrderId);
            _logger.LogInformation("UpdateTaskSheet - Order {OrderId} has ClientOrderStatus: '{Status}', TaskSheet {TaskSheetId} has Status: '{SheetStatus}'",
                taskSheet.OrderId, order.ClientOrderStatus ?? "(null)", taskSheetId, taskSheet.Status);

            if (string.Equals(order.ClientOrderStatus, "Completed", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("UpdateTaskSheet blocked - Order {OrderId} is completed (status: '{Status}')",
                    taskSheet.OrderId, order.ClientOrderStatus);
                throw new InvalidOperationException("This order has been completed. Task sheets can no longer be updated.");
            }

            // Verify ownership
            if (taskSheet.CaregiverId != caregiverId)
            {
                throw new UnauthorizedAccessException("You are not authorized to update this task sheet.");
            }

            // Cannot update a submitted sheet
            if (taskSheet.Status == "submitted")
            {
                throw new InvalidOperationException("Cannot update a submitted task sheet.");
            }

            // Validate: all original tasks (addedByCaregiver == false) must still be present (no removals allowed)
            var originalTaskIds = taskSheet.Tasks
                .Where(t => !t.AddedByCaregiver)
                .Select(t => t.Id)
                .ToHashSet();

            var existingCaregiverTaskIds = taskSheet.Tasks
                .Where(t => t.AddedByCaregiver)
                .Select(t => t.Id)
                .ToHashSet();

            var incomingOriginalTaskIds = request.Tasks
                .Where(t => !t.AddedByCaregiver)
                .Select(t => t.Id)
                .ToHashSet();

            // Every original task must still be present in the incoming request
            if (!originalTaskIds.IsSubsetOf(incomingOriginalTaskIds))
            {
                _logger.LogWarning("UpdateTaskSheet blocked - Original tasks removed. DB originals: [{DbIds}], Incoming originals: [{InIds}]",
                    string.Join(", ", originalTaskIds), string.Join(", ", incomingOriginalTaskIds));
                throw new InvalidOperationException("Original tasks cannot be removed. Only completion status can be toggled.");
            }

            // No new tasks should claim to be original (addedByCaregiver: false) if they weren't in the original set
            var newFakeOriginals = incomingOriginalTaskIds.Except(originalTaskIds).ToList();
            if (newFakeOriginals.Count > 0)
            {
                _logger.LogWarning("UpdateTaskSheet - Reclassifying {Count} new tasks as caregiver-added: [{Ids}]",
                    newFakeOriginals.Count, string.Join(", ", newFakeOriginals));
            }

            // Existing caregiver-added tasks must not be removed (audit trail)
            var incomingCaregiverTaskIds = request.Tasks
                .Where(t => t.AddedByCaregiver)
                .Where(t => !string.IsNullOrEmpty(t.Id))
                .Select(t => t.Id)
                .ToHashSet();

            if (!existingCaregiverTaskIds.IsSubsetOf(incomingCaregiverTaskIds.Union(incomingOriginalTaskIds)))
            {
                _logger.LogWarning("UpdateTaskSheet blocked - Existing caregiver tasks removed. DB caregiver tasks: [{DbIds}]",
                    string.Join(", ", existingCaregiverTaskIds));
                throw new InvalidOperationException("Existing tasks cannot be removed.");
            }

            // Build the updated tasks list — force correct addedByCaregiver values
            var updatedTasks = new List<TaskSheetItem>();
            foreach (var taskDto in request.Tasks)
            {
                bool isOriginal = originalTaskIds.Contains(taskDto.Id);
                bool isExistingCaregiverTask = existingCaregiverTaskIds.Contains(taskDto.Id);

                updatedTasks.Add(new TaskSheetItem
                {
                    Id = string.IsNullOrEmpty(taskDto.Id) ? ObjectId.GenerateNewId().ToString() : taskDto.Id,
                    Text = taskDto.Text,
                    Completed = taskDto.Completed,
                    // Backend is source of truth: original tasks stay original, everything else is caregiver-added
                    AddedByCaregiver = !isOriginal
                });
            }

            taskSheet.Tasks = updatedTasks;
            taskSheet.UpdatedAt = DateTime.UtcNow;

            _dbContext.TaskSheets.Update(taskSheet);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("TaskSheet updated: {TaskSheetId}", taskSheetId);

            return MapToDTO(taskSheet);
        }

        public async Task<TaskSheetDTO> SubmitTaskSheetAsync(string taskSheetId, SubmitTaskSheetRequest request, string caregiverId)
        {
            var taskSheet = await GetTaskSheetOrThrow(taskSheetId);

            // Block completed orders
            var order = await GetOrderOrThrow(taskSheet.OrderId);
            _logger.LogInformation("SubmitTaskSheet - Order {OrderId} has ClientOrderStatus: '{Status}', TaskSheet {TaskSheetId} has Status: '{SheetStatus}'",
                taskSheet.OrderId, order.ClientOrderStatus ?? "(null)", taskSheetId, taskSheet.Status);

            if (string.Equals(order.ClientOrderStatus, "Completed", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("SubmitTaskSheet blocked - Order {OrderId} is completed (status: '{Status}')",
                    taskSheet.OrderId, order.ClientOrderStatus);
                throw new InvalidOperationException("This order has been completed. Task sheets can no longer be submitted.");
            }

            // Verify ownership
            if (taskSheet.CaregiverId != caregiverId)
            {
                throw new UnauthorizedAccessException("You are not authorized to submit this task sheet.");
            }

            // Cannot submit an already submitted sheet
            if (taskSheet.Status == "submitted")
            {
                throw new InvalidOperationException("This task sheet has already been submitted.");
            }

            // Require check-in before submission
            var checkin = await _dbContext.VisitCheckins
                .FirstOrDefaultAsync(vc => vc.TaskSheetId == taskSheetId);
            if (checkin == null)
            {
                throw new InvalidOperationException("You must check in at the service location before submitting this task sheet.");
            }

            // Handle client signature upload
            if (!string.IsNullOrEmpty(request.ClientSignature))
            {
                var signatureBytes = Convert.FromBase64String(request.ClientSignature);
                var signatureUrl = await _cloudinaryService.UploadImageAsync(
                    signatureBytes,
                    $"client_signature_{taskSheetId}_{DateTime.UtcNow:yyyyMMddHHmmss}");
                taskSheet.ClientSignatureUrl = signatureUrl;
                taskSheet.ClientSignatureSignedAt = request.SignedAt ?? DateTime.UtcNow;
            }

            taskSheet.Status = "submitted";
            taskSheet.SubmittedAt = DateTime.UtcNow;
            taskSheet.UpdatedAt = DateTime.UtcNow;

            _dbContext.TaskSheets.Update(taskSheet);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("TaskSheet submitted: {TaskSheetId}", taskSheetId);

            return MapToDTO(taskSheet);
        }

        // ── Private helpers ──

        private async Task<ClientOrder> GetOrderOrThrow(string orderId)
        {
            if (!ObjectId.TryParse(orderId, out var objectId))
            {
                throw new ArgumentException("Invalid order ID format.");
            }

            var order = await _dbContext.ClientOrders.FirstOrDefaultAsync(o => o.Id == objectId);
            if (order == null)
            {
                throw new KeyNotFoundException($"Order with ID '{orderId}' not found.");
            }

            return order;
        }

        private async Task<TaskSheet> GetTaskSheetOrThrow(string taskSheetId)
        {
            if (!ObjectId.TryParse(taskSheetId, out var objectId))
            {
                throw new ArgumentException("Invalid task sheet ID format.");
            }

            var taskSheet = await _dbContext.TaskSheets.FirstOrDefaultAsync(ts => ts.Id == objectId);
            if (taskSheet == null)
            {
                throw new KeyNotFoundException($"Task sheet with ID '{taskSheetId}' not found.");
            }

            return taskSheet;
        }

        private static int CalculateMaxSheets(ClientOrder order)
        {
            if (string.Equals(order.PaymentOption, "one-time", StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            // Monthly/recurring: frequencyPerWeek * 4
            int frequency = order.FrequencyPerWeek ?? 1;
            return frequency * 4;
        }

        private static TaskSheetDTO MapToDTO(TaskSheet entity)
        {
            return new TaskSheetDTO
            {
                Id = entity.Id.ToString(),
                OrderId = entity.OrderId,
                CaregiverId = entity.CaregiverId,
                SheetNumber = entity.SheetNumber,
                BillingCycleNumber = entity.BillingCycleNumber,
                Tasks = entity.Tasks.Select(t => new TaskSheetItemDTO
                {
                    Id = t.Id,
                    Text = t.Text,
                    Completed = t.Completed,
                    AddedByCaregiver = t.AddedByCaregiver
                }).ToList(),
                Status = entity.Status,
                SubmittedAt = entity.SubmittedAt,
                CreatedAt = entity.CreatedAt,
                UpdatedAt = entity.UpdatedAt
            };
        }
    }
}
