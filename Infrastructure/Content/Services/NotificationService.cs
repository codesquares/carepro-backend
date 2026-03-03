using Application.DTOs;
using Application.Interfaces.Content;
using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Content.Services
{
    public class NotificationService : INotificationService
    {
        private readonly CareProDbContext _dbContext;
        private readonly IHubContext<NotificationHub> _notificationHubContext;
        private readonly ILogger<NotificationService> _logger;

        public NotificationService(
            CareProDbContext dbContext,
            IHubContext<NotificationHub> notificationHubContext,
            ILogger<NotificationService> logger)
        {
            _dbContext = dbContext;
            _notificationHubContext = notificationHubContext;
            _logger = logger;
        }

        public async Task<string> CreateNotificationAsync(string recipientId, string senderId, string type, string content, string? Title, string relatedEntityId, string? orderId = null)
        {
            try
            {
                var notification = new Notification
                {
                    Id = ObjectId.GenerateNewId(),
                    RecipientId = recipientId,
                    SenderId = senderId,
                    Type = type,
                    Content = content,
                    Title = Title,
                    CreatedAt = DateTime.UtcNow,
                    IsRead = false,
                    RelatedEntityId = relatedEntityId,
                    OrderId = orderId
                };

                await _dbContext.Notifications.AddAsync(notification);
                await _dbContext.SaveChangesAsync();

                // Send real-time notification
                await SendRealTimeNotificationAsync(recipientId, notification);

                return notification.Id.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating notification");
                throw;
            }
        }



        public async Task<PaginatedResponse<NotificationResponse>> GetUserNotificationsAsync(string userId, int page = 1, int pageSize = 50)
        {
            try
            {
                // Clamp inputs
                if (page < 1) page = 1;
                if (pageSize < 1) pageSize = 10;
                if (pageSize > 200) pageSize = 200;

                var query = _dbContext.Notifications
                    .Where(n => n.RecipientId == userId);

                var totalCount = await query.CountAsync();

                var notifications = await query
                    .OrderByDescending(n => n.CreatedAt)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                // Batch-load sender names for all notifications that have a SenderId
                var senderIds = notifications
                    .Where(n => !string.IsNullOrEmpty(n.SenderId))
                    .Select(n => n.SenderId!)
                    .Distinct()
                    .ToList();

                var senderNames = new Dictionary<string, string>();
                if (senderIds.Any())
                {
                    var senders = await _dbContext.AppUsers
                        .Where(u => senderIds.Contains(u.AppUserId.ToString()) || senderIds.Contains(u.Id.ToString()))
                        .ToListAsync();

                    foreach (var sender in senders)
                    {
                        var key = sender.AppUserId.ToString();
                        var name = $"{sender.FirstName} {sender.LastName}".Trim();
                        if (!string.IsNullOrEmpty(name))
                            senderNames[key] = name;
                        // Also index by Id in case SenderId references that
                        senderNames[sender.Id.ToString()] = name;
                    }
                }

                var items = notifications.Select(notification => new NotificationResponse
                {
                    Id = notification.Id.ToString(),
                    UserId = notification.RecipientId,
                    UserFullName = notification.SenderId != null && senderNames.TryGetValue(notification.SenderId, out var fullName)
                        ? fullName
                        : null,
                    SenderId = notification.SenderId,
                    Type = notification.Type,
                    Content = notification.Content,
                    Title = notification.Title,
                    IsRead = notification.IsRead,
                    RelatedEntityId = notification.RelatedEntityId,
                    OrderId = notification.OrderId,
                    CreatedAt = notification.CreatedAt,
                }).ToList();

                return new PaginatedResponse<NotificationResponse>
                {
                    Items = items,
                    TotalCount = totalCount,
                    Page = page,
                    PageSize = pageSize,
                    HasMore = (page * pageSize) < totalCount
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving notifications for user {UserId}", userId);
                throw;
            }
        }



        public async Task<int> GetUnreadNotificationCountAsync(string userId)
        {
            try
            {
                return await _dbContext.Notifications
                    .CountAsync(n => n.RecipientId == userId && n.IsRead == false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving unread notification count for user {UserId}", userId);
                throw;
            }
        }


        public async Task MarkAsReadAsync(string notificationId)
        {
            try
            {
                if (!ObjectId.TryParse(notificationId, out var objectId))
                {
                    throw new ArgumentException("Invalid Notification ID format.");
                }

                var existingNotification = await _dbContext.Notifications.FindAsync(objectId);
                if (existingNotification == null)
                {
                    throw new KeyNotFoundException($"Notification with ID '{notificationId}' not found.");
                }

                existingNotification.IsRead = true;

                _dbContext.Notifications.Update(existingNotification);
                await _dbContext.SaveChangesAsync();


            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking notification {NotificationId} as read", notificationId);
                throw;
            }
        }

        public async Task MarkAllAsReadAsync(string userId)
        {
            try
            {
                var notifications = await _dbContext.Notifications
                    .Where(n => n.RecipientId == userId && !n.IsRead)
                    .ToListAsync();

                foreach (var notification in notifications)
                {
                    notification.IsRead = true;
                }

                await _dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking all notifications as read for user {UserId}", userId);
                throw;
            }
        }

        public async Task DeleteNotificationAsync(string notificationId)
        {
            try
            {
                var notification = await _dbContext.Notifications.FindAsync(ObjectId.Parse(notificationId));
                if (notification != null)
                {
                    _dbContext.Notifications.Remove(notification);
                    await _dbContext.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting notification {notificationId}", notificationId);
                throw;
            }
        }

        public async Task<NotificationResponse?> GetNotificationByIdAsync(string notificationId)
        {
            try
            {
                if (!ObjectId.TryParse(notificationId, out var objectId))
                    return null;

                var notification = await _dbContext.Notifications.FindAsync(objectId);
                if (notification == null)
                    return null;

                // Look up sender name
                string? senderFullName = null;
                if (!string.IsNullOrEmpty(notification.SenderId))
                {
                    var sender = await _dbContext.AppUsers
                        .FirstOrDefaultAsync(u => u.AppUserId.ToString() == notification.SenderId
                                               || u.Id.ToString() == notification.SenderId);
                    if (sender != null)
                        senderFullName = $"{sender.FirstName} {sender.LastName}".Trim();
                }

                return new NotificationResponse
                {
                    Id = notification.Id.ToString(),
                    UserId = notification.RecipientId,
                    UserFullName = senderFullName,
                    SenderId = notification.SenderId,
                    Type = notification.Type,
                    Content = notification.Content,
                    Title = notification.Title,
                    IsRead = notification.IsRead,
                    RelatedEntityId = notification.RelatedEntityId,
                    OrderId = notification.OrderId,
                    CreatedAt = notification.CreatedAt,
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving notification {NotificationId}", notificationId);
                throw;
            }
        }


        public async Task<bool> SendRealTimeNotificationAsync(string userId, Notification notification)
        {
            try
            {
                // Send to all connections in the user's notification group.
                // NotificationHub adds each connection to group "notifications_{userId}" on connect.
                // This supports multiple devices / tabs for the same user.
                await _notificationHubContext.Clients
                    .Group($"notifications_{userId}")
                    .SendAsync("ReceiveNotification", new
                    {
                        id = notification.Id.ToString(),
                        userId = notification.RecipientId,
                        senderId = notification.SenderId,
                        type = notification.Type,
                        content = notification.Content,
                        title = notification.Title,
                        isRead = notification.IsRead,
                        relatedEntityId = notification.RelatedEntityId,
                        orderId = notification.OrderId,
                        createdAt = notification.CreatedAt
                    });

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending real-time notification to user {UserId}", userId);
                return false;
            }
        }


    }
}
