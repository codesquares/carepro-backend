using MongoDB.Bson;
using System;
using System.Collections.Generic;

namespace Domain.Entities
{
    public class CareRequest
    {
        public ObjectId Id { get; set; }

        public string ClientId { get; set; } = string.Empty;

        public string ServiceCategory { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public string Urgency { get; set; } = string.Empty;

        public List<string> Schedule { get; set; } = new List<string>();

        public string Frequency { get; set; } = string.Empty;

        public string? Duration { get; set; }

        public string? Location { get; set; }

        public string? Budget { get; set; }

        public string? SpecialRequirements { get; set; }

        /// <summary>
        /// Status of the care request: pending, matched, accepted, completed, cancelled
        /// </summary>
        public string Status { get; set; } = "pending";

        public DateTime CreatedAt { get; set; }

        public DateTime? UpdatedAt { get; set; }
    }
}
