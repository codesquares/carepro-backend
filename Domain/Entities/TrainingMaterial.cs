using MongoDB.Bson;
using System;
using System.ComponentModel.DataAnnotations;

namespace Domain.Entities
{
    public class TrainingMaterial
    {
        public ObjectId Id { get; set; }

        [Required]
        public string Title { get; set; } = string.Empty;

        [Required]
        public string UserType { get; set; } = string.Empty; // "Caregiver", "Cleaner", "Both"

        [Required]
        public string FileType { get; set; } = string.Empty; // "PDF", "Video", "Document"

        [Required]
        public string CloudinaryUrl { get; set; } = string.Empty; // URL from Cloudinary

        [Required]
        public string FileName { get; set; } = string.Empty; // Original filename

        public long FileSize { get; set; } // Size in bytes

        public string? Description { get; set; } // Optional description

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        [Required]
        public string UploadedBy { get; set; } = string.Empty; // Admin user ID who uploaded

        public string? CloudinaryPublicId { get; set; } // For deletion from Cloudinary
    }

    // Enum for file types to ensure consistency
    public static class TrainingMaterialFileType
    {
        public const string PDF = "PDF";
        public const string Video = "Video";
        public const string Document = "Document";
    }

    // Enum for user types to ensure consistency
    public static class TrainingMaterialUserType
    {
        public const string Caregiver = "Caregiver";
        public const string Cleaner = "Cleaner";
        public const string Both = "Both";
    }
}