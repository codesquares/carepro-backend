using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Entities
{
    public class ClientRecommendation
    {
        public ObjectId Id { get; set; }
        
        public string ClientId { get; set; }
        
        public List<RecommendationItem> Recommendations { get; set; }
        
        public DateTime GeneratedAt { get; set; }
        
        public DateTime CreatedAt { get; set; }
        
        public DateTime? UpdatedAt { get; set; }
        
        public bool IsActive { get; set; }
        
        public bool IsArchived { get; set; }
        
        public DateTime? ArchivedAt { get; set; }
        
        // Tracking fields for analytics
        public DateTime? ViewedAt { get; set; }
        
        public bool ActedUpon { get; set; }
        
        public string? SelectedProviderId { get; set; }
        
        // Reference to the preferences that generated these recommendations
        public string? PreferenceSnapshot { get; set; }
    }
    
    public class RecommendationItem
    {
        public string ProviderId { get; set; }
        
        public string? CaregiverId { get; set; }
        
        public double MatchScore { get; set; }
        
        public string ServiceType { get; set; }
        
        public string Location { get; set; }
        
        public decimal Price { get; set; }
        
        public string PriceUnit { get; set; }
        
        public double Rating { get; set; }
        
        public int ReviewCount { get; set; }
    }
}
