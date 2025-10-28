using Application.DTOs;
using Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    public interface IChatRepository
    {
        Task SaveMessageAsync(ChatMessage chatMessage);

        Task<List<ChatMessage>> GetChatHistoryAsync(string user1, string user2);

        Task<IEnumerable<ChatPreviewResponse>> GetChatUserPreviewAsync(string userId);

        Task<bool> UpdateUserConnectionStatus(string userId, bool isOnline, string connectionId);

        Task<List<MessageDTO>> GetMessageHistory(string user1Id, string user2Id, int skip, int take);

        Task<bool> IsUserOnline(string userId);

        Task<List<string>> GetOnlineUsers();

        Task<ChatMessage?> UpdateMessageStatus(string messageId, string newStatus);

        // Message deletion methods
        Task<ChatMessage> GetMessageByIdAsync(string messageId);
        Task<bool> DeleteMessageAsync(string messageId);

        // Read receipt methods
        Task<bool> MarkMessageAsReadAsync(string messageId, string receiverId);
        Task<bool> MarkAllMessagesAsReadAsync(string receiverId, string senderId);
        Task<int> GetUnreadMessageCountAsync(string userId);
        Task<bool> MarkMessageAsDeliveredAsync(string messageId, string receiverId);

        // Get all conversations for a user
        Task<List<ConversationDTO>> GetAllUserConversationsAsync(string userId);
    }
}

