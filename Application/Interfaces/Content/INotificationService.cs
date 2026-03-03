using Application.DTOs;
using Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    public interface INotificationService
    {
        Task<string> CreateNotificationAsync(string recipientId, string senderId, string type, string content, string? Title, string relatedEntityId, string? orderId = null);

        /// <summary>
        /// Returns paginated notifications for a user. Defaults to page 1, 50 items.
        /// </summary>
        Task<PaginatedResponse<NotificationResponse>> GetUserNotificationsAsync(string userId, int page = 1, int pageSize = 50);


        Task<int> GetUnreadNotificationCountAsync(string userId);




        Task MarkAsReadAsync(string notificationId);


        Task MarkAllAsReadAsync(string userId);

        Task DeleteNotificationAsync(string notificationId);

        Task<bool> SendRealTimeNotificationAsync(string userId, Notification notification);

        Task<NotificationResponse?> GetNotificationByIdAsync(string notificationId);
    }
}
