using System;
using MongoDB.Bson;

namespace Domain.Entities
{
    /// <summary>
    /// Stores the response of a previously-processed POST so that a client retrying
    /// with the same Idempotency-Key receives the original response instead of the
    /// request being processed twice. Used by signup endpoints to defeat double-clicks
    /// and network-level retries.
    /// </summary>
    public class IdempotencyRecord
    {
        public ObjectId Id { get; set; } = ObjectId.GenerateNewId();

        /// <summary>Client-supplied Idempotency-Key header value (trimmed).</summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>"METHOD path" — e.g. "POST /api/CareGivers/AddCaregiverUser". Used to
        /// reject reuse of the same key against a different endpoint.</summary>
        public string Endpoint { get; set; } = string.Empty;

        /// <summary>HTTP status to replay. Null while the original request is still in flight.</summary>
        public int? ResponseStatus { get; set; }

        /// <summary>Captured response body to replay. Null while the original request is in flight.</summary>
        public string? ResponseBody { get; set; }

        /// <summary>Captured Content-Type header to replay (typically application/json).</summary>
        public string? ContentType { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Records older than this are ignored and may be cleaned up.</summary>
        public DateTime ExpiresAt { get; set; }
    }
}
