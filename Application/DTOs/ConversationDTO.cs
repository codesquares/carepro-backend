using System;

namespace Application.DTOs
{
    public class ConversationDTO
    {
        public string UserId { get; set; }
        public string FullName { get; set; }
        public string Email { get; set; }
        public string Role { get; set; }
        public bool IsOnline { get; set; }
        public string LastMessage { get; set; }
        public DateTime LastMessageTimestamp { get; set; }
        public bool IsRead { get; set; }
        public int UnreadCount { get; set; }
    }
}
