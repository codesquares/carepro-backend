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
    [Authorize]
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

        /// <summary>
        /// Gets the current authenticated user's ID from JWT claims.
        /// </summary>
        private string GetCurrentUserId()
        {
            return User.FindFirstValue("userId")
                ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new UnauthorizedAccessException("User identity not found");
        }

        private bool IsAdmin()
        {
            return User.IsInRole("Admin") || User.IsInRole("SuperAdmin");
        }

        // GET: api/Notifications
        [HttpGet]
        public async Task<IActionResult> GetUserNotifications(string? userId)
        {
            try
            {
                var currentUserId = GetCurrentUserId();

                // IDOR: If userId supplied, must match current user (or admin)
                var targetUserId = userId ?? currentUserId;
                if (targetUserId != currentUserId && !IsAdmin())
                {
                    return Forbid();
                }

                var notifications = await _notificationService.GetUserNotificationsAsync(targetUserId);
                return Ok(notifications);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving notifications");
                return StatusCode(500, new { message = "Failed to retrieve notifications" });
            }
        }

        // GET: api/Notifications/unread/count
        [HttpGet("unread/count")]
        public async Task<IActionResult> GetUnreadCount(string? userId)
        {
            try
            {
                var currentUserId = GetCurrentUserId();

                // IDOR: If userId supplied, must match current user (or admin)
                var targetUserId = userId ?? currentUserId;
                if (targetUserId != currentUserId && !IsAdmin())
                {
                    return Forbid();
                }

                var count = await _notificationService.GetUnreadNotificationCountAsync(targetUserId);
                return Ok(new { count });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving unread notification count");
                return StatusCode(500, new { message = "Failed to retrieve unread notification count" });
            }
        }

        // PUT: api/Notifications/{id}/read
        [HttpPut("{id}/read")]
        public async Task<IActionResult> MarkAsRead(string id)
        {
            try
            {
                var currentUserId = GetCurrentUserId();

                // IDOR: Verify the notification belongs to the current user
                var notification = await _notificationService.GetNotificationByIdAsync(id);
                if (notification == null)
                {
                    return NotFound(new { message = "Notification not found" });
                }
                if (notification.UserId != currentUserId && !IsAdmin())
                {
                    return Forbid();
                }

                await _notificationService.MarkAsReadAsync(id);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking notification as read");
                return StatusCode(500, new { message = "Failed to mark notification as read" });
            }
        }

        // PUT: api/Notifications/read-all
        [HttpPut("read-all")]
        public async Task<IActionResult> MarkAllAsRead()
        {
            try
            {
                // SECURITY: Always use JWT identity — mark only own notifications as read
                var currentUserId = GetCurrentUserId();
                await _notificationService.MarkAllAsReadAsync(currentUserId);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking all notifications as read");
                return StatusCode(500, new { message = "Failed to mark all notifications as read" });
            }
        }

        // DELETE: api/Notifications/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteNotification(string id)
        {
            try
            {
                var currentUserId = GetCurrentUserId();

                // IDOR: Verify the notification belongs to the current user
                var notification = await _notificationService.GetNotificationByIdAsync(id);
                if (notification == null)
                {
                    return NotFound(new { message = "Notification not found" });
                }
                if (notification.UserId != currentUserId && !IsAdmin())
                {
                    return Forbid();
                }

                await _notificationService.DeleteNotificationAsync(id);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting notification");
                return StatusCode(500, new { message = "Failed to delete notification" });
            }
        }

        // POST: api/Notifications/test (for testing purposes — admin only)
        [HttpPost("test")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> TestNotification([FromBody] TestNotificationRequest request)
        {
            try
            {
                var notification = await _notificationService.CreateNotificationAsync(
                    request.RecipientId,
                    GetCurrentUserId(),
                    "System Notice",
                    request.Message,
                    "test_notification",
                    "");

                return Ok(new { message = "Notification sent", notificationId = notification });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending test notification");
                return StatusCode(500, new { message = "Failed to send test notification" });
            }
        }


        // POST: api/Notifications/ — Admin only
        [HttpPost]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> CreateNotification([FromBody] AddNotificationRequest addNotificationRequest)
        {
            try
            {
                var notification = await _notificationService.CreateNotificationAsync(
                    addNotificationRequest.RecipientId,
                    GetCurrentUserId(), // SECURITY: Override senderId with JWT identity
                    addNotificationRequest.Type,
                    addNotificationRequest.Content,
                    addNotificationRequest.Title,
                    addNotificationRequest.RelatedEntityId);

                return Ok(new { message = "Notification sent", notificationId = notification });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending notification");
                return StatusCode(500, new { message = "Failed to send notification" });
            }
        }
    }

    public class TestNotificationRequest
    {
        public string? RecipientId { get; set; }
        public string? Message { get; set; }
    }
}
