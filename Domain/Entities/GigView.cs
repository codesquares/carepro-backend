using System;
using MongoDB.Bson;

namespace Domain.Entities
{
    public class GigView
    {
        public ObjectId Id { get; set; } = ObjectId.GenerateNewId();
        public string GigId { get; set; } = string.Empty;
        public string? ViewerUserId { get; set; }
        public string? ViewerSessionId { get; set; }
        public string? Source { get; set; }
        public DateTime ViewedAt { get; set; } = DateTime.UtcNow;
    }
}