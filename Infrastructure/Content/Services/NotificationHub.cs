using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace Infrastructure.Content.Services
{
    [Authorize]
    public class NotificationHub : Hub
    {
        private readonly ILogger<NotificationHub> _logger;

        public NotificationHub(ILogger<NotificationHub> logger)
        {
            _logger = logger;
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
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in NotificationHub OnConnectedAsync");
            }

            await base.OnConnectedAsync();
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
