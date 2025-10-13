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
        Task<string> CreateNotificationAsync(string recipientId, string senderId, string type, string content, string Title, string relatedEntityId);
       // Task<string> CCreateNotificationAsync(AddNotificationRequest addNotificationRequest );


       // Task<List<Notification>> GetUserNotificationsAsync(string userId);
        Task<List<NotificationResponse>> GetUserNotificationsAsync(string userId );


        Task<int> GetUnreadNotificationCountAsync(string userId);




        Task MarkAsReadAsync(string notificationId);


        Task MarkAllAsReadAsync(string userId);

        Task DeleteNotificationAsync(string notificationId);

        Task<bool> SendRealTimeNotificationAsync(string userId, Notification notification);
    }
}
