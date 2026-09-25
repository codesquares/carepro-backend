using Application.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    /// <summary>
    /// Decides who may message whom. Messaging exists only between a client and a
    /// caregiver who share an Accepted assignment; once that assignment ends (or for
    /// old-model threads that never had one) the conversation is read-only.
    /// </summary>
    public interface IChatAccessService
    {
        /// <summary>Access between the caller and one other user. <paramref name="role"/> is the caller's JWT role.</summary>
        Task<ChatAccessDTO> GetAccessAsync(string userId, string role, string otherUserId);

        /// <summary>Access state for each existing conversation partner of the caller (partners are assumed to have message history).</summary>
        Task<Dictionary<string, ChatAccessDTO>> GetStatesForPartnersAsync(string userId, string role, IEnumerable<string> partnerIds);

        /// <summary>Stamps <see cref="ConversationDTO.AccessState"/> / <see cref="ConversationDTO.CanSend"/> on each conversation.</summary>
        Task EnrichConversationsAsync(string userId, string role, IList<ConversationDTO> conversations);
    }
}
