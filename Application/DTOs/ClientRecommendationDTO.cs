using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.DTOs
{
    // Request DTO for creating recommendations
    public class CreateClientRecommendationRequest
    {
        [Required]
        public string ClientId { get; set; }
        
        [Required]
        [MinLength(1, ErrorMessage = "At least one recommendation is required")]
        public List<RecommendationItemDTO> Recommendations { get; set; }
        
        [Required]
        public DateTime GeneratedAt { get; set; }
    }
    
    // Request DTO for updating recommendations
    public class UpdateClientRecommendationRequest
    {
        [Required]
        public string ClientId { get; set; }
        
        [Required]
        [MinLength(1, ErrorMessage = "At least one recommendation is required")]
        public List<RecommendationItemDTO> Recommendations { get; set; }
        
        [Required]
        public DateTime GeneratedAt { get; set; }
    }
    
    // Individual recommendation item
    public class RecommendationItemDTO
    {
        [Required]
        public string ProviderId { get; set; }
        
        public string? CaregiverId { get; set; }
        
        [Range(0, 100, ErrorMessage = "Match score must be between 0 and 100")]
        public double MatchScore { get; set; }
        
        [Required]
        public string ServiceType { get; set; }
        
        [Required]
        public string Location { get; set; }
        
        [Required]
        [Range(0, double.MaxValue, ErrorMessage = "Price must be a positive number")]
        public decimal Price { get; set; }
        
        [Required]
        public string PriceUnit { get; set; }
        
        [Range(0, 5, ErrorMessage = "Rating must be between 0 and 5")]
        public double Rating { get; set; }
        
        [Range(0, int.MaxValue, ErrorMessage = "Review count must be a positive number")]
        public int ReviewCount { get; set; }
    }
    
    // Response DTO for GET requests
    public class ClientRecommendationDTO
    {
        public string? RecommendationId { get; set; }
        
        public string? ClientId { get; set; }
        
        public List<RecommendationItemDTO>? Recommendations { get; set; }
        
        public DateTime? GeneratedAt { get; set; }
        
        public DateTime? CreatedAt { get; set; }
        
        public DateTime? UpdatedAt { get; set; }
        
        public bool IsActive { get; set; }
        
        // Analytics fields
        public DateTime? ViewedAt { get; set; }
        
        public bool ActedUpon { get; set; }
        
        public string? SelectedProviderId { get; set; }
    }
    
    // Success response DTO
    public class ClientRecommendationResponse
    {
        public bool Success { get; set; }
        
        public string Message { get; set; }
        
        public string? RecommendationId { get; set; }
        
        public string? ClientId { get; set; }
        
        public int TotalRecommendations { get; set; }
        
        public DateTime? SavedAt { get; set; }
        
        public DateTime? UpdatedAt { get; set; }
    }
    
    // Error response DTO
    public class ClientRecommendationErrorResponse
    {
        public bool Success { get; set; }
        
        public string Error { get; set; }
        
        public string Message { get; set; }
        
        public List<string>? Details { get; set; }
    }
}
