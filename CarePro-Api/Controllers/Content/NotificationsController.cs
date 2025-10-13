using Application.DTOs;
using Application.Interfaces.Content;
using Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;

namespace CarePro_Api.Controllers.Content
{
    [Route("api/[controller]")]
    [ApiController]
    //[Authorize]
    public class NotificationsController : ControllerBase
    {
        private readonly INotificationService _notificationService;
        private readonly ILogger<NotificationsController> _logger;

        public NotificationsController(
            INotificationService notificationService,
            ILogger<NotificationsController> logger)
        {
            _notificationService = notificationService;
            _logger = logger;
        }

        // GET: api/Notifications
        [HttpGet]
        public async Task<IActionResult> GetUserNotifications(string userId)
        {
            try
            {
               // var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                var notifications = await _notificationService.GetUserNotificationsAsync(userId);
                return Ok(notifications);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving notifications");
                return StatusCode(500, new { message = "Failed to retrieve notifications", error = ex.Message });
            }
        }

        // GET: api/Notifications/unread/count
        [HttpGet("unread/count")]
        public async Task<IActionResult> GetUnreadCount(string userId)
        {
            try
            {
               // var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                var count = await _notificationService.GetUnreadNotificationCountAsync(userId);
                return Ok(new { count });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving unread notification count");
                return StatusCode(500, new { message = "Failed to retrieve unread notification count", error = ex.Message });
            }
        }

        // PUT: api/Notifications/{id}/read
        [HttpPut("{id}/read")]
        public async Task<IActionResult> MarkAsRead(string id)
        {
            try
            {
                await _notificationService.MarkAsReadAsync(id);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking notification as read");
                return StatusCode(500, new { message = "Failed to mark notification as read", error = ex.Message });
            }
        }

        // PUT: api/Notifications/read-all
        [HttpPut("read-all")]
        public async Task<IActionResult> MarkAllAsRead(string userId)
        {
            try
            {
                //var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                await _notificationService.MarkAllAsReadAsync(userId);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking all notifications as read");
                return StatusCode(500, new { message = "Failed to mark all notifications as read", error = ex.Message });
            }
        }

        // DELETE: api/Notifications/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteNotification(string id)
        {
            try
            {
                await _notificationService.DeleteNotificationAsync(id);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting notification");
                return StatusCode(500, new { message = "Failed to delete notification", error = ex.Message });
            }
        }

        // POST: api/Notifications/test (for testing purposes)
        [HttpPost("test")]
        //[Authorize(Roles = "Admin")]
        public async Task<IActionResult> TestNotification([FromBody] TestNotificationRequest request)
        {
            try
            {
                var notification = await _notificationService.CreateNotificationAsync(
                    request.RecipientId,
                    User.FindFirstValue(ClaimTypes.NameIdentifier),
                    "System Notice",
                    request.Message,
                    "test_notification",
                    "");

                return Ok(new { message = "Notification sent", notification });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending test notification");
                return StatusCode(500, new { message = "Failed to send test notification", error = ex.Message });
            }
        }


        // POST: api/Notifications/
        [HttpPost]
       // [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CreateNotification([FromBody] AddNotificationRequest  addNotificationRequest)
        {
            try
            {
                var notification = await _notificationService.CreateNotificationAsync(
                    addNotificationRequest.RecipientId,
                    addNotificationRequest.SenderId,
                    addNotificationRequest.Type,
                    addNotificationRequest.Content,
                    addNotificationRequest.Title,
                    addNotificationRequest.RelatedEntityId);

                return Ok(new { message = "Notification sent", notification });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending notification");
                return StatusCode(500, new { message = "Failed to send notification", error = ex.Message });
            }
        }
    }

    public class TestNotificationRequest
    {
        public string RecipientId { get; set; }
        public string Message { get; set; }
    }
}
