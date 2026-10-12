using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Entities
{
    public class Certification
    {
        public ObjectId Id { get; set; }

        public string CaregiverId { get; set; } = string.Empty;

        public string CertificateName { get; set; } = string.Empty;

        public string CertificateIssuer { get; set; } = string.Empty;

        /// <summary>
        /// "educational", "professional", "medical", "specialized"
        /// </summary>
        public string? CertificateCategory { get; set; }

        /// <summary>
        /// Which service categories this certificate satisfies.
        /// e.g. ["MedicalSupport", "PostSurgeryCare"]
        /// </summary>
        public List<string>? ServiceCategories { get; set; }

        /// <summary>
        /// Expiry date for certificates that expire (nursing license, CPR, etc.)
        /// Null for certificates that don't expire.
        /// </summary>
        public DateTime? ExpiryDate { get; set; }

        public string? CloudinaryUrl { get; set; } = string.Empty;

        public string? CloudinaryPublicId { get; set; } = string.Empty;

        public bool IsVerified { get; set; }

        public DocumentVerificationStatus? VerificationStatus { get; set; }

        public DateTime? VerificationDate { get; set; }

        public string? DojahVerificationResponse { get; set; }

        public decimal? VerificationConfidence { get; set; }

        public string? ExtractedCertificateInfo { get; set; }

        public int? VerificationAttempts { get; set; }

        public DateTime YearObtained { get; set; }

        public DateTime SubmittedOn { get; set; }

        // Admin review fields
        public string? ReviewedByAdminId { get; set; }

        public DateTime? ReviewedAt { get; set; }

        public string? AdminReviewNotes { get; set; }

        public string? ValidationIssues { get; set; }

        /// <summary>
        /// Set when a rejected certificate has been replaced by a newer upload of the same type. The record is kept as
        /// history; it no longer counts for eligibility, the caregiver's list, or the duplicate check. Null = current.
        /// </summary>
        public DateTime? SupersededAt { get; set; }

        /// <summary>Id of the certificate that replaced this one.</summary>
        public string? SupersededByCertificateId { get; set; }
    }

    public enum DocumentVerificationStatus
    {
        PendingVerification = 0,
        Verified = 1,
        Invalid = 2,
        VerificationFailed = 3,
        ManualReviewRequired = 4,
        NotVerified = 5
    }
}
