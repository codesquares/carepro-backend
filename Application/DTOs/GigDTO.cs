using Microsoft.AspNetCore.Http;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.DTOs
{
    public class GigDTO
    {
        public string Id { get; set; }


        public string Title { get; set; }
        public string Category { get; set; }
        public List<string> SubCategory { get; set; }
        public string Tags { get; set; }
        public string PackageType { get; set; }
        public string PackageName { get; set; }
        public List<string> PackageDetails { get; set; }
        public string DeliveryTime { get; set; }
        public int Price { get; set; }
        public string? Image1 { get; set; }

        // VideoURL is the caregiver's intro video and CaregiverId identifies them; like CaregiverName below,
        // GET /Gigs/{id} omits both (and the professional-history arrays) for anyone but the owner/admin.
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public string? VideoURL { get; set; }
        public string Status { get; set; }

        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public string CaregiverId { get; set; }
        // Omitted from the JSON (not just null) unless the controller deliberately populates it for the
        // owning caregiver/admin — clients must not see who is behind a gig.
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public string CaregiverName { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedOn { get; set; }
        public bool? IsUpdatedToPause { get; set; }

        /// <summary>
        /// When creating a Draft gig in a specialized category, this warns
        /// if the caregiver is NOT yet eligible (missing assessment/certs).
        /// Null means no warning (eligible or general category).
        /// </summary>
        public string? EligibilityWarning { get; set; }

        // ── Care Request Special Gig Fields ──
        public bool? IsSpecialGig { get; set; }
        public string? CareRequestId { get; set; }
        public string? ScopedClientId { get; set; }

        // ── Public Professional Profile Enrichment ──
        // Always returned as arrays — never null. Empty array if the caregiver
        // has not submitted any records.
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public List<CaregiverEducationResponse> CaregiverEducation { get; set; } = new();
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public List<CaregiverQualificationResponse> CaregiverCertifications { get; set; } = new();
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public List<CaregiverWorkExperienceResponse> CaregiverWorkExperience { get; set; } = new();

    }

    public class AddGigRequest
    {
        public string Title { get; set; }
        public string Category { get; set; }
        public List<string> SubCategory { get; set; }
        public string? Tags { get; set; }
        public string? PackageType { get; set; }
        public string? PackageName { get; set; }
        public string? PackageDetails { get; set; }
        public string? DeliveryTime { get; set; }
        public int Price { get; set; }

        public IFormFile? Image1 { get; set; }


        // public string? VideoURL { get; set; }
        public string Status { get; set; }

        public string CaregiverId { get; set; }
    }

    public class UpdateGigStatusToPauseRequest
    {
        public string Status { get; set; }

        public string CaregiverId { get; set; }

    }

    public class UpdateGigRequest
    {
        public string Category { get; set; }
        public List<string> SubCategory { get; set; }
        public string? Tags { get; set; }
        public string? PackageType { get; set; }
        public string? PackageName { get; set; }
        public string? PackageDetails { get; set; }
        public string? DeliveryTime { get; set; }
        public int Price { get; set; }
        public string Status { get; set; }

        public IFormFile? Image1 { get; set; }

        public string CaregiverId { get; set; }

    }

    public class RecommendGigToClientRequest
    {
        public string ClientId { get; set; } = string.Empty;
        public string GigId { get; set; } = string.Empty;
    }

    public class AdminBulkDeleteGigsRequest
    {
        public List<string>? GigIds { get; set; }
        public bool DeleteAll { get; set; }
        public string AdminUserId { get; set; } = string.Empty;
    }

    public class AdminBulkDeleteResult
    {
        public int DeletedCount { get; set; }
        public int SkippedCount { get; set; }
        public int FailedCount { get; set; }
        public List<string> SkippedGigIds { get; set; } = new();
        public List<string> SkippedReasons { get; set; } = new();
        public string Message { get; set; } = string.Empty;
    }

    public class DeletedGigDTO
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Category { get; set; }
        public List<string> SubCategory { get; set; }
        public string PackageType { get; set; }
        public string PackageName { get; set; }
        public int Price { get; set; }
        public string? Image1 { get; set; }
        public string CaregiverId { get; set; }
        public string CaregiverName { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? DeletedOn { get; set; }
        public int DaysRemaining { get; set; }
        public bool CanRestore { get; set; }
    }
}
