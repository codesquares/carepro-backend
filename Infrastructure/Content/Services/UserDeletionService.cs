using Application.Commands;
using Application.DTOs;
using Application.Interfaces.Authentication;
using Application.Interfaces.Common;
using Application.Interfaces.Content;
using Application.Interfaces.Email;
using Domain.Entities;
using Infrastructure.Content.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using System.Text.Json;
using System.Web;

namespace Infrastructure.Content.Services
{
    public class UserDeletionService : IUserDeletionService
    {
        private const int GracePeriodDays = 30;

        private readonly CareProDbContext _db;
        private readonly IMediator _mediator;
        private readonly IEmailService _emailService;
        private readonly ILogger<UserDeletionService> _logger;
        private readonly ITokenHandler _tokenHandler;
        private readonly IOriginValidationService _originValidation;

        public UserDeletionService(
            CareProDbContext db,
            IMediator mediator,
            IEmailService emailService,
            ILogger<UserDeletionService> logger,
            ITokenHandler tokenHandler,
            IOriginValidationService originValidation)
        {
            _db = db;
            _mediator = mediator;
            _emailService = emailService;
            _logger = logger;
            _tokenHandler = tokenHandler;
            _originValidation = originValidation;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Self-deletion: Caregiver
        // ─────────────────────────────────────────────────────────────────────

        public async Task<AccountDeletionResult> RequestCaregiverAccountDeletionAsync(string caregiverId, string reason, string? origin = null)
        {
            if (!ObjectId.TryParse(caregiverId, out var objectId))
                throw new ArgumentException("Invalid Caregiver ID format.");

            var caregiver = await _db.CareGivers.FindAsync(objectId);
            if (caregiver == null || caregiver.IsDeleted)
                throw new KeyNotFoundException($"Caregiver '{caregiverId}' not found.");

            var blockers = await GetCaregiverBlockersAsync(caregiverId, bypassWalletCheck: false);
            if (blockers.Any())
                return new AccountDeletionResult { Success = false, Message = "Account deletion blocked.", Blockers = blockers };

            var permanentDeletionDate = await ScheduleCaregiverDeletionAsync(caregiver, reason);

            _logger.LogInformation("UserDeletionService: Caregiver {CaregiverId} requested account deletion. Permanent at {Date}", caregiverId, permanentDeletionDate);

            await NotifyAndEmailCaregiverDeletionScheduledAsync(caregiver, permanentDeletionDate, origin);

            return new AccountDeletionResult
            {
                Success = true,
                Message = $"Account deletion scheduled. Your data will be permanently deleted on {permanentDeletionDate:MMMM dd, yyyy}.",
                PermanentDeletionDate = permanentDeletionDate
            };
        }

        // ─────────────────────────────────────────────────────────────────────
        // Self-deletion: Client
        // ─────────────────────────────────────────────────────────────────────

        public async Task<AccountDeletionResult> RequestClientAccountDeletionAsync(string clientId, string reason, string? origin = null)
        {
            if (!ObjectId.TryParse(clientId, out var objectId))
                throw new ArgumentException("Invalid Client ID format.");

            var client = await _db.Clients.FindAsync(objectId);
            if (client == null || client.IsDeleted)
                throw new KeyNotFoundException($"Client '{clientId}' not found.");

            var blockers = await GetClientBlockersAsync(clientId);
            if (blockers.Any())
                return new AccountDeletionResult { Success = false, Message = "Account deletion blocked.", Blockers = blockers };

            var permanentDeletionDate = await ScheduleClientDeletionAsync(client, reason);

            _logger.LogInformation("UserDeletionService: Client {ClientId} requested account deletion. Permanent at {Date}", clientId, permanentDeletionDate);

            await NotifyAndEmailClientDeletionScheduledAsync(client, permanentDeletionDate, origin);

            return new AccountDeletionResult
            {
                Success = true,
                Message = $"Account deletion scheduled. Your data will be permanently deleted on {permanentDeletionDate:MMMM dd, yyyy}.",
                PermanentDeletionDate = permanentDeletionDate
            };
        }

        // ─────────────────────────────────────────────────────────────────────
        // Cancel deletion: Caregiver
        // ─────────────────────────────────────────────────────────────────────

        public async Task<string> CancelCaregiverAccountDeletionAsync(string caregiverId)
        {
            if (!ObjectId.TryParse(caregiverId, out var objectId))
                throw new ArgumentException("Invalid Caregiver ID format.");

            var caregiver = await _db.CareGivers
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.Id == objectId);

            if (caregiver == null)
                throw new KeyNotFoundException($"Caregiver '{caregiverId}' not found.");

            if (!caregiver.IsDeleted || caregiver.AccountDeletionRequestedAt == null)
                throw new InvalidOperationException("No pending account deletion found for this caregiver.");

            var cutoff = DateTime.UtcNow.AddDays(-GracePeriodDays);
            if (caregiver.DeletedOn.HasValue && caregiver.DeletedOn.Value <= cutoff)
                throw new InvalidOperationException("The 30-day grace period has expired. This account cannot be restored.");

            // Restore caregiver profile
            caregiver.IsDeleted = false;
            caregiver.DeletedOn = null;
            caregiver.AccountDeletionRequestedAt = null;
            _db.CareGivers.Update(caregiver);

            // Restore AppUser
            var appUser = await _db.AppUsers
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.AppUserId == objectId);
            if (appUser != null)
            {
                appUser.IsDeleted = false;
                _db.AppUsers.Update(appUser);
            }

            // Restore soft-deleted gigs that were deleted at the same time (within 5 minutes — same session)
            var deletionWindow = caregiver.AccountDeletionRequestedAt ?? DateTime.UtcNow;
            var windowStart = deletionWindow.AddMinutes(-5);
            var windowEnd = deletionWindow.AddMinutes(5);

            var caregiverGigs = await _db.Gigs
                .IgnoreQueryFilters()
                .Where(g => g.CaregiverId == caregiverId
                    && g.IsDeleted == true
                    && g.DeletedOn >= windowStart
                    && g.DeletedOn <= windowEnd)
                .ToListAsync();

            foreach (var gig in caregiverGigs)
            {
                gig.IsDeleted = false;
                gig.DeletedOn = null;
                _db.Gigs.Update(gig);
            }

            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "UserDeletionService: Caregiver {CaregiverId} account deletion cancelled. {GigCount} gig(s) restored.",
                caregiverId, caregiverGigs.Count);

            // Send notification and email
            await _mediator.Send(new SendNotificationCommand(
                caregiverId, "system",
                NotificationTypes.AccountDeletionCancelled,
                "Your account deletion request has been cancelled. Your account is now fully restored.",
                "Account Deletion Cancelled",
                caregiverId));

            try
            {
                await _emailService.SendAccountDeletionCancelledEmailAsync(caregiver.Email, caregiver.FirstName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "UserDeletionService: Failed to send cancellation email to caregiver {CaregiverId}", caregiverId);
            }

            return "Account deletion cancelled successfully. Your account has been restored.";
        }

        // ─────────────────────────────────────────────────────────────────────
        // Cancel deletion: Client
        // ─────────────────────────────────────────────────────────────────────

        public async Task<string> CancelClientAccountDeletionAsync(string clientId)
        {
            if (!ObjectId.TryParse(clientId, out var objectId))
                throw new ArgumentException("Invalid Client ID format.");

            var client = await _db.Clients
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.Id == objectId);

            if (client == null)
                throw new KeyNotFoundException($"Client '{clientId}' not found.");

            if (!client.IsDeleted || client.AccountDeletionRequestedAt == null)
                throw new InvalidOperationException("No pending account deletion found for this client.");

            var cutoff = DateTime.UtcNow.AddDays(-GracePeriodDays);
            if (client.DeletedOn.HasValue && client.DeletedOn.Value <= cutoff)
                throw new InvalidOperationException("The 30-day grace period has expired. This account cannot be restored.");

            // Restore client profile
            client.IsDeleted = false;
            client.DeletedOn = null;
            client.AccountDeletionRequestedAt = null;
            _db.Clients.Update(client);

            // Restore AppUser
            var appUser = await _db.AppUsers
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.AppUserId == objectId);
            if (appUser != null)
            {
                appUser.IsDeleted = false;
                _db.AppUsers.Update(appUser);
            }

            await _db.SaveChangesAsync();

            _logger.LogInformation("UserDeletionService: Client {ClientId} account deletion cancelled.", clientId);

            await _mediator.Send(new SendNotificationCommand(
                clientId, "system",
                NotificationTypes.AccountDeletionCancelled,
                "Your account deletion request has been cancelled. Your account is now fully restored.",
                "Account Deletion Cancelled",
                clientId));

            try
            {
                await _emailService.SendAccountDeletionCancelledEmailAsync(client.Email, client.FirstName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "UserDeletionService: Failed to send cancellation email to client {ClientId}", clientId);
            }

            return "Account deletion cancelled successfully. Your account has been restored.";
        }

        // ─────────────────────────────────────────────────────────────────────
        // Admin-initiated: Caregiver
        // ─────────────────────────────────────────────────────────────────────

        public async Task<AccountDeletionResult> AdminDeleteCaregiverAccountAsync(
            string caregiverId, string adminId, string adminEmail, string reason)
        {
            if (!ObjectId.TryParse(caregiverId, out var objectId))
                throw new ArgumentException("Invalid Caregiver ID format.");

            var caregiver = await _db.CareGivers.FindAsync(objectId);
            if (caregiver == null || caregiver.IsDeleted)
                throw new KeyNotFoundException($"Caregiver '{caregiverId}' not found.");

            // Admin bypasses wallet balance check but still blocks on active orders
            var blockers = await GetCaregiverBlockersAsync(caregiverId, bypassWalletCheck: true);
            if (blockers.Any())
                return new AccountDeletionResult { Success = false, Message = "Account deletion blocked.", Blockers = blockers };

            var permanentDeletionDate = await ScheduleCaregiverDeletionAsync(caregiver, reason);

            // Write AdminAuditLog
            await WriteAuditLogAsync(adminId, adminEmail, "Caregiver", caregiverId, caregiverId,
                "AdminAccountDeletion", reason, caregiver.Email);

            _logger.LogWarning(
                "UserDeletionService: Admin {AdminId} scheduled caregiver {CaregiverId} for deletion. Permanent at {Date}",
                adminId, caregiverId, permanentDeletionDate);

            await NotifyAndEmailCaregiverDeletionScheduledAsync(caregiver, permanentDeletionDate);

            return new AccountDeletionResult
            {
                Success = true,
                Message = $"Caregiver account deletion scheduled. Permanent deletion on {permanentDeletionDate:MMMM dd, yyyy}.",
                PermanentDeletionDate = permanentDeletionDate
            };
        }

        // ─────────────────────────────────────────────────────────────────────
        // Admin-initiated: Client
        // ─────────────────────────────────────────────────────────────────────

        public async Task<AccountDeletionResult> AdminDeleteClientAccountAsync(
            string clientId, string adminId, string adminEmail, string reason)
        {
            if (!ObjectId.TryParse(clientId, out var objectId))
                throw new ArgumentException("Invalid Client ID format.");

            var client = await _db.Clients.FindAsync(objectId);
            if (client == null || client.IsDeleted)
                throw new KeyNotFoundException($"Client '{clientId}' not found.");

            var blockers = await GetClientBlockersAsync(clientId);
            if (blockers.Any())
                return new AccountDeletionResult { Success = false, Message = "Account deletion blocked.", Blockers = blockers };

            var permanentDeletionDate = await ScheduleClientDeletionAsync(client, reason);

            await WriteAuditLogAsync(adminId, adminEmail, "Client", clientId, clientId,
                "AdminAccountDeletion", reason, client.Email);

            _logger.LogWarning(
                "UserDeletionService: Admin {AdminId} scheduled client {ClientId} for deletion. Permanent at {Date}",
                adminId, clientId, permanentDeletionDate);

            await NotifyAndEmailClientDeletionScheduledAsync(client, permanentDeletionDate);

            return new AccountDeletionResult
            {
                Success = true,
                Message = $"Client account deletion scheduled. Permanent deletion on {permanentDeletionDate:MMMM dd, yyyy}.",
                PermanentDeletionDate = permanentDeletionDate
            };
        }

        // ─────────────────────────────────────────────────────────────────────
        // Private helpers
        // ─────────────────────────────────────────────────────────────────────

        private async Task<List<string>> GetCaregiverBlockersAsync(string caregiverId, bool bypassWalletCheck)
        {
            var blockers = new List<string>();

            // Block if any active (non-terminal) orders exist across all their gigs
            var caregiverGigIds = await _db.Gigs
                .Where(g => g.CaregiverId == caregiverId && g.IsDeleted != true)
                .Select(g => g.Id.ToString())
                .ToListAsync();

            if (caregiverGigIds.Any())
            {
                var hasActiveOrders = await _db.ClientOrders.AnyAsync(o =>
                    caregiverGigIds.Contains(o.GigId)
                    && o.ClientOrderStatus != null
                    && o.ClientOrderStatus != "Completed"
                    && o.ClientOrderStatus != "Cancelled");

                if (hasActiveOrders)
                    blockers.Add("You have active orders in progress. Please complete or wait for all orders to close before deleting your account.");
            }

            // Block if pending withdrawal requests exist
            var hasPendingWithdrawals = await _db.WithdrawalRequests.AnyAsync(w =>
                w.CaregiverId == caregiverId
                && (w.Status == WithdrawalStatus.Pending || w.Status == WithdrawalStatus.Verified));

            if (hasPendingWithdrawals)
                blockers.Add("You have pending withdrawal requests. Please wait for them to complete before deleting your account.");

            if (!bypassWalletCheck)
            {
                // Block if the caregiver has an unsettled wallet balance
                var wallet = await _db.CaregiverWallets.FirstOrDefaultAsync(w => w.CaregiverId == caregiverId);
                if (wallet != null && (wallet.WithdrawableBalance > 0 || wallet.PendingBalance > 0))
                    blockers.Add($"You have an outstanding wallet balance (₦{wallet.WithdrawableBalance + wallet.PendingBalance:N2}). Please withdraw your funds before deleting your account.");
            }

            return blockers;
        }

        private async Task<List<string>> GetClientBlockersAsync(string clientId)
        {
            var blockers = new List<string>();

            var hasActiveOrders = await _db.ClientOrders.AnyAsync(o =>
                o.ClientId == clientId
                && o.ClientOrderStatus != null
                && o.ClientOrderStatus != "Completed"
                && o.ClientOrderStatus != "Cancelled");

            if (hasActiveOrders)
                blockers.Add("You have active orders in progress. Please complete or wait for all orders to close before deleting your account.");

            return blockers;
        }

        private async Task<DateTime> ScheduleCaregiverDeletionAsync(Caregiver caregiver, string reason)
        {
            var now = DateTime.UtcNow;
            var caregiverId = caregiver.Id.ToString();

            caregiver.IsDeleted = true;
            caregiver.DeletedOn = now;
            caregiver.AccountDeletionRequestedAt = now;
            _db.CareGivers.Update(caregiver);

            // Soft-delete AppUser
            var appUser = await _db.AppUsers.FirstOrDefaultAsync(u => u.AppUserId == caregiver.Id);
            if (appUser != null)
            {
                appUser.IsDeleted = true;
                _db.AppUsers.Update(appUser);
            }

            // Soft-delete all active gigs — feeds into GigHardDeleteProcessor
            var activeGigs = await _db.Gigs
                .Where(g => g.CaregiverId == caregiverId && g.IsDeleted != true)
                .ToListAsync();

            foreach (var gig in activeGigs)
            {
                gig.IsDeleted = true;
                gig.DeletedOn = now;
                _db.Gigs.Update(gig);
            }

            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "UserDeletionService: Caregiver {CaregiverId} soft-deleted with {GigCount} gig(s).",
                caregiverId, activeGigs.Count);

            return now.AddDays(GracePeriodDays);
        }

        private async Task<DateTime> ScheduleClientDeletionAsync(Client client, string reason)
        {
            var now = DateTime.UtcNow;

            client.IsDeleted = true;
            client.DeletedOn = now;
            client.AccountDeletionRequestedAt = now;
            _db.Clients.Update(client);

            var appUser = await _db.AppUsers.FirstOrDefaultAsync(u => u.AppUserId == client.Id);
            if (appUser != null)
            {
                appUser.IsDeleted = true;
                _db.AppUsers.Update(appUser);
            }

            await _db.SaveChangesAsync();

            return now.AddDays(GracePeriodDays);
        }

        private async Task NotifyAndEmailCaregiverDeletionScheduledAsync(Caregiver caregiver, DateTime permanentDeletionDate, string? origin = null)
        {
            var caregiverId = caregiver.Id.ToString();

            await _mediator.Send(new SendNotificationCommand(
                caregiverId, "system",
                NotificationTypes.AccountDeletionScheduled,
                $"Your account deletion has been scheduled. Your data will be permanently deleted on {permanentDeletionDate:MMMM dd, yyyy}. You have 30 days to cancel this request.",
                "Account Deletion Scheduled",
                caregiverId));

            try
            {
                var cancellationLink = BuildCancellationLink(caregiverId, "caregiver", origin);
                await _emailService.SendAccountDeletionScheduledEmailAsync(caregiver.Email, caregiver.FirstName, permanentDeletionDate, cancellationLink);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "UserDeletionService: Failed to send deletion email to caregiver {CaregiverId}", caregiverId);
            }
        }

        private async Task NotifyAndEmailClientDeletionScheduledAsync(Client client, DateTime permanentDeletionDate, string? origin = null)
        {
            var clientId = client.Id.ToString();

            await _mediator.Send(new SendNotificationCommand(
                clientId, "system",
                NotificationTypes.AccountDeletionScheduled,
                $"Your account deletion has been scheduled. Your data will be permanently deleted on {permanentDeletionDate:MMMM dd, yyyy}. You have 30 days to cancel this request.",
                "Account Deletion Scheduled",
                clientId));

            try
            {
                var cancellationLink = BuildCancellationLink(clientId, "client", origin);
                await _emailService.SendAccountDeletionScheduledEmailAsync(client.Email, client.FirstName, permanentDeletionDate, cancellationLink);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "UserDeletionService: Failed to send deletion email to client {ClientId}", clientId);
            }
        }

        /// <summary>
        /// Generates a signed 30-day cancellation token and builds the deep link.
        /// If origin is a known frontend origin the link goes to a frontend route;
        /// otherwise it points directly at the backend endpoint.
        /// </summary>
        private string? BuildCancellationLink(string userId, string role, string? origin)
        {
            try
            {
                var token = _tokenHandler.GenerateCancellationToken(userId);
                var encodedToken = HttpUtility.UrlEncode(token);

                if (!string.IsNullOrWhiteSpace(origin) && _originValidation.IsFrontendOrigin(origin))
                    return $"{origin}/cancel-account-deletion?token={encodedToken}&role={role}";

                // Fallback: backend direct endpoint
                var endpoint = role == "caregiver"
                    ? "CareGivers/cancel-account-deletion-by-token"
                    : "Clients/cancel-account-deletion-by-token";
                return $"{origin?.TrimEnd('/')}/api/{endpoint}?token={encodedToken}";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "UserDeletionService: Could not generate cancellation link for user {UserId}", userId);
                return null;
            }
        }

        private async Task WriteAuditLogAsync(
            string adminId, string adminEmail,
            string entityType, string entityId, string targetUserId,
            string action, string reason, string? beforeSnapshot)
        {
            var log = new AdminAuditLog
            {
                Id = ObjectId.GenerateNewId(),
                AdminId = adminId,
                AdminEmail = adminEmail,
                TargetEntityType = entityType,
                TargetEntityId = entityId,
                TargetUserId = targetUserId,
                Action = action,
                BeforeJson = JsonSerializer.Serialize(new { Email = beforeSnapshot }),
                Reason = reason,
                Timestamp = DateTime.UtcNow
            };

            await _db.AdminAuditLogs.AddAsync(log);
            await _db.SaveChangesAsync();
        }
    }
}
