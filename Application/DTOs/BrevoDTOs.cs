using System.Collections.Generic;

namespace Application.DTOs
{
    /// <summary>
    /// Queued unit of work for BrevoSyncBackgroundConsumer. UserType is "Client" or "Caregiver" —
    /// determines which attribute/list-mapping rules the consumer applies.
    /// </summary>
    public record BrevoSyncJob(string UserId, string UserType);

    /// <summary>
    /// Body for Brevo's POST /v3/contacts upsert (updateEnabled=true — idempotent by email,
    /// no local dedupe table needed).
    /// </summary>
    public class BrevoUpsertContactRequest
    {
        public string Email { get; set; } = string.Empty;
        public Dictionary<string, object> Attributes { get; set; } = new();
        public List<int>? ListIds { get; set; }
        public List<int>? UnlinkListIds { get; set; }
        public bool UpdateEnabled { get; set; } = true;
    }
}
