using MongoDB.Bson;
using System;

namespace Domain.Entities
{
    /// <summary>
    /// Tracks a caregiver's response (interest) in a client's care request.
    /// </summary>
    public class CareRequestResponse
    {
        public ObjectId Id { get; set; }

        /// <summary>FK → CareRequest</summary>
        public string CareRequestId { get; set; } = string.Empty;

        /// <summary>FK → Caregiver profile (ObjectId as string)</summary>
        public string CaregiverId { get; set; } = string.Empty;

        /// <summary>
        /// Response status: pending, shortlisted, hired, rejected
        /// </summary>
        public string Status { get; set; } = "pending";

        /// <summary>Optional cover message from the caregiver to the client</summary>
        public string? Message { get; set; }

        /// <summary>Optional rate proposed by the caregiver</summary>
        public decimal? ProposedRate { get; set; }

        /// <summary>Match score at time of response (from matching engine, if available)</summary>
        public double? MatchScore { get; set; }

        public DateTime RespondedAt { get; set; } = DateTime.UtcNow;

        public DateTime? ShortlistedAt { get; set; }

        public DateTime? HiredAt { get; set; }

        /// <summary>The special gig ID created when client hires this responder</summary>
        public string? SpecialGigId { get; set; }
    }
}
