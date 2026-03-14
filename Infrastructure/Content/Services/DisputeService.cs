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
    public class DisputeService : IDisputeService
    {
        private readonly CareProDbContext _dbContext;
        private readonly INotificationService _notificationService;
        private readonly IEarningsLedgerService _ledgerService;
        private readonly ICaregiverWalletService _walletService;
        private readonly ILogger<DisputeService> _logger;

        public DisputeService(
            CareProDbContext dbContext,
            INotificationService notificationService,
            IEarningsLedgerService ledgerService,
            ICaregiverWalletService walletService,
            ILogger<DisputeService> logger)
        {
            _dbContext = dbContext;
            _notificationService = notificationService;
            _ledgerService = ledgerService;
            _walletService = walletService;
            _logger = logger;
        }

        public async Task<DisputeResponse> RaiseDisputeAsync(RaiseDisputeRequest request, string raisedByUserId)
        {
            // ── Validate dispute type ──
            if (request.DisputeType != DisputeType.Visit && request.DisputeType != DisputeType.Order)
                throw new ArgumentException("DisputeType must be 'Visit' or 'Order'.");

            // ── Validate category matches dispute type ──
            if (request.DisputeType == DisputeType.Visit && !DisputeCategory.VisitCategories.Contains(request.Category))
                throw new ArgumentException($"Invalid category '{request.Category}' for visit dispute.");
            if (request.DisputeType == DisputeType.Order && !DisputeCategory.OrderCategories.Contains(request.Category))
                throw new ArgumentException($"Invalid category '{request.Category}' for order dispute.");

            if (string.IsNullOrWhiteSpace(request.Reason))
                throw new ArgumentException("Dispute reason is required.");

            // ── Validate order exists ──
            if (!ObjectId.TryParse(request.OrderId, out var orderObjectId))
                throw new ArgumentException("Invalid order ID format.");

            var order = await _dbContext.ClientOrders.FindAsync(orderObjectId);
            if (order == null)
                throw new KeyNotFoundException($"Order with ID '{request.OrderId}' not found.");

            // ── For visit disputes, validate task sheet exists and belongs to order ──
            if (request.DisputeType == DisputeType.Visit)
            {
                if (string.IsNullOrWhiteSpace(request.TaskSheetId))
                    throw new ArgumentException("TaskSheetId is required for visit disputes.");

                if (!ObjectId.TryParse(request.TaskSheetId, out var tsObjectId))
                    throw new ArgumentException("Invalid task sheet ID format.");

                var taskSheet = await _dbContext.TaskSheets.FindAsync(tsObjectId);
                if (taskSheet == null)
                    throw new KeyNotFoundException($"Task sheet with ID '{request.TaskSheetId}' not found.");
                if (taskSheet.OrderId != request.OrderId)
                    throw new ArgumentException("Task sheet does not belong to the specified order.");
            }

            // ── Check for duplicate open dispute on same target ──
            var existingDispute = await _dbContext.Disputes
                .Where(d => d.OrderId == request.OrderId
                    && d.TaskSheetId == request.TaskSheetId
                    && d.DisputeType == request.DisputeType
                    && (d.Status == DisputeStatus.Open || d.Status == DisputeStatus.UnderReview))
                .FirstOrDefaultAsync();

            if (existingDispute != null)
                throw new InvalidOperationException("An active dispute already exists for this target. Please wait for it to be resolved.");

            // ── Create dispute record ──
            var dispute = new Dispute
            {
                OrderId = request.OrderId,
                TaskSheetId = request.TaskSheetId,
                DisputeType = request.DisputeType,
                Category = request.Category,
                Reason = request.Reason,
                RaisedBy = raisedByUserId,
                ClientId = order.ClientId,
                CaregiverId = order.CaregiverId,
                Status = DisputeStatus.Open
            };

            _dbContext.Disputes.Add(dispute);

            // ── Flag the order as having a dispute ──
            order.HasDispute = true;
            order.DisputeReason = request.Reason;
            order.ClientOrderStatus = "Disputed";
            order.OrderUpdatedOn = DateTime.UtcNow;
            _dbContext.ClientOrders.Update(order);

            // ── If visit dispute, update the task sheet review status ──
            if (request.DisputeType == DisputeType.Visit && !string.IsNullOrWhiteSpace(request.TaskSheetId))
            {
                if (ObjectId.TryParse(request.TaskSheetId, out var tsId))
                {
                    var taskSheet = await _dbContext.TaskSheets.FindAsync(tsId);
                    if (taskSheet != null)
                    {
                        taskSheet.ClientReviewStatus = "Disputed";
                        taskSheet.ClientReviewedAt = DateTime.UtcNow;
                        taskSheet.ClientDisputeReason = request.Reason;
                        taskSheet.UpdatedAt = DateTime.UtcNow;
                        _dbContext.TaskSheets.Update(taskSheet);
                    }
                }
            }

            await _dbContext.SaveChangesAsync();

            // ── Record dispute hold in ledger ──
            try
            {
                await _ledgerService.RecordDisputeHoldAsync(
                    order.CaregiverId, order.Amount, request.OrderId,
                    $"Dispute raised ({dispute.DisputeType}/{dispute.Category}): {dispute.Reason}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error recording dispute hold for order {OrderId}", request.OrderId);
            }

            // ── Send notifications ──
            await NotifyDisputeRaisedAsync(dispute, order);

            _logger.LogInformation("Dispute {DisputeId} raised on order {OrderId} by user {UserId}. Type: {Type}, Category: {Category}",
                dispute.Id, request.OrderId, raisedByUserId, request.DisputeType, request.Category);

            return await BuildDisputeResponseAsync(dispute);
        }

        public async Task<DisputeResponse> ResolveDisputeAsync(string disputeId, ResolveDisputeRequest request, string adminUserId)
        {
            var dispute = await GetDisputeEntityAsync(disputeId);

            if (dispute.Status == DisputeStatus.Resolved || dispute.Status == DisputeStatus.Dismissed)
                throw new InvalidOperationException($"Dispute is already {dispute.Status}.");

            if (string.IsNullOrWhiteSpace(request.ResolutionAction))
                throw new ArgumentException("Resolution action is required.");
            if (string.IsNullOrWhiteSpace(request.AdminNotes))
                throw new ArgumentException("Admin notes are required.");
            if (string.IsNullOrWhiteSpace(request.ResolutionSummary))
                throw new ArgumentException("Resolution summary is required.");

            dispute.Status = DisputeStatus.Resolved;
            dispute.ResolutionAction = request.ResolutionAction;
            dispute.AdminNotes = request.AdminNotes;
            dispute.ResolutionSummary = request.ResolutionSummary;
            dispute.ResolvedBy = adminUserId;
            dispute.ResolvedAt = DateTime.UtcNow;
            dispute.UpdatedAt = DateTime.UtcNow;

            _dbContext.Disputes.Update(dispute);

            // ── Check if all disputes for this order are resolved/dismissed ──
            var hasActiveDisputes = await _dbContext.Disputes
                .Where(d => d.OrderId == dispute.OrderId
                    && d.Id != dispute.Id
                    && (d.Status == DisputeStatus.Open || d.Status == DisputeStatus.UnderReview))
                .AnyAsync();

            if (!hasActiveDisputes)
            {
                // Clear the order-level dispute flag and restore status
                if (ObjectId.TryParse(dispute.OrderId, out var orderObjectId))
                {
                    var order = await _dbContext.ClientOrders.FindAsync(orderObjectId);
                    if (order != null)
                    {
                        order.HasDispute = false;
                        if (order.ClientOrderStatus == "Disputed")
                            order.ClientOrderStatus = "In Progress";
                        order.OrderUpdatedOn = DateTime.UtcNow;
                        _dbContext.ClientOrders.Update(order);
                    }
                }
            }

            await _dbContext.SaveChangesAsync();

            // ── Notify client and caregiver of resolution ──
            await NotifyDisputeResolvedAsync(dispute);

            _logger.LogInformation("Dispute {DisputeId} resolved by admin {AdminId}. Action: {Action}",
                disputeId, adminUserId, request.ResolutionAction);

            return await BuildDisputeResponseAsync(dispute);
        }

        public async Task<DisputeResponse> MarkUnderReviewAsync(string disputeId, string adminUserId)
        {
            var dispute = await GetDisputeEntityAsync(disputeId);

            if (dispute.Status != DisputeStatus.Open)
                throw new InvalidOperationException($"Only Open disputes can be moved to UnderReview. Current status: {dispute.Status}");

            dispute.Status = DisputeStatus.UnderReview;
            dispute.UpdatedAt = DateTime.UtcNow;

            _dbContext.Disputes.Update(dispute);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Dispute {DisputeId} marked UnderReview by admin {AdminId}", disputeId, adminUserId);

            return await BuildDisputeResponseAsync(dispute);
        }

        public async Task<DisputeResponse> DismissDisputeAsync(string disputeId, ResolveDisputeRequest request, string adminUserId)
        {
            var dispute = await GetDisputeEntityAsync(disputeId);

            if (dispute.Status == DisputeStatus.Resolved || dispute.Status == DisputeStatus.Dismissed)
                throw new InvalidOperationException($"Dispute is already {dispute.Status}.");

            if (string.IsNullOrWhiteSpace(request.AdminNotes))
                throw new ArgumentException("Admin notes are required when dismissing a dispute.");

            dispute.Status = DisputeStatus.Dismissed;
            dispute.ResolutionAction = DisputeResolutionAction.NoAction;
            dispute.AdminNotes = request.AdminNotes;
            dispute.ResolutionSummary = request.ResolutionSummary;
            dispute.ResolvedBy = adminUserId;
            dispute.ResolvedAt = DateTime.UtcNow;
            dispute.UpdatedAt = DateTime.UtcNow;

            _dbContext.Disputes.Update(dispute);

            // ── Check if all disputes for this order are resolved/dismissed ──
            var hasActiveDisputes = await _dbContext.Disputes
                .Where(d => d.OrderId == dispute.OrderId
                    && d.Id != dispute.Id
                    && (d.Status == DisputeStatus.Open || d.Status == DisputeStatus.UnderReview))
                .AnyAsync();

            if (!hasActiveDisputes)
            {
                if (ObjectId.TryParse(dispute.OrderId, out var orderObjectId))
                {
                    var order = await _dbContext.ClientOrders.FindAsync(orderObjectId);
                    if (order != null)
                    {
                        order.HasDispute = false;
                        if (order.ClientOrderStatus == "Disputed")
                            order.ClientOrderStatus = "In Progress";
                        order.OrderUpdatedOn = DateTime.UtcNow;
                        _dbContext.ClientOrders.Update(order);
                    }
                }
            }

            await _dbContext.SaveChangesAsync();

            await NotifyDisputeResolvedAsync(dispute);

            _logger.LogInformation("Dispute {DisputeId} dismissed by admin {AdminId}", disputeId, adminUserId);

            return await BuildDisputeResponseAsync(dispute);
        }

        public async Task<DisputeResponse> GetDisputeByIdAsync(string disputeId)
        {
            var dispute = await GetDisputeEntityAsync(disputeId);
            return await BuildDisputeResponseAsync(dispute);
        }

        public async Task<List<DisputeResponse>> GetDisputesByOrderIdAsync(string orderId)
        {
            var disputes = await _dbContext.Disputes
                .Where(d => d.OrderId == orderId)
                .OrderByDescending(d => d.CreatedAt)
                .ToListAsync();

            var responses = new List<DisputeResponse>();
            foreach (var d in disputes)
                responses.Add(await BuildDisputeResponseAsync(d));
            return responses;
        }

        public async Task<List<DisputeResponse>> GetDisputesByTaskSheetIdAsync(string taskSheetId)
        {
            var disputes = await _dbContext.Disputes
                .Where(d => d.TaskSheetId == taskSheetId)
                .OrderByDescending(d => d.CreatedAt)
                .ToListAsync();

            var responses = new List<DisputeResponse>();
            foreach (var d in disputes)
                responses.Add(await BuildDisputeResponseAsync(d));
            return responses;
        }

        public async Task<PaginatedResponse<DisputeResponse>> GetAllDisputesAsync(
            int page = 1, int pageSize = 20, string? status = null, string? disputeType = null)
        {
            var query = _dbContext.Disputes.AsQueryable();

            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(d => d.Status == status);

            if (!string.IsNullOrWhiteSpace(disputeType))
                query = query.Where(d => d.DisputeType == disputeType);

            var totalCount = await query.CountAsync();

            var disputes = await query
                .OrderByDescending(d => d.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var responses = new List<DisputeResponse>();
            foreach (var d in disputes)
                responses.Add(await BuildDisputeResponseAsync(d));

            return new PaginatedResponse<DisputeResponse>
            {
                Items = responses,
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                HasMore = page * pageSize < totalCount
            };
        }

        public async Task<DisputeResponse?> ReviewVisitAsync(string taskSheetId, ReviewVisitRequest request, string clientUserId)
        {
            if (request.ReviewStatus != "Approved" && request.ReviewStatus != "Disputed")
                throw new ArgumentException("ReviewStatus must be 'Approved' or 'Disputed'.");

            if (!ObjectId.TryParse(taskSheetId, out var tsObjectId))
                throw new ArgumentException("Invalid task sheet ID format.");

            var taskSheet = await _dbContext.TaskSheets.FindAsync(tsObjectId);
            if (taskSheet == null)
                throw new KeyNotFoundException($"Task sheet with ID '{taskSheetId}' not found.");

            // ── IDOR check: verify the calling client actually owns this order ──
            var ownerOrder = await _dbContext.ClientOrders.FirstOrDefaultAsync(
                o => o.Id.ToString() == taskSheet.OrderId);
            if (ownerOrder == null)
                throw new KeyNotFoundException($"Order '{taskSheet.OrderId}' not found.");
            if (ownerOrder.ClientId != clientUserId)
                throw new UnauthorizedAccessException("You are not authorized to review this visit.");

            // Update task sheet review fields
            taskSheet.ClientReviewStatus = request.ReviewStatus;
            taskSheet.ClientReviewedAt = DateTime.UtcNow;
            taskSheet.UpdatedAt = DateTime.UtcNow;

            if (request.ReviewStatus == "Disputed")
            {
                if (string.IsNullOrWhiteSpace(request.DisputeReason))
                    throw new ArgumentException("Dispute reason is required when disputing a visit.");

                taskSheet.ClientDisputeReason = request.DisputeReason;

                _dbContext.TaskSheets.Update(taskSheet);
                await _dbContext.SaveChangesAsync();

                // Auto-create a dispute record
                var category = !string.IsNullOrWhiteSpace(request.DisputeCategory)
                    && DisputeCategory.VisitCategories.Contains(request.DisputeCategory)
                    ? request.DisputeCategory
                    : DisputeCategory.Other;

                // Use Other as fallback for visit disputes with invalid/missing category
                if (!DisputeCategory.VisitCategories.Contains(category))
                    category = DisputeCategory.QualityOfCare;

                var raiseRequest = new RaiseDisputeRequest
                {
                    OrderId = taskSheet.OrderId,
                    TaskSheetId = taskSheetId,
                    DisputeType = DisputeType.Visit,
                    Category = category,
                    Reason = request.DisputeReason
                };

                return await RaiseDisputeAsync(raiseRequest, clientUserId);
            }

            // Approved path
            taskSheet.ClientDisputeReason = null;
            _dbContext.TaskSheets.Update(taskSheet);
            await _dbContext.SaveChangesAsync();

            // ── Per-visit wallet credit: release this visit's share from PendingBalance → WithdrawableBalance ──
            try
            {
                // ownerOrder already loaded during IDOR check above
                var order = ownerOrder;

                if (order != null)
                {
                    // Calculate max visits for this order/cycle
                    int maxVisits = string.Equals(order.PaymentOption, "one-time", StringComparison.OrdinalIgnoreCase)
                        ? 1
                        : (order.FrequencyPerWeek ?? 1) * 4;

                    // Per-visit amount = (OrderFee × 0.80) / totalVisits
                    decimal caregiverTotal = Math.Round((order.OrderFee ?? 0m) * 0.80m, 2);
                    decimal perVisitAmount = Math.Round(caregiverTotal / maxVisits, 2);

                    // Rounding remainder: adjust the last visit so total credits equal caregiverTotal exactly
                    int currentBillingCycleForCalc = order.BillingCycleNumber ?? 1;
                    int alreadyCreditedCount = await _dbContext.EarningsLedger
                        .CountAsync(e => e.ClientOrderId == order.Id.ToString()
                            && e.Type == LedgerEntryType.VisitApproved
                            && e.BillingCycleNumber == currentBillingCycleForCalc);
                    if (alreadyCreditedCount == maxVisits - 1)
                    {
                        // This is the last visit — absorb rounding remainder
                        decimal alreadyCreditedTotal = perVisitAmount * alreadyCreditedCount;
                        perVisitAmount = caregiverTotal - alreadyCreditedTotal;
                    }

                    // Idempotency: check if this visit was already credited
                    bool alreadyCreditedVisit = await _dbContext.EarningsLedger
                        .AnyAsync(e => e.TaskSheetId == taskSheetId && e.Type == LedgerEntryType.VisitApproved);

                    if (!alreadyCreditedVisit && perVisitAmount > 0)
                    {
                        // Write ledger FIRST (idempotent) — prevents race condition with DailyEarningService
                        string serviceType = string.IsNullOrEmpty(order.SubscriptionId) ? "one-time" : "monthly";
                        await _ledgerService.RecordVisitApprovedAsync(
                            order.CaregiverId, perVisitAmount, order.Id.ToString(), taskSheetId,
                            order.SubscriptionId, order.BillingCycleNumber, serviceType,
                            $"Visit #{taskSheet.SheetNumber} approved — ₦{perVisitAmount} released (visit {taskSheet.SheetNumber}/{maxVisits})");

                        // Credit wallet AFTER ledger write succeeds
                        await _walletService.CreditVisitApprovedAsync(order.CaregiverId, perVisitAmount);

                        _logger.LogInformation(
                            "Per-visit credit: ₦{Amount} released for caregiver {CaregiverId}, visit {SheetNumber}/{MaxVisits}, order {OrderId}",
                            perVisitAmount, order.CaregiverId, taskSheet.SheetNumber, maxVisits, order.Id);
                    }

                    // ── Auto-complete order if ALL visits for this cycle are approved ──
                    int currentBillingCycle = order.BillingCycleNumber ?? 1;
                    int approvedCount = await _dbContext.TaskSheets
                        .Where(ts => ts.OrderId == taskSheet.OrderId
                            && ts.BillingCycleNumber == currentBillingCycle
                            && ts.ClientReviewStatus == "Approved")
                        .CountAsync();

                    if (approvedCount >= maxVisits && order.ClientOrderStatus != "Completed")
                    {
                        order.ClientOrderStatus = "Completed";
                        order.IsOrderStatusApproved = true;
                        order.OrderUpdatedOn = DateTime.UtcNow;
                        _dbContext.ClientOrders.Update(order);
                        await _dbContext.SaveChangesAsync();

                        _logger.LogInformation(
                            "Order {OrderId} auto-completed: all {Max} visits approved for billing cycle {Cycle}.",
                            order.Id, maxVisits, currentBillingCycle);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing per-visit credit for TaskSheet {TaskSheetId}", taskSheetId);
                // Don't fail the approval — wallet can be reconciled later
            }

            _logger.LogInformation("Visit {TaskSheetId} approved by client {ClientId}", taskSheetId, clientUserId);
            return null; // No dispute created for approvals
        }

        // ── Private helpers ──

        private async Task<Dispute> GetDisputeEntityAsync(string disputeId)
        {
            if (!ObjectId.TryParse(disputeId, out var objectId))
                throw new ArgumentException("Invalid dispute ID format.");

            var dispute = await _dbContext.Disputes.FindAsync(objectId);
            if (dispute == null)
                throw new KeyNotFoundException($"Dispute with ID '{disputeId}' not found.");

            return dispute;
        }

        private async Task<DisputeResponse> BuildDisputeResponseAsync(Dispute dispute)
        {
            var response = new DisputeResponse
            {
                Id = dispute.Id.ToString(),
                OrderId = dispute.OrderId,
                TaskSheetId = dispute.TaskSheetId,
                DisputeType = dispute.DisputeType,
                Category = dispute.Category,
                Reason = dispute.Reason,
                RaisedBy = dispute.RaisedBy,
                ClientId = dispute.ClientId,
                CaregiverId = dispute.CaregiverId,
                Status = dispute.Status,
                ResolutionAction = dispute.ResolutionAction,
                AdminNotes = dispute.AdminNotes,
                ResolutionSummary = dispute.ResolutionSummary,
                ResolvedBy = dispute.ResolvedBy,
                ResolvedAt = dispute.ResolvedAt,
                CreatedAt = dispute.CreatedAt,
                UpdatedAt = dispute.UpdatedAt
            };

            // Resolve names
            try
            {
                var client = await _dbContext.Clients.FirstOrDefaultAsync(c => c.Id.ToString() == dispute.ClientId);
                if (client != null) response.ClientName = $"{client.FirstName} {client.LastName}";

                var caregiver = await _dbContext.CareGivers.FirstOrDefaultAsync(c => c.Id.ToString() == dispute.CaregiverId);
                if (caregiver != null) response.CaregiverName = $"{caregiver.FirstName} {caregiver.LastName}";

                if (!string.IsNullOrWhiteSpace(dispute.RaisedBy))
                {
                    // Could be client or admin
                    if (dispute.RaisedBy == dispute.ClientId)
                        response.RaisedByName = response.ClientName;
                    else
                    {
                        var admin = await _dbContext.AdminUsers.FirstOrDefaultAsync(a => a.Id.ToString() == dispute.RaisedBy);
                        if (admin != null) response.RaisedByName = $"{admin.FirstName} {admin.LastName}";
                    }
                }

                if (!string.IsNullOrWhiteSpace(dispute.ResolvedBy))
                {
                    var admin = await _dbContext.AdminUsers.FirstOrDefaultAsync(a => a.Id.ToString() == dispute.ResolvedBy);
                    if (admin != null) response.ResolvedByName = $"{admin.FirstName} {admin.LastName}";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error resolving names for dispute {DisputeId}", dispute.Id);
            }

            return response;
        }

        private async System.Threading.Tasks.Task NotifyDisputeRaisedAsync(Dispute dispute, ClientOrder order)
        {
            try
            {
                var client = await _dbContext.Clients.FirstOrDefaultAsync(c => c.Id.ToString() == order.ClientId);
                var caregiver = await _dbContext.CareGivers.FirstOrDefaultAsync(c => c.Id.ToString() == order.CaregiverId);
                var clientName = client != null ? $"{client.FirstName} {client.LastName}" : "A client";

                var disputeTarget = dispute.DisputeType == DisputeType.Visit
                    ? $"visit (Task Sheet #{dispute.TaskSheetId})"
                    : "order";
                var title = $"Dispute Raised on {disputeTarget}";
                var content = $"{clientName} has raised a {dispute.Category.Replace("_", " ")} dispute on {disputeTarget} for Order {order.Id}. Reason: {dispute.Reason}";

                // ── Notify all admins ──
                var admins = await _dbContext.AdminUsers
                    .Where(a => !a.IsDeleted)
                    .ToListAsync();

                foreach (var admin in admins)
                {
                    await _notificationService.CreateNotificationAsync(
                        recipientId: admin.Id.ToString(),
                        senderId: dispute.RaisedBy,
                        type: NotificationTypes.DisputeRaised,
                        content: $"[ACTION REQUIRED] {content}",
                        Title: title,
                        relatedEntityId: dispute.Id.ToString(),
                        orderId: dispute.OrderId);
                }

                // ── Notify caregiver ──
                if (caregiver != null)
                {
                    await _notificationService.CreateNotificationAsync(
                        recipientId: order.CaregiverId,
                        senderId: dispute.RaisedBy,
                        type: NotificationTypes.DisputeRaised,
                        content: $"A dispute has been raised on your {disputeTarget} for Order {order.Id}. Category: {dispute.Category}. An admin will review this shortly.",
                        Title: title,
                        relatedEntityId: dispute.Id.ToString(),
                        orderId: dispute.OrderId);
                }

                // ── Confirm to client ──
                await _notificationService.CreateNotificationAsync(
                    recipientId: order.ClientId,
                    senderId: dispute.RaisedBy,
                    type: NotificationTypes.DisputeRaised,
                    content: $"Your dispute on {disputeTarget} for Order {order.Id} has been submitted. An admin will review it shortly.",
                    Title: "Dispute Submitted",
                    relatedEntityId: dispute.Id.ToString(),
                    orderId: dispute.OrderId);

                _logger.LogInformation("Dispute notifications sent for dispute {DisputeId} to {AdminCount} admins, caregiver, and client",
                    dispute.Id, admins.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send dispute raised notifications for dispute {DisputeId}", dispute.Id);
            }
        }

        private async System.Threading.Tasks.Task NotifyDisputeResolvedAsync(Dispute dispute)
        {
            try
            {
                var admin = !string.IsNullOrWhiteSpace(dispute.ResolvedBy)
                    ? await _dbContext.AdminUsers.FirstOrDefaultAsync(a => a.Id.ToString() == dispute.ResolvedBy)
                    : null;
                var adminName = admin != null ? $"{admin.FirstName} {admin.LastName}" : "An admin";

                var status = dispute.Status == DisputeStatus.Dismissed ? "dismissed" : "resolved";
                var title = $"Dispute {char.ToUpper(status[0])}{status[1..]}";
                var actionText = dispute.ResolutionAction != null ? $" Action: {dispute.ResolutionAction}." : "";

                // ── Notify client ──
                await _notificationService.CreateNotificationAsync(
                    recipientId: dispute.ClientId,
                    senderId: dispute.ResolvedBy ?? string.Empty,
                    type: NotificationTypes.DisputeResolved,
                    content: $"Your dispute on Order {dispute.OrderId} has been {status} by {adminName}.{actionText} {dispute.ResolutionSummary}",
                    Title: title,
                    relatedEntityId: dispute.Id.ToString(),
                    orderId: dispute.OrderId);

                // ── Notify caregiver ──
                await _notificationService.CreateNotificationAsync(
                    recipientId: dispute.CaregiverId,
                    senderId: dispute.ResolvedBy ?? string.Empty,
                    type: NotificationTypes.DisputeResolved,
                    content: $"A dispute on Order {dispute.OrderId} has been {status}.{actionText} {dispute.ResolutionSummary}",
                    Title: title,
                    relatedEntityId: dispute.Id.ToString(),
                    orderId: dispute.OrderId);

                _logger.LogInformation("Dispute resolution notifications sent for dispute {DisputeId}", dispute.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send dispute resolution notifications for dispute {DisputeId}", dispute.Id);
            }
        }
    }
}
