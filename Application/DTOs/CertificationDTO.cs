using Microsoft.AspNetCore.Http;
using MongoDB.Bson;
using Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.DTOs
{
    public class CertificationDTO
    {
        public string? Id { get; set; }

        public string? CaregiverId { get; set; }

        public string? CertificateName { get; set; }

        public string? CertificateIssuer { get; set; }

        /// <summary>
        /// "educational", "professional", "medical", "specialized"
        /// </summary>
        public string? CertificateCategory { get; set; }

        /// <summary>
        /// Which service categories this certificate satisfies.
        /// </summary>
        public List<string>? ServiceCategories { get; set; }

        public DateTime? ExpiryDate { get; set; }

        public string? CertificateUrl { get; set; }

        public bool IsVerified { get; set; }

        public DocumentVerificationStatus VerificationStatus { get; set; }

        public DateTime? VerificationDate { get; set; }

        public decimal? VerificationConfidence { get; set; }

        public DateTime YearObtained { get; set; }

        public DateTime SubmittedOn { get; set; }
    }

    public class CertificationResponse
    {
        public string? Id { get; set; }

        public string? CaregiverId { get; set; }

        public string? CertificateName { get; set; }

        public string? CertificateIssuer { get; set; }

        public string? CertificateCategory { get; set; }

        public List<string>? ServiceCategories { get; set; }

        public DateTime? ExpiryDate { get; set; }

        public string? CertificateUrl { get; set; }

        public bool IsVerified { get; set; }

        public DocumentVerificationStatus VerificationStatus { get; set; }

        public DateTime? VerificationDate { get; set; }

        public decimal? VerificationConfidence { get; set; }

        public CertificateExtractedInfoDTO? ExtractedInfo { get; set; }

        public DateTime YearObtained { get; set; }

        public DateTime SubmittedOn { get; set; }
    }

    public class CertificateExtractedInfoDTO
    {
        public string? HolderName { get; set; }
        public string? DocumentNumber { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public DateTime? IssueDate { get; set; }
    }

    public class AddCertificationRequest
    {
        public string? CertificateName { get; set; }

        public string? CaregiverId { get; set; }

        public string? CertificateIssuer { get; set; }

        /// <summary>
        /// "educational", "professional", "medical", "specialized"
        /// </summary>
        public string? CertificateCategory { get; set; }

        public string? Certificate { get; set; }

        public DateTime YearObtained { get; set; }

        public DateTime? ExpiryDate { get; set; }

        public bool VerifyImmediately { get; set; } = true;
    }

    /// <summary>
    /// Multipart/form-data variant of <see cref="AddCertificationRequest"/>.
    /// Used by mobile clients to avoid the memory blow-up caused by base64-in-JSON
    /// uploads. The file is delivered as a streamed binary part instead of an
    /// embedded base64 string.
    /// </summary>
    public class AddCertificationFormRequest
    {
        public string? CertificateName { get; set; }

        public string? CaregiverId { get; set; }

        public string? CertificateIssuer { get; set; }

        /// <summary>
        /// "educational", "professional", "medical", "specialized"
        /// </summary>
        public string? CertificateCategory { get; set; }

        /// <summary>
        /// The certificate file (image or PDF) delivered as a multipart part.
        /// </summary>
        public IFormFile? Certificate { get; set; }

        public DateTime YearObtained { get; set; }

        public DateTime? ExpiryDate { get; set; }

        public bool VerifyImmediately { get; set; } = true;
    }

    public class CertificationUploadResponse
    {
        public string? CertificateId { get; set; }
        
        public string UploadStatus { get; set; } = "success";
        
        public string? CertificateUrl { get; set; }
        
        public VerificationResultDTO? Verification { get; set; }
    }

    public class VerificationResultDTO
    {
        public DocumentVerificationStatus Status { get; set; }
        
        public decimal Confidence { get; set; }
        
        public DateTime? VerifiedAt { get; set; }
        
        public string? ErrorMessage { get; set; }
        
        public CertificateExtractedInfoDTO? ExtractedInfo { get; set; }
    }

    // Admin Certificate Management DTOs
    public class AdminCertificationResponse : CertificationResponse
    {
        public CaregiverDetailsDTO? CaregiverDetails { get; set; }
        
        public string? DojahRawResponse { get; set; }
        
        public int? VerificationAttempts { get; set; }
    }

    public class CaregiverDetailsDTO
    {
        public string? FirstName { get; set; }
        
        public string? LastName { get; set; }
        
        public string? Email { get; set; }
        
        public string? PhoneNumber { get; set; }
    }

    public class ManualApprovalRequest
    {
        public string CertificateId { get; set; } = string.Empty;
        
        public string AdminId { get; set; } = string.Empty;
        
        public string? ApprovalNotes { get; set; }
    }

    public class ManualRejectionRequest
    {
        public string CertificateId { get; set; } = string.Empty;
        
        public string AdminId { get; set; } = string.Empty;
        
        public string RejectionReason { get; set; } = string.Empty;
    }

    public class CertificateManagementResponse
    {
        public bool Success { get; set; }
        
        public string Message { get; set; } = string.Empty;
        
        public AdminCertificationResponse? Certificate { get; set; }
    }

    public class AdminCertificateReviewRequest
    {
        public string CertificateId { get; set; } = string.Empty;
        
        public string AdminId { get; set; } = string.Empty;
        
        public bool Approved { get; set; }
        
        public string? AdminNotes { get; set; }
    }

    public class AdminCertificateReviewResponse
    {
        public bool Success { get; set; }
        
        public string Message { get; set; } = string.Empty;
        
        public DocumentVerificationStatus NewStatus { get; set; }
        
        public string? CertificateId { get; set; }
    }
}
