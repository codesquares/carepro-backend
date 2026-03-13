using Application.DTOs;
using Application.Interfaces.Content;
using Application.Interfaces.Email;
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
        private readonly INotificationService _notificationService;
        private readonly IEmailService _emailService;
        private readonly ILogger<TaskSheetService> _logger;

        public TaskSheetService(
            CareProDbContext dbContext,
            CloudinaryService cloudinaryService,
            INotificationService notificationService,
            IEmailService emailService,
            ILogger<TaskSheetService> logger)
        {
            _dbContext = dbContext;
            _cloudinaryService = cloudinaryService;
            _notificationService = notificationService;
            _emailService = emailService;
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

            // Previous sheet must be approved by client before a new one can be created
            if (existingCount > 0)
            {
                var previousSheet = await _dbContext.TaskSheets
                    .Where(ts => ts.OrderId == orderId && ts.BillingCycleNumber == currentBillingCycle)
                    .OrderByDescending(ts => ts.SheetNumber)
                    .FirstOrDefaultAsync();

                if (previousSheet != null)
                {
                    if (previousSheet.Status != "submitted")
                    {
                        throw new InvalidOperationException(
                            $"Visit #{previousSheet.SheetNumber} has not been submitted yet. Please submit it before creating a new visit.");
                    }

                    if (previousSheet.ClientReviewStatus != "Approved")
                    {
                        throw new InvalidOperationException(
                            $"Visit #{previousSheet.SheetNumber} has not been approved by the client yet. The client must approve the previous visit before a new one can start.");
                    }
                }
            }

            // Prefer tasks from approved contract; fall back to gig package details
            var approvedContract = await _dbContext.Contracts
                .FirstOrDefaultAsync(c => c.OrderId == orderId
                    && c.Status == ContractStatus.Approved);

            List<TaskSheetItem> tasks;

            if (approvedContract?.Tasks != null && approvedContract.Tasks.Count > 0)
            {
                // Use the detailed tasks from the approved contract
                tasks = approvedContract.Tasks.Select(t => new TaskSheetItem
                {
                    Id = ObjectId.GenerateNewId().ToString(),
                    Text = !string.IsNullOrEmpty(t.Description)
                        ? $"{t.Title} — {t.Description}"
                        : t.Title,
                    Completed = false,
                    AddedByCaregiver = false
                }).ToList();

                _logger.LogInformation("TaskSheet for order {OrderId} using {Count} tasks from approved contract {ContractId}",
                    orderId, tasks.Count, approvedContract.Id);
            }
            else
            {
                // No approved contract — fall back to gig package details
                var gig = await _dbContext.Gigs.FirstOrDefaultAsync(g => g.Id.ToString() == order.GigId);
                var packageDetails = gig?.PackageDetails ?? new List<string>();

                tasks = packageDetails.Select(detail => new TaskSheetItem
                {
                    Id = ObjectId.GenerateNewId().ToString(),
                    Text = detail,
                    Completed = false,
                    AddedByCaregiver = false
                }).ToList();
            }

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

            // Block submission if there are still pending client-proposed tasks
            var pendingTasks = taskSheet.Tasks.Where(t => t.ProposalStatus == "Pending").ToList();
            if (pendingTasks.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Cannot submit: {pendingTasks.Count} client-proposed task(s) still pending. Please accept or reject all proposed tasks first.");
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
                var base64Data = request.ClientSignature;
                // Strip data URL prefix if present (e.g., "data:image/png;base64,...")
                var commaIndex = base64Data.IndexOf(',');
                if (commaIndex >= 0)
                {
                    base64Data = base64Data[(commaIndex + 1)..];
                }
                var signatureBytes = Convert.FromBase64String(base64Data);
                var signatureUrl = await _cloudinaryService.UploadImageAsync(
                    signatureBytes,
                    $"client_signature_{taskSheetId}_{DateTime.UtcNow:yyyyMMddHHmmss}");
                taskSheet.ClientSignatureUrl = signatureUrl;
                taskSheet.ClientSignatureSignedAt = request.SignedAt ?? DateTime.UtcNow;
            }

            taskSheet.Status = "submitted";
            taskSheet.SubmittedAt = DateTime.UtcNow;
            taskSheet.UpdatedAt = DateTime.UtcNow;

            // Calculate visit duration from check-in to submission
            taskSheet.VisitDurationMinutes = Math.Round((taskSheet.SubmittedAt.Value - checkin.CheckinTimestamp).TotalMinutes, 1);

            _dbContext.TaskSheets.Update(taskSheet);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("TaskSheet submitted: {TaskSheetId}", taskSheetId);

            // Notify both client and caregiver about submission
            try
            {
                var caregiver = await _dbContext.CareGivers.FirstOrDefaultAsync(c => c.Id.ToString() == taskSheet.CaregiverId);
                var client = await _dbContext.Clients.FirstOrDefaultAsync(c => c.Id.ToString() == order.ClientId);
                var caregiverName = caregiver != null ? $"{caregiver.FirstName} {caregiver.LastName}".Trim() : "Your caregiver";
                var clientName = client != null ? $"{client.FirstName} {client.LastName}".Trim() : "Your client";
                var durationText = taskSheet.VisitDurationMinutes.HasValue
                    ? $" Duration: {taskSheet.VisitDurationMinutes:F0} minutes."
                    : "";

                // Notify client
                await _notificationService.CreateNotificationAsync(
                    recipientId: order.ClientId,
                    senderId: taskSheet.CaregiverId,
                    type: NotificationTypes.VisitSubmitted,
                    content: $"{caregiverName} has completed and submitted visit #{taskSheet.SheetNumber}.{durationText} Please review and approve the visit.",
                    Title: "Visit Submitted for Review",
                    relatedEntityId: taskSheetId,
                    orderId: taskSheet.OrderId
                );

                if (client?.Email != null)
                {
                    await _emailService.SendGenericNotificationEmailAsync(
                        client.Email,
                        client.FirstName ?? "Client",
                        "Visit Submitted for Review",
                        $"{caregiverName} has completed and submitted visit #{taskSheet.SheetNumber} for your order.{durationText} Please log in to review the completed tasks and approve the visit."
                    );
                }

                // Notify caregiver (confirmation)
                await _notificationService.CreateNotificationAsync(
                    recipientId: taskSheet.CaregiverId,
                    senderId: order.ClientId,
                    type: NotificationTypes.VisitSubmitted,
                    content: $"Your visit #{taskSheet.SheetNumber} for {clientName} has been submitted successfully.{durationText} Waiting for client approval.",
                    Title: "Visit Submitted Successfully",
                    relatedEntityId: taskSheetId,
                    orderId: taskSheet.OrderId
                );

                if (caregiver?.Email != null)
                {
                    await _emailService.SendGenericNotificationEmailAsync(
                        caregiver.Email,
                        caregiver.FirstName ?? "Caregiver",
                        "Visit Submitted Successfully",
                        $"Your visit #{taskSheet.SheetNumber} for {clientName} has been submitted successfully.{durationText} You will be notified once the client reviews and approves your visit."
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send submission notifications for TaskSheet {TaskSheetId}", taskSheetId);
            }

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
                    AddedByCaregiver = t.AddedByCaregiver,
                    AddedByClient = t.AddedByClient ?? false,
                    ProposalStatus = t.ProposalStatus ?? "Accepted"
                }).ToList(),
                Status = entity.Status,
                SubmittedAt = entity.SubmittedAt,
                ClientReviewStatus = entity.ClientReviewStatus,
                ClientReviewedAt = entity.ClientReviewedAt,
                ClientDisputeReason = entity.ClientDisputeReason,
                VisitDurationMinutes = entity.VisitDurationMinutes,
                CreatedAt = entity.CreatedAt,
                UpdatedAt = entity.UpdatedAt
            };
        }

        /// <summary>
        /// Client proposes tasks on a task sheet. Tasks are stored as "Pending" until the caregiver accepts.
        /// </summary>
        public async Task<TaskSheetDTO> ClientProposeTasksAsync(string taskSheetId, ClientProposeTasksRequest request, string clientId)
        {
            var taskSheet = await GetTaskSheetOrThrow(taskSheetId);

            var order = await GetOrderOrThrow(taskSheet.OrderId);

            // Block completed orders
            if (string.Equals(order.ClientOrderStatus, "Completed", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("This order has been completed. Tasks can no longer be proposed.");

            // Verify the caller is the client for this order
            if (order.ClientId != clientId)
                throw new UnauthorizedAccessException("You are not authorized to propose tasks on this task sheet.");

            // Cannot propose on a submitted sheet
            if (taskSheet.Status == "submitted")
                throw new InvalidOperationException("Cannot propose tasks on a submitted task sheet.");

            if (request.Tasks == null || request.Tasks.Count == 0)
                throw new ArgumentException("At least one task must be proposed.");

            // Add client-proposed tasks as Pending
            foreach (var proposedTask in request.Tasks)
            {
                if (string.IsNullOrWhiteSpace(proposedTask.Text))
                    continue;

                taskSheet.Tasks.Add(new TaskSheetItem
                {
                    Id = ObjectId.GenerateNewId().ToString(),
                    Text = proposedTask.Text.Trim(),
                    Completed = false,
                    AddedByCaregiver = false,
                    AddedByClient = true,
                    ProposalStatus = "Pending"
                });
            }

            taskSheet.UpdatedAt = DateTime.UtcNow;
            _dbContext.TaskSheets.Update(taskSheet);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Client {ClientId} proposed {Count} tasks on TaskSheet {TaskSheetId}",
                clientId, request.Tasks.Count, taskSheetId);

            // Notify the caregiver about client's proposed tasks
            var taskNames = string.Join(", ", request.Tasks.Where(t => !string.IsNullOrWhiteSpace(t.Text)).Select(t => t.Text.Trim()));
            try
            {
                var client = await _dbContext.Clients.FirstOrDefaultAsync(c => c.Id.ToString() == clientId);
                var clientName = client != null ? $"{client.FirstName} {client.LastName}".Trim() : "Your client";

                await _notificationService.CreateNotificationAsync(
                    recipientId: taskSheet.CaregiverId,
                    senderId: clientId,
                    type: NotificationTypes.TaskProposedByClient,
                    content: $"{clientName} has proposed {request.Tasks.Count} new task(s) on visit #{taskSheet.SheetNumber}: {taskNames}. Please review and accept or reject them before your next submission.",
                    Title: "New Task Proposal from Client",
                    relatedEntityId: taskSheetId,
                    orderId: taskSheet.OrderId
                );

                // Send email to caregiver
                var caregiver = await _dbContext.CareGivers.FirstOrDefaultAsync(c => c.Id.ToString() == taskSheet.CaregiverId);
                if (caregiver?.Email != null)
                {
                    await _emailService.SendGenericNotificationEmailAsync(
                        caregiver.Email,
                        caregiver.FirstName ?? "Caregiver",
                        "New Task Proposal from Client",
                        $"{clientName} has proposed {request.Tasks.Count} new task(s) on visit #{taskSheet.SheetNumber}: {taskNames}. Please log in to review and accept or reject them before your next visit submission."
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send task proposal notifications for TaskSheet {TaskSheetId}", taskSheetId);
            }

            return MapToDTO(taskSheet);
        }

        /// <summary>
        /// Caregiver accepts or rejects client-proposed tasks on a task sheet.
        /// </summary>
        public async Task<TaskSheetDTO> RespondToProposedTasksAsync(string taskSheetId, RespondToProposedTasksRequest request, string caregiverId)
        {
            var taskSheet = await GetTaskSheetOrThrow(taskSheetId);

            var order = await GetOrderOrThrow(taskSheet.OrderId);

            // Block completed orders
            if (string.Equals(order.ClientOrderStatus, "Completed", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("This order has been completed. Task proposals can no longer be responded to.");

            // Verify the caller is the assigned caregiver
            if (taskSheet.CaregiverId != caregiverId)
                throw new UnauthorizedAccessException("You are not authorized to respond to task proposals on this task sheet.");

            // Cannot respond on a submitted sheet
            if (taskSheet.Status == "submitted")
                throw new InvalidOperationException("Cannot respond to task proposals on a submitted task sheet.");

            if (request.Responses == null || request.Responses.Count == 0)
                throw new ArgumentException("At least one response must be provided.");

            var responseMap = request.Responses.ToDictionary(r => r.TaskId, r => r.Accepted);

            var acceptedTasks = new List<string>();
            var rejectedTasks = new List<string>();

            foreach (var task in taskSheet.Tasks)
            {
                if (task.ProposalStatus != "Pending") continue;
                if (!responseMap.TryGetValue(task.Id, out var accepted)) continue;

                task.ProposalStatus = accepted ? "Accepted" : "Rejected";
                if (accepted) acceptedTasks.Add(task.Text);
                else rejectedTasks.Add(task.Text);
            }

            taskSheet.UpdatedAt = DateTime.UtcNow;
            _dbContext.TaskSheets.Update(taskSheet);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Caregiver {CaregiverId} responded to {Count} proposed tasks on TaskSheet {TaskSheetId}",
                caregiverId, request.Responses.Count, taskSheetId);

            // Notify the client about the caregiver's response
            try
            {
                var caregiver = await _dbContext.CareGivers.FirstOrDefaultAsync(c => c.Id.ToString() == caregiverId);
                var caregiverName = caregiver != null ? $"{caregiver.FirstName} {caregiver.LastName}".Trim() : "Your caregiver";

                // Build message parts
                var messageParts = new List<string>();
                if (acceptedTasks.Count > 0)
                    messageParts.Add($"Accepted: {string.Join(", ", acceptedTasks)}");
                if (rejectedTasks.Count > 0)
                    messageParts.Add($"Rejected: {string.Join(", ", rejectedTasks)}");
                var details = string.Join(". ", messageParts);

                // Send notification per accepted/rejected type
                if (acceptedTasks.Count > 0)
                {
                    await _notificationService.CreateNotificationAsync(
                        recipientId: order.ClientId,
                        senderId: caregiverId,
                        type: NotificationTypes.TaskProposalAccepted,
                        content: $"{caregiverName} accepted {acceptedTasks.Count} of your proposed task(s) on visit #{taskSheet.SheetNumber}: {string.Join(", ", acceptedTasks)}.",
                        Title: "Task Proposal Accepted",
                        relatedEntityId: taskSheetId,
                        orderId: taskSheet.OrderId
                    );
                }

                if (rejectedTasks.Count > 0)
                {
                    await _notificationService.CreateNotificationAsync(
                        recipientId: order.ClientId,
                        senderId: caregiverId,
                        type: NotificationTypes.TaskProposalRejected,
                        content: $"{caregiverName} rejected {rejectedTasks.Count} of your proposed task(s) on visit #{taskSheet.SheetNumber}: {string.Join(", ", rejectedTasks)}.",
                        Title: "Task Proposal Rejected",
                        relatedEntityId: taskSheetId,
                        orderId: taskSheet.OrderId
                    );
                }

                // Send email to client
                var client = await _dbContext.Clients.FirstOrDefaultAsync(c => c.Id.ToString() == order.ClientId);
                if (client?.Email != null)
                {
                    await _emailService.SendGenericNotificationEmailAsync(
                        client.Email,
                        client.FirstName ?? "Client",
                        "Update on Your Proposed Tasks",
                        $"{caregiverName} has responded to your proposed tasks on visit #{taskSheet.SheetNumber}. {details}. Log in to view the updated task sheet."
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send task proposal response notifications for TaskSheet {TaskSheetId}", taskSheetId);
            }

            return MapToDTO(taskSheet);
        }
    }
}
