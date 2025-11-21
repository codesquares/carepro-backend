using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Entities
{
    public class RefreshToken
    {
        public ObjectId Id { get; set; }
        
        public string Token { get; set; } = string.Empty;
        
        public string UserId { get; set; } = string.Empty;
        
        public DateTime ExpiryDate { get; set; }
        
        public bool IsRevoked { get; set; } = false;
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        public DateTime? RevokedAt { get; set; }
        
        public string? RevokedBy { get; set; }
        
        public string? ReplacedByToken { get; set; }
        
        public bool IsExpired => DateTime.UtcNow >= ExpiryDate;
        
        public bool IsActive => !IsRevoked && !IsExpired;
    }
}