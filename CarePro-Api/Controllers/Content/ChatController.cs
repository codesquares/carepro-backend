using Domain.Entities;
using Infrastructure.Content.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using System;
using Microsoft.AspNetCore.Authorization;
using Application.Interfaces.Common;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace CarePro_Api.Controllers.Content
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class ChatController : ControllerBase
    {
        private readonly ChatRepository _chatRepository;
        private readonly IContentSanitizer _contentSanitizer;

        public ChatController(ChatRepository chatRepository, IContentSanitizer contentSanitizer)
        {
            _chatRepository = chatRepository;
            _contentSanitizer = contentSanitizer;
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

        /// <summary>
        /// Checks if the current user is an admin.
        /// </summary>
        private bool IsAdmin()
        {
            return User.IsInRole("Admin") || User.IsInRole("SuperAdmin");
        }

        [HttpGet("history")]
        public async Task<IActionResult> GetChatHistory(string user1, string user2, int skip = 0, int take = 50)
        {
            var currentUserId = GetCurrentUserId();

            // IDOR: User must be one of the participants in the conversation
            if (currentUserId != user1 && currentUserId != user2 && !IsAdmin())
            {
                return Forbid();
            }

            // Cap take to prevent bulk data extraction
            take = Math.Min(take, 100);

            var messages = await _chatRepository.GetChatHistoryAsync(user1, user2, skip, take);
            return Ok(messages);
        }

        [HttpGet("ChatPreview")]
        public async Task<IActionResult> GetChatUsersHistory(string userId)
        {
            var currentUserId = GetCurrentUserId();

            // IDOR: Can only view own chat previews (or admin)
            if (currentUserId != userId && !IsAdmin())
            {
                return Forbid();
            }

            var messages = await _chatRepository.GetChatUserPreviewAsync(userId);
            return Ok(messages);
        }

        [HttpPost("send")]
        public async Task<IActionResult> SendMessage([FromBody] SendMessageRequest request)
        {
            try
            {
                var currentUserId = GetCurrentUserId();

                // Validate request
                if (string.IsNullOrEmpty(request.ReceiverId) || string.IsNullOrEmpty(request.Message))
                {
                    return BadRequest(new { error = "ReceiverId and Message are required" });
                }

                // SECURITY: Override SenderId with authenticated user — prevent spoofing
                request.SenderId = currentUserId;

                // Prevent sending messages to yourself
                if (request.SenderId == request.ReceiverId)
                {
                    return BadRequest(new { error = "Cannot send messages to yourself" });
                }

                // Sanitize message content to prevent XSS attacks
                var sanitizedMessage = _contentSanitizer.SanitizeText(request.Message);

                // Enforce message length limit
                if (sanitizedMessage.Length > 5000)
                {
                    return BadRequest(new { error = "Message exceeds maximum length of 5000 characters" });
                }

                // Create a new chat message
                var chatMessage = new ChatMessage
                {
                    MessageId = ObjectId.GenerateNewId(),
                    SenderId = currentUserId,
                    ReceiverId = request.ReceiverId,
                    Message = sanitizedMessage,
                    // SECURITY: Server sets timestamp — ignore client-supplied value
                    Timestamp = DateTime.UtcNow
                };

                // Save the message
                await _chatRepository.SaveMessageAsync(chatMessage);

                // Return the message ID for the frontend
                return Ok(new { messageId = chatMessage.MessageId });
            }
            catch (Exception)
            {
                return StatusCode(500, new { error = "Failed to send message" });
            }
        }

        [HttpDelete("delete/{messageId}")]
        public async Task<IActionResult> DeleteMessage(string messageId)
        {
            try
            {
                var currentUserId = GetCurrentUserId();

                if (string.IsNullOrEmpty(messageId))
                {
                    return BadRequest(new { error = "MessageId is required" });
                }

                // Get the message first to verify ownership
                var message = await _chatRepository.GetMessageByIdAsync(messageId);

                if (message == null)
                {
                    return NotFound(new { error = "Message not found" });
                }

                // IDOR: Must be the sender to delete (using JWT identity, not client-supplied userId)
                if (message.SenderId != currentUserId && !IsAdmin())
                {
                    return Forbid();
                }

                bool success = await _chatRepository.DeleteMessageAsync(messageId);

                if (!success)
                {
                    return StatusCode(500, new { error = "Failed to delete message" });
                }

                return Ok(new { success = true, messageId });
            }
            catch (Exception)
            {
                return StatusCode(500, new { error = "Failed to delete message" });
            }
        }

        [HttpPost("mark-read/{messageId}")]
        public async Task<IActionResult> MarkMessageAsRead(string messageId)
        {
            try
            {
                var currentUserId = GetCurrentUserId();

                if (string.IsNullOrEmpty(messageId))
                {
                    return BadRequest(new { error = "MessageId is required" });
                }

                var message = await _chatRepository.GetMessageByIdAsync(messageId);

                if (message == null)
                {
                    return NotFound(new { error = "Message not found" });
                }

                // IDOR: Must be the recipient to mark as read (JWT identity)
                if (message.ReceiverId != currentUserId)
                {
                    return Forbid();
                }

                bool success = await _chatRepository.MarkMessageAsReadAsync(messageId, currentUserId);

                if (!success)
                {
                    return StatusCode(500, new { error = "Failed to mark message as read" });
                }

                return Ok(new { success = true, messageId });
            }
            catch (Exception)
            {
                return StatusCode(500, new { error = "Failed to mark message as read" });
            }
        }

        [HttpPost("mark-all-read")]
        public async Task<IActionResult> MarkAllMessagesAsRead([FromBody] MarkAllReadRequest request)
        {
            try
            {
                var currentUserId = GetCurrentUserId();

                if (string.IsNullOrEmpty(request.SenderId))
                {
                    return BadRequest(new { error = "SenderId is required" });
                }

                // SECURITY: Override ReceiverId with authenticated user — only mark own messages as read
                bool success = await _chatRepository.MarkAllMessagesAsReadAsync(currentUserId, request.SenderId);

                if (!success)
                {
                    return StatusCode(500, new { error = "Failed to mark all messages as read" });
                }

                return Ok(new { success = true });
            }
            catch (Exception)
            {
                return StatusCode(500, new { error = "Failed to mark all messages as read" });
            }
        }

        [HttpGet("unread-count")]
        public async Task<IActionResult> GetUnreadMessageCount()
        {
            try
            {
                // SECURITY: Always use JWT identity — no client-supplied userId
                var currentUserId = GetCurrentUserId();

                int count = await _chatRepository.GetUnreadMessageCountAsync(currentUserId);

                return Ok(new { count });
            }
            catch (Exception)
            {
                return StatusCode(500, new { error = "Failed to get unread message count" });
            }
        }

        [HttpGet("conversations/{userId}")]
        public async Task<IActionResult> GetUserConversations(string userId)
        {
            try
            {
                var currentUserId = GetCurrentUserId();

                // IDOR: Can only view own conversations (or admin)
                if (currentUserId != userId && !IsAdmin())
                {
                    return Forbid();
                }

                var conversations = await _chatRepository.GetAllUserConversationsAsync(userId);

                return Ok(conversations);
            }
            catch (Exception)
            {
                return StatusCode(500, new { error = "Failed to get user conversations" });
            }
        }

        [HttpPost("mark-delivered/{messageId}")]
        public async Task<IActionResult> MarkMessageAsDelivered(string messageId)
        {
            try
            {
                var currentUserId = GetCurrentUserId();

                if (string.IsNullOrEmpty(messageId))
                {
                    return BadRequest(new { error = "MessageId is required" });
                }

                var message = await _chatRepository.GetMessageByIdAsync(messageId);

                if (message == null)
                {
                    return NotFound(new { error = "Message not found" });
                }

                // IDOR: Must be the recipient to mark as delivered (JWT identity)
                if (message.ReceiverId != currentUserId)
                {
                    return Forbid();
                }

                bool success = await _chatRepository.MarkMessageAsDeliveredAsync(messageId, currentUserId);

                if (!success)
                {
                    return StatusCode(500, new { error = "Failed to mark message as delivered" });
                }

                return Ok(new { success = true, messageId });
            }
            catch (Exception)
            {
                return StatusCode(500, new { error = "Failed to mark message as delivered" });
            }
        }
    }

    public class SendMessageRequest
    {
        public string? SenderId { get; set; }

        [Required(ErrorMessage = "ReceiverId is required")]
        public string? ReceiverId { get; set; }

        [Required(ErrorMessage = "Message is required")]
        [MaxLength(5000, ErrorMessage = "Message cannot exceed 5000 characters")]
        public string? Message { get; set; }

        // Timestamp is always set server-side — client value ignored
        public DateTime? Timestamp { get; set; }
    }

    public class MarkAllReadRequest
    {
        [Required(ErrorMessage = "SenderId is required")]
        public string? SenderId { get; set; }
        public string? ReceiverId { get; set; }
    }
}
