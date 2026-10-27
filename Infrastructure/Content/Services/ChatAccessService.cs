using Application.DTOs;
using Application.Interfaces.Content;
using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Infrastructure.Content.Services
{
    public class ChatAccessService : IChatAccessService
    {
        private const string ClientRole = "Client";
        private const string CaregiverRole = "Caregiver";

        private readonly CareProDbContext _db;

        public ChatAccessService(CareProDbContext db)
        {
            _db = db;
        }

        public async Task<ChatAccessDTO> GetAccessAsync(string userId, string role, string otherUserId)
        {
            var normalizedRole = NormalizeRole(role);
            if (normalizedRole == null || string.IsNullOrEmpty(otherUserId) || userId == otherUserId)
            {
                return Denied(ChatAccessStates.None, "Only a client and their assigned caregiver can message each other.");
            }

            var (clientId, caregiverId) = normalizedRole == ClientRole
                ? (userId, otherUserId)
                : (otherUserId, userId);

            var assignments = await _db.Assignments
                .Where(a => a.ClientId == clientId && a.CaregiverId == caregiverId)
                .ToListAsync();

            var result = Classify(assignments);
            if (result == null)
            {
                var hasHistory = await _db.ChatMessages.AnyAsync(m =>
                    (m.SenderId == userId && m.ReceiverId == otherUserId) ||
                    (m.SenderId == otherUserId && m.ReceiverId == userId));

                result = hasHistory
                    ? Denied(ChatAccessStates.Archived, ArchivedReason)
                    : Denied(ChatAccessStates.None,
                        "You can only message a client or caregiver you are assigned to. Use the message option on your assignment.");
            }

            if (result.State != ChatAccessStates.None)
            {
                result.Counterpart = await LoadCounterpartAsync(normalizedRole == ClientRole ? CaregiverRole : ClientRole, otherUserId);
            }

            return result;
        }

        public async Task<Dictionary<string, ChatAccessDTO>> GetStatesForPartnersAsync(string userId, string role, IEnumerable<string> partnerIds)
        {
            var partners = partnerIds.Where(p => !string.IsNullOrEmpty(p)).Distinct().ToList();
            var states = new Dictionary<string, ChatAccessDTO>();
            if (partners.Count == 0) return states;

            var normalizedRole = NormalizeRole(role);
            List<Assignment> assignments = new();
            if (normalizedRole == ClientRole)
            {
                assignments = await _db.Assignments
                    .Where(a => a.ClientId == userId && partners.Contains(a.CaregiverId))
                    .ToListAsync();
            }
            else if (normalizedRole == CaregiverRole)
            {
                assignments = await _db.Assignments
                    .Where(a => a.CaregiverId == userId && partners.Contains(a.ClientId))
                    .ToListAsync();
            }

            foreach (var partner in partners)
            {
                var pair = assignments.Where(a => (normalizedRole == ClientRole ? a.CaregiverId : a.ClientId) == partner);
                // Partners come from existing message history, so "no assignment" means an old-model thread.
                states[partner] = Classify(pair) ?? Denied(ChatAccessStates.Archived, ArchivedReason);
            }

            return states;
        }

        public async Task EnrichConversationsAsync(string userId, string role, IList<ConversationDTO> conversations)
        {
            var states = await GetStatesForPartnersAsync(userId, role, conversations.Select(c => c.UserId));
            foreach (var conversation in conversations)
            {
                if (states.TryGetValue(conversation.UserId, out var access))
                {
                    conversation.AccessState = access.State;
                    conversation.CanSend = access.CanSend;
                }
            }
        }

        private const string ArchivedReason =
            "This is an archived conversation from before assignments. It is read-only history.";

        private const string EndedReason =
            "This assignment has ended, so this conversation is now read-only. Please contact CarePro support if you have further questions.";

        /// <summary>
        /// Active if any Accepted assignment exists. Ended if one was responded to (so it was accepted)
        /// and is no longer Accepted — that is, any terminal status other than Declined. Returns null
        /// when the pair has no qualifying assignment.
        /// </summary>
        private static ChatAccessDTO? Classify(IEnumerable<Assignment> pairAssignments)
        {
            var list = pairAssignments.ToList();

            var accepted = list.FirstOrDefault(a => a.Status == AssignmentStatuses.Accepted);
            if (accepted != null)
            {
                return new ChatAccessDTO
                {
                    State = ChatAccessStates.Active,
                    CanSend = true,
                    AssignmentId = accepted.Id.ToString()
                };
            }

            var ended = list
                .Where(a => a.RespondedAt != null
                            && a.Status != AssignmentStatuses.Declined
                            && a.Status != AssignmentStatuses.PendingAcceptance)
                .OrderByDescending(a => a.UpdatedAt ?? a.RespondedAt)
                .FirstOrDefault();
            if (ended != null)
            {
                return new ChatAccessDTO
                {
                    State = ChatAccessStates.Ended,
                    CanSend = false,
                    AssignmentId = ended.Id.ToString(),
                    Reason = EndedReason
                };
            }

            return null;
        }

        private static ChatAccessDTO Denied(string state, string reason) => new()
        {
            State = state,
            CanSend = false,
            Reason = reason
        };

        private static string? NormalizeRole(string? role)
        {
            if (string.Equals(role, ClientRole, StringComparison.OrdinalIgnoreCase)) return ClientRole;
            if (string.Equals(role, CaregiverRole, StringComparison.OrdinalIgnoreCase)) return CaregiverRole;
            return null;
        }

        private async Task<ChatCounterpartDTO?> LoadCounterpartAsync(string counterpartRole, string id)
        {
            if (!ObjectId.TryParse(id, out var objectId)) return null;

            if (counterpartRole == CaregiverRole)
            {
                var caregiver = await _db.CareGivers.FirstOrDefaultAsync(c => c.Id == objectId);
                return caregiver == null ? null : new ChatCounterpartDTO
                {
                    Id = id,
                    Name = $"{caregiver.FirstName} {caregiver.LastName}".Trim(),
                    Role = CaregiverRole,
                    ProfileImage = caregiver.ProfileImage
                };
            }

            var client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == objectId);
            return client == null ? null : new ChatCounterpartDTO
            {
                Id = id,
                Name = $"{client.FirstName} {client.LastName}".Trim(),
                Role = ClientRole,
                ProfileImage = client.ProfileImage
            };
        }
    }
}
