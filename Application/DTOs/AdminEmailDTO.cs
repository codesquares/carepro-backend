using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Application.DTOs
{
    public class SendCustomEmailRequest
    {
        [Required]
        [EmailAddress]
        public string RecipientEmail { get; set; } = string.Empty;

        [Required]
        public string RecipientName { get; set; } = string.Empty;

        [Required]
        [StringLength(200, MinimumLength = 3)]
        public string Subject { get; set; } = string.Empty;

        [Required]
        public string Message { get; set; } = string.Empty;
    }

    public class SendBulkEmailRequest
    {
        [Required]
        public string RecipientType { get; set; } = string.Empty; // "All", "Caregivers", "Clients", "Specific"

        public List<string>? SpecificUserIds { get; set; }

        [Required]
        [StringLength(200, MinimumLength = 3)]
        public string Subject { get; set; } = string.Empty;

        [Required]
        public string Message { get; set; } = string.Empty;
    }

    public class BulkEmailResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int TotalRecipients { get; set; }
        public int SuccessfulSends { get; set; }
        public int FailedSends { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }
}
