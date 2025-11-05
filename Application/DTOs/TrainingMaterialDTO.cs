using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Application.DTOs
{
    public class TrainingMaterialDTO
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string UserType { get; set; } = string.Empty;
        public string FileType { get; set; } = string.Empty;
        public string CloudinaryUrl { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string? Description { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string UploadedBy { get; set; } = string.Empty;
        public string? CloudinaryPublicId { get; set; }
    }

    public class AddTrainingMaterialRequest
    {
        [Required]
        [StringLength(200, MinimumLength = 3)]
        public string Title { get; set; } = string.Empty;

        [Required]
        [RegularExpression("^(Caregiver|Cleaner|Both)$", ErrorMessage = "UserType must be Caregiver, Cleaner, or Both")]
        public string UserType { get; set; } = string.Empty;

        [Required]
        public IFormFile File { get; set; } = null!;

        [StringLength(500)]
        public string? Description { get; set; }

        [Required]
        public string UploadedBy { get; set; } = string.Empty; // Admin user ID
    }

    public class UpdateTrainingMaterialRequest
    {
        [Required]
        public string Id { get; set; } = string.Empty;

        [StringLength(200, MinimumLength = 3)]
        public string? Title { get; set; }

        [RegularExpression("^(Caregiver|Cleaner|Both)$", ErrorMessage = "UserType must be Caregiver, Cleaner, or Both")]
        public string? UserType { get; set; }

        [StringLength(500)]
        public string? Description { get; set; }

        public bool? IsActive { get; set; }

        public IFormFile? File { get; set; } // Optional - for replacing the file
    }

    public class TrainingMaterialListResponse
    {
        public List<TrainingMaterialDTO> Materials { get; set; } = new List<TrainingMaterialDTO>();
        public int TotalCount { get; set; }
        public string UserType { get; set; } = string.Empty;
    }

    public class TrainingMaterialUploadResponse
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string CloudinaryUrl { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string? Message { get; set; }
    }

    public class TrainingMaterialDownloadRequest
    {
        [Required]
        [RegularExpression("^(Caregiver|Cleaner|Both)$", ErrorMessage = "UserType must be Caregiver, Cleaner, or Both")]
        public string UserType { get; set; } = string.Empty;

        public bool ActiveOnly { get; set; } = true;
    }
}