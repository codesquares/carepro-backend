using Application.DTOs;
using Application.Interfaces.Content;
using Application.Interfaces.Email;
using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;

namespace Infrastructure.Content.Services
{
    public class RefundRequestService : IRefundRequestService
    {
        private readonly CareProDbContext _dbContext;
        private readonly IClientWalletService _clientWalletService;
        private readonly IClientService _clientService;
        private readonly INotificationService _notificationService;
        private readonly IEmailService _emailService;
        private readonly ILogger<RefundRequestService> _logger;

        public RefundRequestService(
            CareProDbContext dbContext,
            IClientWalletService clientWalletService,
            IClientService clientService,
            INotificationService notificationService,
            IEmailService emailService,
            ILogger<RefundRequestService> logger)
        {
            _dbContext = dbContext;
            _clientWalletService = clientWalletService;
            _clientService = clientService;
            _notificationService = notificationService;
            _emailService = emailService;
            _logger = logger;
        }

        public async Task<Result<RefundRequestResponse>> CreateRefundRequestAsync(CreateRefundRequestDTO request, string clientId)
        {
            if (string.IsNullOrWhiteSpace(clientId))
                return Result<RefundRequestResponse>.Failure(new List<string> { "Client authorization required." });

            // Check client wallet balance
            var wallet = await _clientWalletService.GetOrCreateWalletAsync(clientId);
            if (wallet.CreditBalance < request.Amount)
            {
                return Result<RefundRequestResponse>.Failure(new List<string>
                {
                    $"Insufficient wallet balance. Available: ₦{wallet.CreditBalance:N2}, Requested: ₦{request.Amount:N2}"
                });
            }

            // Check for existing pending refund request
            var existingPending = await _dbContext.RefundRequests
                .AnyAsync(r => r.ClientId == clientId && r.Status == RefundRequestStatus.Pending);
            if (existingPending)
            {
                return Result<RefundRequestResponse>.Failure(new List<string>
                {
                    "You already have a pending refund request. Please wait for it to be reviewed before submitting another."
                });
            }

            var refundRequest = new RefundRequest
            {
                Id = ObjectId.GenerateNewId(),
                ClientId = clientId,
                Amount = request.Amount,
                Reason = request.Reason,
                Status = RefundRequestStatus.Pending,
                WalletBalanceAtRequest = wallet.CreditBalance,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.RefundRequests.Add(refundRequest);
            await _dbContext.SaveChangesAsync();

            // Notify client
            await _notificationService.CreateNotificationAsync(
                clientId, clientId,
                NotificationTypes.RefundRequested,
                $"Your refund request for ₦{request.Amount:N2} has been submitted and is pending review.",
                "Refund Request Submitted",
                refundRequest.Id.ToString());

            _logger.LogInformation("Refund request {RequestId} created by client {ClientId} for ₦{Amount}",
                refundRequest.Id, clientId, request.Amount);

            var client = await _clientService.GetClientUserAsync(clientId);
            return Result<RefundRequestResponse>.Success(MapToResponse(refundRequest, client));
        }

        public async Task<List<RefundRequestResponse>> GetClientRefundRequestsAsync(string clientId)
        {
            var requests = await _dbContext.RefundRequests
                .Where(r => r.ClientId == clientId)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            var client = await _clientService.GetClientUserAsync(clientId);
            return requests.Select(r => MapToResponse(r, client)).ToList();
        }

        public async Task<RefundRequestResponse> GetRefundRequestAsync(string requestId, string? clientId = null)
        {
            if (!ObjectId.TryParse(requestId, out var objectId))
                throw new ArgumentException("Invalid refund request ID format.");

            var request = await _dbContext.RefundRequests.FindAsync(objectId);
            if (request == null)
                throw new KeyNotFoundException($"Refund request '{requestId}' not found.");

            // IDOR: if clientId is provided, verify ownership
            if (!string.IsNullOrEmpty(clientId) && request.ClientId != clientId)
                throw new UnauthorizedAccessException("You are not authorized to view this refund request.");

            var client = await _clientService.GetClientUserAsync(request.ClientId);
            return MapToResponse(request, client);
        }

        public async Task<List<RefundRequestResponse>> GetAllRefundRequestsAsync(string? status = null)
        {
            var query = _dbContext.RefundRequests.AsQueryable();

            if (!string.IsNullOrEmpty(status))
                query = query.Where(r => r.Status == status);

            var requests = await query.OrderByDescending(r => r.CreatedAt).ToListAsync();

            // Batch-load client info
            var clientIds = requests.Select(r => r.ClientId).Distinct().ToList();
            var clients = new Dictionary<string, ClientResponse?>();
            foreach (var cid in clientIds)
            {
                try { clients[cid] = await _clientService.GetClientUserAsync(cid); }
                catch { clients[cid] = null; }
            }

            return requests.Select(r => MapToResponse(r, clients.GetValueOrDefault(r.ClientId))).ToList();
        }

        public async Task<Result<RefundRequestResponse>> ReviewRefundRequestAsync(string requestId, ReviewRefundRequestDTO review, string adminId)
        {
            if (!ObjectId.TryParse(requestId, out var objectId))
                return Result<RefundRequestResponse>.Failure(new List<string> { "Invalid refund request ID format." });

            var request = await _dbContext.RefundRequests.FindAsync(objectId);
            if (request == null)
                return Result<RefundRequestResponse>.Failure(new List<string> { "Refund request not found." });

            if (request.Status != RefundRequestStatus.Pending)
                return Result<RefundRequestResponse>.Failure(new List<string>
                {
                    $"This request has already been reviewed. Current status: {request.Status}"
                });

            // Validate status
            if (review.Status != RefundRequestStatus.Approved && review.Status != RefundRequestStatus.Rejected)
                return Result<RefundRequestResponse>.Failure(new List<string>
                {
                    "Status must be 'Approved' or 'Rejected'."
                });

            request.Status = review.Status;
            request.ReviewedByAdminId = adminId;
            request.AdminNote = review.AdminNote;
            request.ReviewedAt = DateTime.UtcNow;

            // On approval, debit the client wallet
            if (review.Status == RefundRequestStatus.Approved)
            {
                // Re-verify balance at approval time
                var wallet = await _clientWalletService.GetOrCreateWalletAsync(request.ClientId);
                if (wallet.CreditBalance < request.Amount)
                {
                    return Result<RefundRequestResponse>.Failure(new List<string>
                    {
                        $"Client's wallet balance (₦{wallet.CreditBalance:N2}) is now less than the requested refund (₦{request.Amount:N2}). Cannot approve."
                    });
                }

                await _clientWalletService.DebitAsync(
                    request.ClientId, request.Amount,
                    $"Refund approved — ₦{request.Amount:N2} to be transferred to bank account",
                    null);

                _logger.LogInformation("Refund request {RequestId} approved by admin {AdminId}. ₦{Amount} debited from client {ClientId} wallet.",
                    requestId, adminId, request.Amount, request.ClientId);
            }

            _dbContext.RefundRequests.Update(request);
            await _dbContext.SaveChangesAsync();

            // Notify client
            var client = await _clientService.GetClientUserAsync(request.ClientId);
            string notificationType = review.Status == RefundRequestStatus.Approved
                ? NotificationTypes.RefundApproved
                : NotificationTypes.RefundRejected;
            string notificationContent = review.Status == RefundRequestStatus.Approved
                ? $"Your refund request for ₦{request.Amount:N2} has been approved. The amount will be transferred to your bank account."
                : $"Your refund request for ₦{request.Amount:N2} has been rejected." +
                  (!string.IsNullOrEmpty(review.AdminNote) ? $" Reason: {review.AdminNote}" : "");

            await _notificationService.CreateNotificationAsync(
                request.ClientId, adminId,
                notificationType, notificationContent,
                review.Status == RefundRequestStatus.Approved ? "Refund Approved" : "Refund Rejected",
                requestId);

            // Send email
            if (client?.Email != null && review.Status == RefundRequestStatus.Approved)
            {
                try
                {
                    await _emailService.SendRefundNotificationEmailAsync(
                        client.Email, client.FirstName ?? "Client", request.Amount, request.Reason);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send refund approval email for request {RequestId}", requestId);
                }
            }

            _logger.LogInformation("Refund request {RequestId} reviewed by admin {AdminId}: {Status}",
                requestId, adminId, review.Status);

            return Result<RefundRequestResponse>.Success(MapToResponse(request, client));
        }

        public async Task<Result<RefundRequestResponse>> CompleteRefundRequestAsync(string requestId, string adminId)
        {
            if (!ObjectId.TryParse(requestId, out var objectId))
                return Result<RefundRequestResponse>.Failure(new List<string> { "Invalid refund request ID format." });

            var request = await _dbContext.RefundRequests.FindAsync(objectId);
            if (request == null)
                return Result<RefundRequestResponse>.Failure(new List<string> { "Refund request not found." });

            if (request.Status != RefundRequestStatus.Approved)
                return Result<RefundRequestResponse>.Failure(new List<string>
                {
                    $"Only approved requests can be marked as completed. Current status: {request.Status}"
                });

            request.Status = RefundRequestStatus.Completed;
            request.CompletedAt = DateTime.UtcNow;

            _dbContext.RefundRequests.Update(request);
            await _dbContext.SaveChangesAsync();

            // Notify client that bank transfer is complete
            await _notificationService.CreateNotificationAsync(
                request.ClientId, adminId,
                NotificationTypes.RefundProcessed,
                $"Your refund of ₦{request.Amount:N2} has been transferred to your bank account.",
                "Refund Completed",
                requestId);

            _logger.LogInformation("Refund request {RequestId} completed by admin {AdminId}. Bank transfer done.",
                requestId, adminId);

            var client = await _clientService.GetClientUserAsync(request.ClientId);
            return Result<RefundRequestResponse>.Success(MapToResponse(request, client));
        }

        private static RefundRequestResponse MapToResponse(RefundRequest request, ClientResponse? client)
        {
            return new RefundRequestResponse
            {
                Id = request.Id.ToString(),
                ClientId = request.ClientId,
                ClientName = client != null ? $"{client.FirstName} {client.LastName}" : null,
                ClientEmail = client?.Email,
                Amount = request.Amount,
                Reason = request.Reason,
                Status = request.Status,
                ReviewedByAdminId = request.ReviewedByAdminId,
                AdminNote = request.AdminNote,
                WalletBalanceAtRequest = request.WalletBalanceAtRequest,
                CreatedAt = request.CreatedAt,
                ReviewedAt = request.ReviewedAt,
                CompletedAt = request.CompletedAt
            };
        }
    }
}
