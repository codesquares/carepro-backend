using Domain.Entities;
using Infrastructure.Content.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using System;

namespace CarePro_Api.Controllers.Content
{
    [Route("api/[controller]")]
    [ApiController]
    public class ChatController : ControllerBase
    {
        private readonly ChatRepository _chatRepository;

        public ChatController(ChatRepository chatRepository)
        {
            _chatRepository = chatRepository;
        }

        [HttpGet("history")]
        public async Task<IActionResult> GetChatHistory(string user1, string user2, int skip = 0, int take = 50)
        {
            var messages = await _chatRepository.GetChatHistoryAsync(user1, user2, skip, take);
            return Ok(messages);
        }

        [HttpGet("ChatPreview")]
        public async Task<IActionResult> GetChatUsersHistory(string userId)
        {
            var messages = await _chatRepository.GetChatUserPreviewAsync(userId);
            return Ok(messages);
        }

        [HttpPost("send")]
        public async Task<IActionResult> SendMessage([FromBody] SendMessageRequest request)
        {
            try
            {
                // Validate request
                if (string.IsNullOrEmpty(request.SenderId) || string.IsNullOrEmpty(request.ReceiverId) || string.IsNullOrEmpty(request.Message))
                {
                    return BadRequest(new { error = "SenderId, ReceiverId, and Message are required" });
                }

                // Create a new chat message
                var chatMessage = new ChatMessage
                {
                    //MessageId = Guid.NewGuid().ToString(),
                    MessageId = ObjectId.GenerateNewId(),
                    SenderId = request.SenderId,
                    ReceiverId = request.ReceiverId,
                    Message = request.Message,
                    Timestamp = request.Timestamp ?? DateTime.UtcNow
                };

                // Save the message
                await _chatRepository.SaveMessageAsync(chatMessage);

                // Return the message ID for the frontend
                return Ok(new { messageId = chatMessage.MessageId });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Failed to send message", message = ex.Message });
            }
        }

        [HttpDelete("delete/{messageId}")]
        public async Task<IActionResult> DeleteMessage(string messageId, [FromQuery] string userId)
        {
            try
            {
                // Validate request
                if (string.IsNullOrEmpty(messageId) || string.IsNullOrEmpty(userId))
                {
                    return BadRequest(new { error = "MessageId and UserId are required" });
                }

                // Get the message first to verify ownership
                var message = await _chatRepository.GetMessageByIdAsync(messageId);
                
                // Check if message exists
                if (message == null)
                {
                    return NotFound(new { error = "Message not found" });
                }

                // Verify the user is authorized to delete this message (must be the sender)
                if (message.SenderId != userId)
                {
                    return Unauthorized(new { error = "You can only delete your own messages" });
                }

                // Soft delete the message (mark as deleted but keep in database)
                bool success = await _chatRepository.DeleteMessageAsync(messageId);

                if (!success)
                {
                    return StatusCode(500, new { error = "Failed to delete message" });
                }

                // Return success response
                return Ok(new { success = true, messageId });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Failed to delete message", message = ex.Message });
            }
        }

        [HttpPost("mark-read/{messageId}")]
        public async Task<IActionResult> MarkMessageAsRead(string messageId, [FromQuery] string userId)
        {
            try
            {
                // Validate request
                if (string.IsNullOrEmpty(messageId) || string.IsNullOrEmpty(userId))
                {
                    return BadRequest(new { error = "MessageId and UserId are required" });
                }

                // Get the message first to verify the recipient
                var message = await _chatRepository.GetMessageByIdAsync(messageId);
                
                // Check if message exists
                if (message == null)
                {
                    return NotFound(new { error = "Message not found" });
                }

                // Verify the user is the recipient
                if (message.ReceiverId != userId)
                {
                    return Unauthorized(new { error = "You can only mark messages sent to you as read" });
                }

                // Mark as read
                bool success = await _chatRepository.MarkMessageAsReadAsync(messageId, userId);

                if (!success)
                {
                    return StatusCode(500, new { error = "Failed to mark message as read" });
                }

                // Return success response
                return Ok(new { success = true, messageId });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Failed to mark message as read", message = ex.Message });
            }
        }

        [HttpPost("mark-all-read")]
        public async Task<IActionResult> MarkAllMessagesAsRead([FromBody] MarkAllReadRequest request)
        {
            try
            {
                // Validate request
                if (string.IsNullOrEmpty(request.SenderId) || string.IsNullOrEmpty(request.ReceiverId))
                {
                    return BadRequest(new { error = "SenderId and ReceiverId are required" });
                }

                // Mark all messages as read
                bool success = await _chatRepository.MarkAllMessagesAsReadAsync(request.ReceiverId, request.SenderId);

                if (!success)
                {
                    return StatusCode(500, new { error = "Failed to mark all messages as read" });
                }

                // Return success response
                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Failed to mark all messages as read", message = ex.Message });
            }
        }

        [HttpGet("unread-count")]
        public async Task<IActionResult> GetUnreadMessageCount([FromQuery] string userId)
        {
            try
            {
                // Validate request
                if (string.IsNullOrEmpty(userId))
                {
                    return BadRequest(new { error = "UserId is required" });
                }

                // Get unread count
                int count = await _chatRepository.GetUnreadMessageCountAsync(userId);

                // Return count
                return Ok(new { count });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Failed to get unread message count", message = ex.Message });
            }
        }

        [HttpGet("conversations/{userId}")]
        public async Task<IActionResult> GetUserConversations(string userId)
        {
            try
            {
                // Validate request
                if (string.IsNullOrEmpty(userId))
                {
                    return BadRequest(new { error = "UserId is required" });
                }

                // Get all conversations for this user
                var conversations = await _chatRepository.GetAllUserConversationsAsync(userId);

                // Return conversations sorted by most recent message
                return Ok(conversations);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Failed to get user conversations", message = ex.Message });
            }
        }

        [HttpPost("mark-delivered/{messageId}")]
        public async Task<IActionResult> MarkMessageAsDelivered(string messageId, [FromQuery] string userId)
        {
            try
            {
                // Validate request
                if (string.IsNullOrEmpty(messageId) || string.IsNullOrEmpty(userId))
                {
                    return BadRequest(new { error = "MessageId and UserId are required" });
                }

                // Get the message first to verify the recipient
                var message = await _chatRepository.GetMessageByIdAsync(messageId);
                
                // Check if message exists
                if (message == null)
                {
                    return NotFound(new { error = "Message not found" });
                }

                // Verify the user is the recipient
                if (message.ReceiverId != userId)
                {
                    return Unauthorized(new { error = "You can only mark messages sent to you as delivered" });
                }

                // Mark as delivered
                bool success = await _chatRepository.MarkMessageAsDeliveredAsync(messageId, userId);

                if (!success)
                {
                    return StatusCode(500, new { error = "Failed to mark message as delivered" });
                }

                // Return success response
                return Ok(new { success = true, messageId });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Failed to mark message as delivered", message = ex.Message });
            }
        }
    }

    public class SendMessageRequest
    {
        public string SenderId { get; set; }
        public string ReceiverId { get; set; }
        public string Message { get; set; }
        public DateTime? Timestamp { get; set; }
    }

    public class MarkAllReadRequest
    {
        public string SenderId { get; set; }
        public string ReceiverId { get; set; }
    }
}
