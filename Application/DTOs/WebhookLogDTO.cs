using System;
using System.Collections.Generic;

namespace Application.DTOs
{
    public class WebhookLogResponse
    {
        public string Id { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string WebhookType { get; set; } = string.Empty;
        public DateTime ReceivedAt { get; set; }
        public DateTime? ProcessedAt { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? VerificationId { get; set; }
        public string ClientIp { get; set; } = string.Empty;
        public bool HasRawData { get; set; }
    }

    public class ParsedWebhookDataResponse
    {
        public string WebhookLogId { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public DateTime ReceivedAt { get; set; }
        
        // Parsed data from webhook
        public WebhookParsedData ParsedData { get; set; } = new();
        
        // Full raw JSON for debugging
        public string RawPayload { get; set; } = string.Empty;
        
        // Caregiver's registered info for comparison
        public CaregiverProfileData RegisteredProfile { get; set; } = new();
    }

    public class WebhookParsedData
    {
        public string VerificationStatus { get; set; } = string.Empty;
        public string VerificationMethod { get; set; } = string.Empty;
        public string IdType { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string VerificationNo { get; set; } = string.Empty;
        
        public VerifiedNameData? VerifiedName { get; set; }
        public VerifiedDetailsData? VerifiedDetails { get; set; }
    }

    public class VerifiedNameData
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string MiddleName { get; set; } = string.Empty;
    }

    public class VerifiedDetailsData
    {
        public string DateOfBirth { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
    }

    public class CaregiverProfileData
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string MiddleName { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }

    public class PendingVerificationReviewResponse
    {
        public string UserId { get; set; } = string.Empty;
        public string CaregiverName { get; set; } = string.Empty;
        public string CaregiverEmail { get; set; } = string.Empty;
        
        // From Verification table
        public string VerificationId { get; set; } = string.Empty;
        public string VerificationStatus { get; set; } = string.Empty;
        public string VerificationMethod { get; set; } = string.Empty;
        public bool IsVerified { get; set; }
        public DateTime VerifiedOn { get; set; }
        
        // Link to raw data
        public string? WebhookLogId { get; set; }
        public bool HasRawData { get; set; }
    }

    public class AdminVerificationReviewRequest
    {
        public string VerificationId { get; set; } = string.Empty;
        public string AdminId { get; set; } = string.Empty;
        public string Decision { get; set; } = string.Empty; // "Approve" or "Reject"
        public string? AdminNotes { get; set; }
        public string? ReviewedWebhookLogId { get; set; }
    }

    public class AdminVerificationReviewResponse
    {
        public bool Success { get; set; }
        public string NewStatus { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string VerificationId { get; set; } = string.Empty;
    }
}
