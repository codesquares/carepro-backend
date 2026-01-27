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
        //public async Task SendMessage(string senderId, string receiverId, string message)
        //{
        //    try
        //    {
        //        var chatMessage = new ChatMessage
        //        {
        //            SenderId = senderId,
        //            ReceiverId = receiverId,
        //            Message = message,
        //            Timestamp = DateTime.UtcNow
        //        };

        //        await _chatRepository.SaveMessageAsync(chatMessage);

        //        await Clients.User(receiverId).SendAsync("ReceiveMessage", senderId, message);

        //    }
        //    catch (Exception)
        //    {

        //        await Clients.Caller.SendAsync("Error", "Message failed to send.");
        //    }

        //}


        public async Task<string> SendMessage(string senderId, string receiverId, string message)
        {
            // Validate input
            if (string.IsNullOrEmpty(senderId) || string.IsNullOrEmpty(receiverId) || string.IsNullOrEmpty(message))
            {
                _logger.LogWarning("SendMessage called with invalid parameters. SenderId: {SenderId}, ReceiverId: {ReceiverId}", senderId ?? "null", receiverId ?? "null");
                throw new HubException("Invalid message parameters");
            }

            _logger.LogInformation("Sending message from {SenderId} to {ReceiverId}", senderId, receiverId);

            // Sanitize message content to prevent XSS attacks
            var sanitizedMessage = _contentSanitizer.SanitizeText(message);

            // Create message object
            //var messageId = Guid.NewGuid().ToString();
            //var timestamp = DateTime.UtcNow;

            var chatMessage = new ChatMessage
            {
                SenderId = senderId,
                ReceiverId = receiverId,
                Message = sanitizedMessage,
                MessageId = ObjectId.GenerateNewId(),
                Timestamp = DateTime.UtcNow
            };

            // Save message to database
            await _chatRepository.SaveMessageAsync(chatMessage);

            // Send to recipient if online (their connection ID is in their user group)
            await Clients.Group(receiverId).SendAsync("ReceiveMessage", senderId, message, chatMessage.MessageId.ToString(), "sent");

            _logger.LogInformation("Message {MessageId} sent successfully from {SenderId} to {ReceiverId}", chatMessage.MessageId.ToString(), senderId, receiverId);

            // Return message ID to sender for tracking
            return chatMessage.MessageId.ToString();
        }


        public async Task<List<MessageDTO>> GetMessageHistory(string user1Id, string user2Id, int skip = 0, int take = 50)
        {
            // Get message history between two users
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
            // Update message status to "delivered"
            var message = await _chatRepository.UpdateMessageStatus(messageId, "delivered");

            // Notify sender that message was delivered
            if (message != null)
            {
                await Clients.Group(message.SenderId).SendAsync("MessageStatusChanged", messageId, "delivered");
            }
        }


        public async Task MessageRead(string messageId)
        {
            // Update message status to "read"
            var message = await _chatRepository.UpdateMessageStatus(messageId, "read");

            // Notify sender that message was read
            if (message != null)
            {
                await Clients.Group(message.SenderId).SendAsync("MessageStatusChanged", messageId, "read");
            }
        }


        /// <summary>
        /// Delete a message and notify participants
        /// </summary>
        public async Task<bool> DeleteMessage(string messageId, string userId)
        {
            try
            {
                // Get the message first to verify ownership
                var message = await _chatRepository.GetMessageByIdAsync(messageId);

                // Check if message exists
                if (message == null)
                {
                    throw new HubException("Message not found");
                }

                // Verify the user is authorized to delete this message
                if (message.SenderId != userId)
                {
                    throw new HubException("You can only delete your own messages");
                }

                // Delete the message
                bool deleted = await _chatRepository.DeleteMessageAsync(messageId);

                if (deleted)
                {
                    // Notify both the sender and receiver that the message was deleted
                    await Clients.Group(message.SenderId).SendAsync("MessageDeleted", messageId);
                    await Clients.Group(message.ReceiverId).SendAsync("MessageDeleted", messageId);

                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                // Log error properly in production
                throw new HubException($"Failed to delete message: {ex.Message}");
            }
        }

        /// <summary>
        /// Mark a message as read and notify the sender
        /// </summary>
        public async Task<bool> MarkMessageAsRead(string messageId, string userId)
        {
            try
            {
                // Get the message to verify the recipient
                var message = await _chatRepository.GetMessageByIdAsync(messageId);

                // Check if message exists
                if (message == null)
                {
                    throw new HubException("Message not found");
                }

                // Verify the user is the recipient
                if (message.ReceiverId != userId)
                {
                    throw new HubException("You can only mark messages sent to you as read");
                }

                // Mark as read
                bool success = await _chatRepository.MarkMessageAsReadAsync(messageId, userId);

                if (success)
                {
                    // Notify the sender that the message was read
                    await Clients.Group(message.SenderId).SendAsync("MessageRead", messageId, DateTime.UtcNow);

                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                // Log error properly in production
                throw new HubException($"Failed to mark message as read: {ex.Message}");
            }
        }

        /// <summary>
        /// Mark all messages from a specific sender as read
        /// </summary>
        public async Task<bool> MarkAllMessagesAsRead(string senderId, string receiverId)
        {
            try
            {
                // Mark all messages as read
                bool success = await _chatRepository.MarkAllMessagesAsReadAsync(receiverId, senderId);

                if (success)
                {
                    // Notify the sender that all messages were read
                    await Clients.Group(senderId).SendAsync("AllMessagesRead", receiverId, DateTime.UtcNow);

                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                // Log error properly in production
                throw new HubException($"Failed to mark all messages as read: {ex.Message}");
            }
        }

        /// <summary>
        /// Mark a message as delivered (received but not read yet)
        /// </summary>
        public async Task<bool> MarkMessageAsDelivered(string messageId, string userId)
        {
            try
            {
                // Get the message to verify the recipient
                var message = await _chatRepository.GetMessageByIdAsync(messageId);

                // Check if message exists
                if (message == null)
                {
                    throw new HubException("Message not found");
                }

                // Verify the user is the recipient
                if (message.ReceiverId != userId)
                {
                    throw new HubException("You can only mark messages sent to you as delivered");
                }

                // Mark as delivered
                bool success = await _chatRepository.MarkMessageAsDeliveredAsync(messageId, userId);

                if (success)
                {
                    // Notify the sender that the message was delivered
                    await Clients.Group(message.SenderId).SendAsync("MessageDelivered", messageId, DateTime.UtcNow);

                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                // Log error properly in production
                throw new HubException($"Failed to mark message as delivered: {ex.Message}");
            }
        }

        /// <summary>
        /// Get all conversations for a user
        /// </summary>
        public async Task<List<ConversationDTO>> GetUserConversations(string userId)
        {
            try
            {
                if (string.IsNullOrEmpty(userId))
                {
                    throw new HubException("UserId is required");
                }

                var conversations = await _chatRepository.GetAllUserConversationsAsync(userId);
                return conversations;
            }
            catch (Exception ex)
            {
                // Log error properly in production
                throw new HubException($"Failed to get user conversations: {ex.Message}");
            }
        }
    }
}
