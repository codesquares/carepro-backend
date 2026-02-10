using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Application.DTOs
{
    /// <summary>
    /// DTO for creating a new care request
    /// </summary>
    public class CreateCareRequestDTO
    {
        [Required]
        public string ClientId { get; set; } = string.Empty;

        [Required]
        public string ServiceCategory { get; set; } = string.Empty;

        [Required]
        [MaxLength(120)]
        public string Title { get; set; } = string.Empty;

        [Required]
        [MaxLength(2000)]
        public string Description { get; set; } = string.Empty;

        [Required]
        public string Urgency { get; set; } = string.Empty;

        [Required]
        public List<string> Schedule { get; set; } = new List<string>();

        [Required]
        public string Frequency { get; set; } = string.Empty;

        public string? Duration { get; set; }

        public string? Location { get; set; }

        public string? Budget { get; set; }

        [MaxLength(1000)]
        public string? SpecialRequirements { get; set; }
    }

    /// <summary>
    /// DTO for updating a care request
    /// </summary>
    public class UpdateCareRequestDTO
    {
        public string? ServiceCategory { get; set; }

        [MaxLength(120)]
        public string? Title { get; set; }

        [MaxLength(2000)]
        public string? Description { get; set; }

        public string? Urgency { get; set; }

        public List<string>? Schedule { get; set; }

        public string? Frequency { get; set; }

        public string? Duration { get; set; }

        public string? Location { get; set; }

        public string? Budget { get; set; }

        [MaxLength(1000)]
        public string? SpecialRequirements { get; set; }
    }

    /// <summary>
    /// DTO for returning care request data
    /// </summary>
    public class CareRequestDTO
    {
        public string Id { get; set; } = string.Empty;

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

        public string Status { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }

        public DateTime? UpdatedAt { get; set; }
    }

    /// <summary>
    /// Response wrapper for care request operations
    /// </summary>
    public class CareRequestResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public CareRequestDTO? Data { get; set; }
    }

    /// <summary>
    /// Response wrapper for multiple care requests
    /// </summary>
    public class CareRequestListResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public List<CareRequestDTO> Data { get; set; } = new List<CareRequestDTO>();
        public int TotalCount { get; set; }
    }
}
