using Application.DTOs;
using Application.Interfaces.Content;
using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using MongoDB.Bson;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Content.Services
{
    public class ChatRepository : IChatRepository
    {
        private readonly CareProDbContext careProDbContext;
        private readonly INotificationService notificationService;


        public ChatRepository(CareProDbContext careProDbContext, INotificationService notificationService)
        {
            this.careProDbContext = careProDbContext;
            this.notificationService = notificationService;
        }

        public async Task SaveMessageAsync(ChatMessage chatMessage)
        {
            await careProDbContext.ChatMessages.AddAsync(chatMessage);
            await careProDbContext.SaveChangesAsync();
            
            // Create a notification for the recipient
            var sender = await careProDbContext.AppUsers.FirstOrDefaultAsync(u => 
                u.Id.ToString() == chatMessage.SenderId || u.AppUserId.ToString() == chatMessage.SenderId);
            
            if (sender != null)
            {
                string senderName = $"{sender.FirstName} {sender.LastName}";
                string notificationContent = $"{senderName} sent you a message";
                
                await notificationService.CreateNotificationAsync(
                    chatMessage.ReceiverId,
                    chatMessage.SenderId,
                    "Chat Message",
                    notificationContent,
                    "New Message Alert",
                    chatMessage.MessageId.ToString()
                );
            }
        }

        public async Task<List<ChatMessage>> GetChatHistoryAsync(string user1, string user2, int skip = 0, int take = 50)
        {           
            return await careProDbContext.ChatMessages
                .Where(m => ((m.SenderId == user1 && m.ReceiverId == user2) ||
                            (m.SenderId == user2 && m.ReceiverId == user1)) &&
                           !m.IsDeleted) // Only include non-deleted messages
                .OrderBy(m => m.Timestamp)
                .Skip(skip)
                .Take(take)
                .ToListAsync();
        }
        
        // Get a single message by ID
        public async Task<ChatMessage> GetMessageByIdAsync(string messageId)
        {
            return await careProDbContext.ChatMessages
                .FirstOrDefaultAsync(m => m.MessageId.ToString() == messageId);
        }
        
        // Delete a message (soft delete)
        public async Task<bool> DeleteMessageAsync(string messageId)
        {
            try
            {
                var message = await GetMessageByIdAsync(messageId);
                if (message == null)
                {
                    return false;
                }
                
                // Soft delete: mark as deleted
                message.IsDeleted = true;
                message.DeletedAt = DateTime.UtcNow;
                
                // Update the message
                careProDbContext.ChatMessages.Update(message);
                await careProDbContext.SaveChangesAsync();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }



        //public async Task<IEnumerable<ChatPreviewResponse>> GetChatUserPreviewAsync(string userId)
        //{
        //    var latestMessages = await careProDbContext.ChatMessages
        //        .Where(x => x.SenderId == userId || x.ReceiverId == userId)
        //        .GroupBy(x => x.SenderId.CompareTo(x.ReceiverId) < 0
        //                        ? new { User1 = x.SenderId, User2 = x.ReceiverId }
        //                        : new { User1 = x.ReceiverId, User2 = x.SenderId })
        //        .Select(g => g.OrderByDescending(m => m.Timestamp).FirstOrDefault()) // get latest message in each unique conversation
        //        .ToListAsync();

        //    var userIds = latestMessages
        //        .Select(m => m.SenderId == userId ? m.ReceiverId : m.SenderId)
        //        .Distinct()
        //        .ToList();

        //    var appUsers = await careProDbContext.AppUsers
        //        .Where(u => userIds.Contains(u.MessageId.ToString()))
        //        .ToListAsync();

        //    var result = latestMessages
        //        .Select(m =>
        //        {
        //            var chatPartnerId = m.SenderId == userId ? m.ReceiverId : m.SenderId;
        //            var user = appUsers.FirstOrDefault(u => u.MessageId.ToString() == chatPartnerId);

        //            if (user == null) return null;

        //            return new ChatPreviewResponse
        //            {
        //                FullName = user.FirstName + " " + user.LastName,
        //                AppUserId = user.AppUserId.ToString(),
        //                Email = user.Email,
        //                Role = user.Role,
        //                LastMessage = m.Message,
        //                LastMessageTimestamp = m.Timestamp
        //            };
        //        })
        //        .Where(x => x != null)
        //        .OrderByDescending(x => x.LastMessageTimestamp)
        //        .ToList();

        //    return result;

        //}



        public async Task<IEnumerable<ChatPreviewResponse>> GetChatUserPreviewAsync(string userId)
        {
            // Step 1: Fetch messages involving the user into memory
            var messages = await careProDbContext.ChatMessages
                .Where(x => x.SenderId == userId || x.ReceiverId == userId)
                .ToListAsync(); // Bring data into memory first

            // Step 2: Group by unique conversation pair (ignores order of sender/receiver)
            var latestMessages = messages
                .GroupBy(x => string.Compare(x.SenderId, x.ReceiverId) < 0
                                ? new { User1 = x.SenderId, User2 = x.ReceiverId }
                                : new { User1 = x.ReceiverId, User2 = x.SenderId })
                .Select(g => g.OrderByDescending(m => m.Timestamp).FirstOrDefault())
                .Where(m => m != null)
                .ToList();

            // Step 3: Extract IDs of chat partners
            var userIds = latestMessages
                .Select(m => m.SenderId == userId ? m.ReceiverId : m.SenderId)
                .Distinct()
                .ToList();

            // Step 4: Fetch user profiles for chat partners
            var appUsers = await careProDbContext.AppUsers
                .Where(u => userIds.Contains(u.Id.ToString()))
                .ToListAsync();

            // Step 5: Build chat preview responses
            var result = latestMessages
                .Select(m =>
                {
                    var chatPartnerId = m.SenderId == userId ? m.ReceiverId : m.SenderId;
                    var user = appUsers.FirstOrDefault(u => u.Id.ToString() == chatPartnerId);

                    if (user == null) return null;

                    return new ChatPreviewResponse
                    {
                        FullName = user.FirstName + " " + user.LastName,
                        AppUserId = user.AppUserId.ToString(),
                        Email = user.Email,
                        Role = user.Role,
                        LastMessage = m.Message,
                        LastMessageTimestamp = m.Timestamp
                    };
                })
                .Where(x => x != null)
                .OrderByDescending(x => x.LastMessageTimestamp)
                .ToList();

            return result;
        }


        //public async Task UpdateUserConnectionStatus(string userId, bool isOnline, string connectionId)
        //{
        //    var user = await careProDbContext.AppUsers
        //        .FirstOrDefaultAsync(u => u.MessageId.ToString() == userId || u.AppUserId.ToString() == userId);

        //    if (user != null)
        //    {
        //        user.IsOnline = isOnline;
        //        user.ConnectionId = isOnline ? connectionId : null;
        //        await careProDbContext.SaveChangesAsync();
        //    }
        //}


        public async Task<List<MessageDTO>> GetMessageHistory(string user1Id, string user2Id, int skip, int take)
        {
            var messages = await careProDbContext.ChatMessages
                .Where(m => (m.SenderId == user1Id && m.ReceiverId == user2Id) ||
                            (m.SenderId == user2Id && m.ReceiverId == user1Id))
                .OrderByDescending(m => m.Timestamp) // newest first
                .Skip(skip)
                .Take(take)
                .Select(m => new MessageDTO
                {
                    SenderId = m.SenderId,
                    ReceiverId = m.ReceiverId,
                    Message = m.Message,
                    Timestamp = m.Timestamp
                })
                .ToListAsync();

            // Optional: reverse to return messages oldest to newest
            messages.Reverse();

            return messages;
        }


        public async Task<bool> IsUserOnline(string userId)
        {
            var user = await careProDbContext.AppUsers
                .FirstOrDefaultAsync(u => u.Id.ToString() == userId || u.AppUserId.ToString() == userId);

            return user?.IsOnline ?? false;
        }

        public async Task<List<string>> GetOnlineUsers()
        {
            return await careProDbContext.AppUsers
                .Where(u => (bool)u.IsOnline)
                //.Select(u => u.AppUserId.ToString())
                .Select(u => u.FirstName + " " + u.LastName)
                .ToListAsync();
        }

        public async Task<ChatMessage?> UpdateMessageStatus(string messageId, string newStatus)
        {
            var message = await careProDbContext.ChatMessages
                .FirstOrDefaultAsync(m => m.MessageId.ToString() == messageId);

            if (message == null)
                return null;

            message.Status = newStatus;
            await careProDbContext.SaveChangesAsync();

            return message;
        }

        // Update user connection status
        public async Task<bool> UpdateUserConnectionStatus(string userId, bool isOnline, string connectionId)
        {
            try
            {
                // In a real implementation, you would typically store this in a separate collection
                // or a distributed cache like Redis. For simplicity, we'll just return true here.
                // TODO: Implement proper connection tracking

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // Mark message as read
        public async Task<bool> MarkMessageAsReadAsync(string messageId, string receiverId)
        {
            try
            {
                var message = await GetMessageByIdAsync(messageId);
                if (message == null || message.ReceiverId != receiverId)
                {
                    return false; // Message doesn't exist or user is not the recipient
                }

                // Don't update if already marked as read
                if (message.IsRead)
                {
                    return true;
                }

                // Mark as read
                message.IsRead = true;
                message.ReadAt = DateTime.UtcNow;

                // Update the message
                careProDbContext.ChatMessages.Update(message);
                await careProDbContext.SaveChangesAsync();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // Mark all messages from a specific sender as read
        public async Task<bool> MarkAllMessagesAsReadAsync(string receiverId, string senderId)
        {
            try
            {
                // Get all unread messages from sender to receiver
                var unreadMessages = await careProDbContext.ChatMessages
                    .Where(m => m.SenderId == senderId && 
                                m.ReceiverId == receiverId && 
                                !m.IsRead &&
                                !m.IsDeleted)
                    .ToListAsync();

                if (!unreadMessages.Any())
                {
                    return true; // No unread messages
                }

                // Mark all as read
                var now = DateTime.UtcNow;
                foreach (var message in unreadMessages)
                {
                    message.IsRead = true;
                    message.ReadAt = now;
                }

                await careProDbContext.SaveChangesAsync();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // Get unread message count for a user
        public async Task<int> GetUnreadMessageCountAsync(string userId)
        {
            return await careProDbContext.ChatMessages
                .CountAsync(m => m.ReceiverId == userId && 
                                !m.IsRead && 
                                !m.IsDeleted);
        }
        
        // Mark message as delivered (when user receives but hasn't read yet)
        public async Task<bool> MarkMessageAsDeliveredAsync(string messageId, string receiverId)
        {
            try
            {
                var message = await GetMessageByIdAsync(messageId);
                if (message == null || message.ReceiverId != receiverId)
                {
                    return false; // Message doesn't exist or user is not the recipient
                }

                // Don't update if already delivered or read
                if (message.IsDelivered || message.IsRead)
                {
                    return true;
                }

                // Mark as delivered
                message.IsDelivered = true;
                message.DeliveredAt = DateTime.UtcNow;

                // Update the message
                careProDbContext.ChatMessages.Update(message);
                await careProDbContext.SaveChangesAsync();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // Get all conversations for a specific user
        public async Task<List<ConversationDTO>> GetAllUserConversationsAsync(string userId)
        {
            try
            {
                // Get messages
                var messages = await careProDbContext.ChatMessages
                    .Where(m => m.SenderId == userId || m.ReceiverId == userId)
                    .OrderByDescending(m => m.Timestamp)
                    .ToListAsync();

                if (!messages.Any()) return new List<ConversationDTO>();

                // Step 2: Find distinct conversation partners
                var conversationPartners = messages
                    .Select(m => m.SenderId == userId ? m.ReceiverId : m.SenderId)
                    .Distinct()
                    .ToList();

                // Step 3: Parse to ObjectId and get user information
                var partnerObjectIds = conversationPartners
                    .Select(id => ObjectId.Parse(id))
                    .ToList();

                var partnerUsers = await careProDbContext.AppUsers
                    .Where(u => partnerObjectIds.Contains(u.AppUserId))
                    .ToListAsync();



                //// Step 1: Get all messages involving the user
                //var messages = await careProDbContext.ChatMessages
                //    .Where(m => m.SenderId == userId || m.ReceiverId == userId)
                //    .OrderByDescending(m => m.Timestamp)
                //    .ToListAsync();

                //// Step 2: Find distinct conversation partners
                //var conversationPartners = messages
                //    .Select(m => m.SenderId == userId ? m.ReceiverId : m.SenderId)
                //    .Distinct()
                //    .ToList();

                //// Step 3: Get user information for all conversation partners
                //var partnerUsers = await careProDbContext.AppUsers
                //    .Where(u => conversationPartners.Contains(u.Id.ToString()))
                //    .ToListAsync();

                // Step 4: Build conversation DTOs
                var conversations = new List<ConversationDTO>();
                foreach (var partnerId in conversationPartners)
                {
                    // Get partner user info
                    var partnerUser = partnerUsers.FirstOrDefault(u => u.AppUserId.ToString() == partnerId);
                    if (partnerUser == null) continue; // Skip if user not found

                    // Get latest message in this conversation
                    var latestMessage = messages
                        .Where(m => (m.SenderId == userId && m.ReceiverId == partnerId) || 
                                  (m.SenderId == partnerId && m.ReceiverId == userId))
                        .OrderByDescending(m => m.Timestamp)
                        .FirstOrDefault();

                    if (latestMessage == null) continue; // Skip if no messages found

                    // Count unread messages
                    var unreadCount = messages
                        .Count(m => m.SenderId == partnerId && 
                                  m.ReceiverId == userId && 
                                  !m.IsRead && 
                                  !m.IsDeleted);

                    // Create conversation dto
                    conversations.Add(new ConversationDTO
                    {
                        UserId = partnerId,
                        FullName = $"{partnerUser.FirstName} {partnerUser.LastName}",
                        Email = partnerUser.Email,
                        Role = partnerUser.Role,
                        IsOnline = partnerUser.IsOnline ?? false,
                        LastMessage = latestMessage.Message,
                        LastMessageTimestamp = latestMessage.Timestamp,
                        IsRead = latestMessage.SenderId == partnerId ? 
                            latestMessage.IsRead : true, // Messages from the current user are considered read
                        UnreadCount = unreadCount
                    });
                }

                // Sort conversations by most recent message
                return conversations
                    .OrderByDescending(c => c.LastMessageTimestamp)
                    .ToList();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error getting user conversations: {ex.Message}");
                return new List<ConversationDTO>();
            }
        }

        public Task<List<ChatMessage>> GetChatHistoryAsync(string user1, string user2)
        {
            throw new NotImplementedException();
        }
    }

}
