using Infrastructure.Content.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace Infrastructure.Content.Services
{
    [Authorize]
    public class NotificationHub : Hub
    {
        private readonly ILogger<NotificationHub> _logger;
        private readonly IServiceScopeFactory _scopeFactory;

        public NotificationHub(ILogger<NotificationHub> logger, IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        /// <summary>
        /// Gets the current authenticated user's ID from JWT claims.
        /// </summary>
        private string GetCurrentUserId()
        {
            return Context.User?.FindFirst("userId")?.Value
                ?? throw new HubException("Authentication required");
        }

        public override async Task OnConnectedAsync()
        {
            try
            {
                var userId = Context.User?.FindFirst("userId")?.Value;
                if (!string.IsNullOrEmpty(userId))
                {
                    // Add user to their own group so server-side code can push notifications
                    await Groups.AddToGroupAsync(Context.ConnectionId, $"notifications_{userId}");
                    _logger.LogInformation("User {UserId} connected to notification hub", userId);

                    // Replay unread notifications from the last 30 minutes that may have been
                    // dispatched while the client was disconnected. Sends only to this connection
                    // (Clients.Caller) so other open tabs are not spammed with duplicates.
                    await ReplayMissedNotificationsAsync(userId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in NotificationHub OnConnectedAsync");
            }

            await base.OnConnectedAsync();
        }

        private async Task ReplayMissedNotificationsAsync(string userId)
        {
            try
            {
                var cutoff = DateTime.UtcNow.AddMinutes(-30);

                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<CareProDbContext>();

                var missed = await db.Notifications
                    .Where(n => n.RecipientId == userId && !n.IsRead && n.CreatedAt >= cutoff)
                    .OrderBy(n => n.CreatedAt)
                    .ToListAsync();

                foreach (var n in missed)
                {
                    await Clients.Caller.SendAsync("ReceiveNotification", new
                    {
                        id = n.Id.ToString(),
                        userId = n.RecipientId,
                        senderId = n.SenderId,
                        type = n.Type,
                        content = n.Content,
                        title = n.Title,
                        isRead = n.IsRead,
                        relatedEntityId = n.RelatedEntityId,
                        orderId = n.OrderId,
                        createdAt = n.CreatedAt
                    });
                }

                if (missed.Count > 0)
                {
                    _logger.LogInformation(
                        "Replayed {Count} missed notification(s) to reconnecting user {UserId}",
                        missed.Count, userId);

                    // Push updated unread count so badge reflects reality
                    var totalUnread = await db.Notifications
                        .CountAsync(n => n.RecipientId == userId && !n.IsRead);
                    await Clients.Caller.SendAsync("UnreadCountChanged", totalUnread);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error replaying missed notifications for user {UserId}", userId);
            }
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            try
            {
                var userId = Context.User?.FindFirst("userId")?.Value;
                if (!string.IsNullOrEmpty(userId))
                {
                    await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"notifications_{userId}");
                    _logger.LogInformation("User {UserId} disconnected from notification hub", userId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in NotificationHub OnDisconnectedAsync");
            }

            await base.OnDisconnectedAsync(exception);
        }

        /// <summary>
        /// Client-callable method removed. Notifications should only be pushed
        /// from the server side via NotificationService.SendRealTimeNotificationAsync.
        /// This stub exists only for backward compatibility — it does nothing.
        /// </summary>
        public Task SendNotification(string userId, string message)
        {
            // SECURITY: Clients cannot push notifications to arbitrary users.
            // All real-time notifications are sent server-side only.
            _logger.LogWarning("Client {UserId} attempted to call SendNotification directly — blocked", GetCurrentUserId());
            return Task.CompletedTask;
        }
    }
}
