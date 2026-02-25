using Application.DTOs;
using Application.Interfaces.Common;
using Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Content.Services
{
    [Authorize]
    public class ChatHub : Hub
    {
        private readonly ChatRepository _chatRepository;
        private readonly ILogger<ChatHub> _logger;
        private readonly IContentSanitizer _contentSanitizer;

        public ChatHub(ChatRepository chatRepository, ILogger<ChatHub> logger, IContentSanitizer contentSanitizer)
        {
            _chatRepository = chatRepository;
            _logger = logger;
            _contentSanitizer = contentSanitizer;
        }

        /// <summary>
        /// Gets the current authenticated user's ID from JWT claims.
        /// </summary>
        private string GetCurrentUserId()
        {
            return Context.User?.FindFirst("userId")?.Value
                ?? throw new HubException("Authentication required");
        }


        /// Connection Management
        public override async Task OnConnectedAsync()
        {
            try
            {
                // Get user ID from authenticated context
                var userId = Context.User?.FindFirst("userId")?.Value;

                if (string.IsNullOrEmpty(userId))
                {
                    _logger.LogWarning("User connected without userId claim. ConnectionId: {ConnectionId}", Context.ConnectionId);
                    await base.OnConnectedAsync();
                    return;
                }

                _logger.LogInformation("User {UserId} connecting with ConnectionId: {ConnectionId}", userId, Context.ConnectionId);

                // Associate connection ID with user ID
                await Groups.AddToGroupAsync(Context.ConnectionId, userId);

                // Set user as online
                await Clients.All.SendAsync("UserStatusChanged", userId, "Online");

                // Store user connection info in database/cache
                await _chatRepository.UpdateUserConnectionStatus(userId, true, Context.ConnectionId);

                _logger.LogInformation("User {UserId} successfully connected", userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in OnConnectedAsync for ConnectionId: {ConnectionId}", Context.ConnectionId);
            }

            await base.OnConnectedAsync();
        }


        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            try
            {
                // Get user ID from authenticated context
                var userId = Context.User?.FindFirst("userId")?.Value;

                if (string.IsNullOrEmpty(userId))
                {
                    _logger.LogWarning("User disconnected without userId claim. ConnectionId: {ConnectionId}", Context.ConnectionId);
                    await base.OnDisconnectedAsync(exception);
                    return;
                }

                _logger.LogInformation("User {UserId} disconnecting. ConnectionId: {ConnectionId}", userId, Context.ConnectionId);

                // Remove connection ID from user's group
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, userId);

                // Set user as offline
                await Clients.All.SendAsync("UserStatusChanged", userId, "Offline");

                // Update user connection status in database/cache
                await _chatRepository.UpdateUserConnectionStatus(userId, false, string.Empty);

                _logger.LogInformation("User {UserId} successfully disconnected", userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in OnDisconnectedAsync for ConnectionId: {ConnectionId}", Context.ConnectionId);
            }

            await base.OnDisconnectedAsync(exception);
        }


        /// Message Operations
        public async Task<string> SendMessage(string senderId, string receiverId, string message)
        {
            // SECURITY: Override senderId with authenticated user's identity — prevent spoofing
            var currentUserId = GetCurrentUserId();

            // Validate input
            if (string.IsNullOrEmpty(receiverId) || string.IsNullOrEmpty(message))
            {
                throw new HubException("Invalid message parameters");
            }

            // Prevent self-messaging
            if (currentUserId == receiverId)
            {
                throw new HubException("Cannot send messages to yourself");
            }

            _logger.LogInformation("Sending message from {SenderId} to {ReceiverId}", currentUserId, receiverId);

            // Sanitize message content to prevent XSS attacks
            var sanitizedMessage = _contentSanitizer.SanitizeText(message);

            // Enforce message length limit
            if (sanitizedMessage.Length > 5000)
            {
                throw new HubException("Message exceeds maximum length");
            }

            var chatMessage = new ChatMessage
            {
                SenderId = currentUserId, // SECURITY: Always use JWT identity
                ReceiverId = receiverId,
                Message = sanitizedMessage,
                MessageId = ObjectId.GenerateNewId(),
                Timestamp = DateTime.UtcNow
            };

            // Save message to database
            await _chatRepository.SaveMessageAsync(chatMessage);

            // SECURITY: Send SANITIZED message over SignalR (not raw input)
            await Clients.Group(receiverId).SendAsync("ReceiveMessage", currentUserId, sanitizedMessage, chatMessage.MessageId.ToString(), "sent");

            _logger.LogInformation("Message {MessageId} sent successfully from {SenderId} to {ReceiverId}", chatMessage.MessageId.ToString(), currentUserId, receiverId);

            // Return message ID to sender for tracking
            return chatMessage.MessageId.ToString();
        }


        public async Task<List<MessageDTO>> GetMessageHistory(string user1Id, string user2Id, int skip = 0, int take = 50)
        {
            var currentUserId = GetCurrentUserId();

            // IDOR: User must be one of the participants
            if (currentUserId != user1Id && currentUserId != user2Id)
            {
                throw new HubException("Access denied");
            }

            // Cap take to prevent bulk extraction
            take = Math.Min(take, 100);

            var messages = await _chatRepository.GetMessageHistory(user1Id, user2Id, skip, take);
            return messages;
        }


        public async Task<bool> GetOnlineStatus(string userId)
        {
            // Check if user is online
            var isOnline = await _chatRepository.IsUserOnline(userId);
            return isOnline;
        }

        public async Task<List<string>> GetOnlineUsers()
        {
            // Get all online users
            var onlineUsers = await _chatRepository.GetOnlineUsers();
            return onlineUsers;
        }

        /// Message Status Update
        public async Task MessageReceived(string messageId)
        {
            var currentUserId = GetCurrentUserId();

            // IDOR: Verify the user is the recipient of this message
            var message = await _chatRepository.GetMessageByIdAsync(messageId);
            if (message == null || message.ReceiverId != currentUserId)
            {
                throw new HubException("Access denied");
            }

            await _chatRepository.UpdateMessageStatus(messageId, "delivered");

            // Notify sender that message was delivered
            await Clients.Group(message.SenderId).SendAsync("MessageStatusChanged", messageId, "delivered");
        }


        public async Task MessageRead(string messageId)
        {
            var currentUserId = GetCurrentUserId();

            // IDOR: Verify the user is the recipient of this message
            var message = await _chatRepository.GetMessageByIdAsync(messageId);
            if (message == null || message.ReceiverId != currentUserId)
            {
                throw new HubException("Access denied");
            }

            await _chatRepository.UpdateMessageStatus(messageId, "read");

            // Notify sender that message was read
            await Clients.Group(message.SenderId).SendAsync("MessageStatusChanged", messageId, "read");
        }


        /// <summary>
        /// Delete a message and notify participants
        /// </summary>
        public async Task<bool> DeleteMessage(string messageId, string userId)
        {
            try
            {
                // SECURITY: Override userId with JWT identity
                var currentUserId = GetCurrentUserId();

                var message = await _chatRepository.GetMessageByIdAsync(messageId);

                if (message == null)
                {
                    throw new HubException("Message not found");
                }

                // IDOR: Must be the sender to delete
                if (message.SenderId != currentUserId)
                {
                    throw new HubException("You can only delete your own messages");
                }

                bool deleted = await _chatRepository.DeleteMessageAsync(messageId);

                if (deleted)
                {
                    await Clients.Group(message.SenderId).SendAsync("MessageDeleted", messageId);
                    await Clients.Group(message.ReceiverId).SendAsync("MessageDeleted", messageId);
                    return true;
                }

                return false;
            }
            catch (HubException)
            {
                throw; // Re-throw HubExceptions as-is (they have safe messages)
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting message {MessageId}", messageId);
                throw new HubException("Failed to delete message");
            }
        }

        /// <summary>
        /// Mark a message as read and notify the sender
        /// </summary>
        public async Task<bool> MarkMessageAsRead(string messageId, string userId)
        {
            try
            {
                // SECURITY: Override userId with JWT identity
                var currentUserId = GetCurrentUserId();

                var message = await _chatRepository.GetMessageByIdAsync(messageId);

                if (message == null)
                {
                    throw new HubException("Message not found");
                }

                // IDOR: Must be the recipient
                if (message.ReceiverId != currentUserId)
                {
                    throw new HubException("You can only mark messages sent to you as read");
                }

                bool success = await _chatRepository.MarkMessageAsReadAsync(messageId, currentUserId);

                if (success)
                {
                    await Clients.Group(message.SenderId).SendAsync("MessageRead", messageId, DateTime.UtcNow);
                    return true;
                }

                return false;
            }
            catch (HubException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking message as read {MessageId}", messageId);
                throw new HubException("Failed to mark message as read");
            }
        }

        /// <summary>
        /// Mark all messages from a specific sender as read
        /// </summary>
        public async Task<bool> MarkAllMessagesAsRead(string senderId, string receiverId)
        {
            try
            {
                // SECURITY: Override receiverId with JWT identity — can only mark own messages as read
                var currentUserId = GetCurrentUserId();

                bool success = await _chatRepository.MarkAllMessagesAsReadAsync(currentUserId, senderId);

                if (success)
                {
                    await Clients.Group(senderId).SendAsync("AllMessagesRead", currentUserId, DateTime.UtcNow);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking all messages as read");
                throw new HubException("Failed to mark all messages as read");
            }
        }

        /// <summary>
        /// Mark a message as delivered (received but not read yet)
        /// </summary>
        public async Task<bool> MarkMessageAsDelivered(string messageId, string userId)
        {
            try
            {
                // SECURITY: Override userId with JWT identity
                var currentUserId = GetCurrentUserId();

                var message = await _chatRepository.GetMessageByIdAsync(messageId);

                if (message == null)
                {
                    throw new HubException("Message not found");
                }

                // IDOR: Must be the recipient
                if (message.ReceiverId != currentUserId)
                {
                    throw new HubException("You can only mark messages sent to you as delivered");
                }

                bool success = await _chatRepository.MarkMessageAsDeliveredAsync(messageId, currentUserId);

                if (success)
                {
                    await Clients.Group(message.SenderId).SendAsync("MessageDelivered", messageId, DateTime.UtcNow);
                    return true;
                }

                return false;
            }
            catch (HubException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking message as delivered {MessageId}", messageId);
                throw new HubException("Failed to mark message as delivered");
            }
        }

        /// <summary>
        /// Get all conversations for a user
        /// </summary>
        public async Task<List<ConversationDTO>> GetUserConversations(string userId)
        {
            try
            {
                // SECURITY: Override userId with JWT identity — can only view own conversations
                var currentUserId = GetCurrentUserId();

                var conversations = await _chatRepository.GetAllUserConversationsAsync(currentUserId);
                return conversations;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user conversations");
                throw new HubException("Failed to get conversations");
            }
        }
    }
}
