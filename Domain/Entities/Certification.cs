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
