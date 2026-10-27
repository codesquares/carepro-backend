using System;

namespace Application.DTOs
{
    /// <summary>
    /// Where a client↔caregiver conversation stands under the assignment model.
    /// </summary>
    public static class ChatAccessStates
    {
        /// <summary>The two people share an Accepted assignment — messaging is open.</summary>
        public const string Active = "Active";

        /// <summary>They had an assignment that was accepted and has since ended — read-only.</summary>
        public const string Ended = "Ended";

        /// <summary>Old-model thread with no assignment behind it — read-only history.</summary>
        public const string Archived = "Archived";

        /// <summary>No assignment and no message history — no conversation exists.</summary>
        public const string None = "None";
    }

    public class ChatCounterpartDTO
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        /// <summary>"Client" | "Caregiver"</summary>
        public string Role { get; set; } = string.Empty;
        public string? ProfileImage { get; set; }
    }

    public class ChatAccessDTO
    {
        public string State { get; set; } = ChatAccessStates.None;
        public bool CanSend { get; set; }
        /// <summary>The Accepted assignment (Active) or the most recent ended one (Ended); null otherwise.</summary>
        public string? AssignmentId { get; set; }
        /// <summary>Human-readable reason when <see cref="CanSend"/> is false.</summary>
        public string? Reason { get; set; }
        /// <summary>Null when <see cref="State"/> is None — identity is never disclosed without a relationship.</summary>
        public ChatCounterpartDTO? Counterpart { get; set; }
    }
}
