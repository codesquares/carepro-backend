using System;
using System.Collections.Generic;
using System.Linq;
using Domain.Entities;

namespace Infrastructure.Content.Helpers
{
    public static class ApprovedCertificates
    {
        public const string WASSCE = "West African Senior School Certificate Examination (WASSCE)";
        public const string NECO = "National Examination Council (NECO) Senior School Certificate Examination (SSCE)";
        public const string NABTEB = "National Business and Technical Examinations Board (NABTEB)";
        public const string NYSC = "National Youth Service Corps (NYSC) Certificate";

        public static readonly Dictionary<string, string> CertificateIssuerMapping = new()
        {
            { WASSCE, "West African Examinations Council (WAEC)" },
            { NECO, "National Examination Council (NECO)" },
            { NABTEB, "National Business and Technical Examinations Board (NABTEB)" },
            { NYSC, "National Youth Service Corps (NYSC)" }
        };

        public static readonly List<string> ValidCertificateNames = new()
        {
            WASSCE,
            NECO,
            NABTEB,
            NYSC
        };
    }

    public static class CertificateValidationHelper
    {
        public static bool IsValidCertificateType(string? certificateName)
        {
            if (string.IsNullOrWhiteSpace(certificateName))
                return false;
                
            return ApprovedCertificates.ValidCertificateNames.Contains(certificateName.Trim());
        }

        public static string? GetExpectedIssuer(string certificateName)
        {
            if (string.IsNullOrWhiteSpace(certificateName))
                return null;

            return ApprovedCertificates.CertificateIssuerMapping.TryGetValue(certificateName.Trim(), out var issuer) 
                ? issuer 
                : null;
        }

        public static bool ValidateIssuerMatch(string? certificateName, string? certificateIssuer)
        {
            if (string.IsNullOrWhiteSpace(certificateName) || string.IsNullOrWhiteSpace(certificateIssuer))
                return false;
                
            var expectedIssuer = GetExpectedIssuer(certificateName);
            
            if (expectedIssuer == null)
                return false;

            return string.Equals(expectedIssuer, certificateIssuer.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        public static string GetValidationErrorMessage(string certificateName, string? certificateIssuer)
        {
            if (!IsValidCertificateType(certificateName))
            {
                return "Invalid certificate type. Only WASSCE, NECO SSCE, NABTEB, and NYSC certificates are accepted.";
            }

            if (!ValidateIssuerMatch(certificateName, certificateIssuer ?? ""))
            {
                var expectedIssuer = GetExpectedIssuer(certificateName);
                return $"Invalid certificate issuer. Expected issuer for this certificate type is: {expectedIssuer}";
            }

            return string.Empty;
        }

        /// <summary>
        /// Checks if extracted name from certificate matches caregiver's profile name.
        /// Uses fuzzy matching to allow minor variations (typos, middle names, order).
        /// </summary>
        public static bool ValidateNameMatch(string? extractedFirstName, string? extractedLastName, 
                                            string? caregiverFirstName, string? caregiverLastName)
        {
            if (string.IsNullOrWhiteSpace(extractedFirstName) || string.IsNullOrWhiteSpace(extractedLastName))
                return false; // Cannot validate if Dojah didn't extract names

            if (string.IsNullOrWhiteSpace(caregiverFirstName) || string.IsNullOrWhiteSpace(caregiverLastName))
                return false;

            var extractedFirst = NormalizeName(extractedFirstName);
            var extractedLast = NormalizeName(extractedLastName);
            var caregiverFirst = NormalizeName(caregiverFirstName);
            var caregiverLast = NormalizeName(caregiverLastName);

            // Exact match (case-insensitive)
            if ((extractedFirst == caregiverFirst && extractedLast == caregiverLast) ||
                (extractedFirst == caregiverLast && extractedLast == caregiverFirst)) // Handle name order swaps
            {
                return true;
            }

            // Check if names contain each other (handles middle names, compound names)
            if ((caregiverFirst.Contains(extractedFirst) || extractedFirst.Contains(caregiverFirst)) &&
                (caregiverLast.Contains(extractedLast) || extractedLast.Contains(caregiverLast)))
            {
                return true;
            }

            // Calculate Levenshtein distance for fuzzy matching (typos)
            var firstNameSimilarity = CalculateSimilarity(extractedFirst, caregiverFirst);
            var lastNameSimilarity = CalculateSimilarity(extractedLast, caregiverLast);

            // Allow if both names are at least 80% similar
            return firstNameSimilarity >= 0.8 && lastNameSimilarity >= 0.8;
        }

        /// <summary>
        /// Validates if confidence score meets minimum threshold for auto-approval.
        /// Returns status: Verified (>= 0.7), ManualReviewRequired (0.5-0.7), Invalid (< 0.5)
        /// </summary>
        public static (bool IsValid, DocumentVerificationStatus Status, string Message) ValidateConfidenceThreshold(decimal confidence)
        {
            if (confidence >= 0.7m)
            {
                return (true, DocumentVerificationStatus.Verified, "Certificate verification confidence meets threshold for auto-approval.");
            }
            else if (confidence >= 0.5m)
            {
                return (false, DocumentVerificationStatus.ManualReviewRequired, 
                       $"Certificate verification confidence ({confidence:P0}) requires manual review. Confidence must be at least 70% for auto-approval.");
            }
            else
            {
                return (false, DocumentVerificationStatus.Invalid, 
                       $"Certificate verification confidence ({confidence:P0}) is too low. Minimum acceptable confidence is 50%.");
            }
        }

        /// <summary>
        /// Validates that document country is Nigeria for Nigerian educational certificates.
        /// </summary>
        public static bool ValidateCountryCode(string? countryCode)
        {
            if (string.IsNullOrWhiteSpace(countryCode))
                return false;

            return string.Equals(countryCode.Trim(), "NG", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(countryCode.Trim(), "NGA", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(countryCode.Trim(), "Nigeria", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Cross-validates that Dojah detected document type matches caregiver's claimed certificate type.
        /// </summary>
        public static bool ValidateDocumentTypeMatch(string? claimedCertificateType, string? dojahDetectedDocumentName)
        {
            if (string.IsNullOrWhiteSpace(dojahDetectedDocumentName))
                return true; // Cannot validate if Dojah didn't detect document type, allow it

            if (string.IsNullOrWhiteSpace(claimedCertificateType))
                return false;

            var claimed = NormalizeName(claimedCertificateType);
            var detected = NormalizeName(dojahDetectedDocumentName);

            // Check for key certificate identifiers in detected document name
            if (claimed.Contains("wassce") || claimed.Contains("waec"))
            {
                return detected.Contains("waec") || detected.Contains("wassce") || 
                       detected.Contains("school") || detected.Contains("certificate");
            }

            if (claimed.Contains("neco"))
            {
                return detected.Contains("neco") || detected.Contains("school") || 
                       detected.Contains("certificate");
            }

            if (claimed.Contains("nabteb"))
            {
                return detected.Contains("nabteb") || detected.Contains("technical") || 
                       detected.Contains("certificate");
            }

            if (claimed.Contains("nysc"))
            {
                return detected.Contains("nysc") || detected.Contains("service") || 
                       detected.Contains("corps") || detected.Contains("certificate");
            }

            // If we can't determine, allow it (conservative approach)
            return true;
        }

        /// <summary>
        /// Validates that issue date is in the past and within reasonable range.
        /// Does NOT check expiry dates as most Nigerian educational certificates don't expire.
        /// </summary>
        public static (bool IsValid, string Message) ValidateIssueDate(DateTime? issueDate)
        {
            if (!issueDate.HasValue)
                return (true, string.Empty); // No issue date provided, skip validation

            var now = DateTime.UtcNow;
            var minValidYear = 1960; // Reasonable minimum year for Nigerian certificates

            if (issueDate.Value > now)
            {
                return (false, "Certificate issue date cannot be in the future.");
            }

            if (issueDate.Value.Year < minValidYear)
            {
                return (false, $"Certificate issue date must be after {minValidYear}.");
            }

            return (true, string.Empty);
        }

        // Helper methods

        private static string NormalizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            return name.Trim()
                      .ToLowerInvariant()
                      .Replace("-", "")
                      .Replace("'", "")
                      .Replace(".", "")
                      .Replace(",", "")
                      .Replace("  ", " "); // Remove double spaces
        }

        private static double CalculateSimilarity(string source, string target)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target))
                return 0;

            if (source == target)
                return 1;

            var distance = LevenshteinDistance(source, target);
            var maxLength = Math.Max(source.Length, target.Length);
            
            return 1.0 - ((double)distance / maxLength);
        }

        private static int LevenshteinDistance(string source, string target)
        {
            if (string.IsNullOrEmpty(source))
                return string.IsNullOrEmpty(target) ? 0 : target.Length;

            if (string.IsNullOrEmpty(target))
                return source.Length;

            var sourceLength = source.Length;
            var targetLength = target.Length;
            var distance = new int[sourceLength + 1, targetLength + 1];

            for (var i = 0; i <= sourceLength; distance[i, 0] = i++) { }
            for (var j = 0; j <= targetLength; distance[0, j] = j++) { }

            for (var i = 1; i <= sourceLength; i++)
            {
                for (var j = 1; j <= targetLength; j++)
                {
                    var cost = (target[j - 1] == source[i - 1]) ? 0 : 1;
                    distance[i, j] = Math.Min(
                        Math.Min(distance[i - 1, j] + 1, distance[i, j - 1] + 1),
                        distance[i - 1, j - 1] + cost);
                }
            }

            return distance[sourceLength, targetLength];
        }
    }
}
