using System;
using System.Collections.Generic;
using System.Linq;
using Domain.Entities;

namespace Infrastructure.Content.Helpers
{
    public static class ApprovedCertificates
    {
        // Educational certificates
        public const string WASSCE = "West African Senior School Certificate Examination (WASSCE)";
        public const string NECO = "National Examination Council (NECO) Senior School Certificate Examination (SSCE)";
        public const string NABTEB = "National Business and Technical Examinations Board (NABTEB)";
        public const string NYSC = "National Youth Service Corps (NYSC) Certificate";

        // Professional / Medical / Specialized certificates
        public const string NURSING_LICENSE = "Nursing License (RN/LPN/CNA)";
        public const string CPR_CERTIFICATION = "CPR Certification";
        public const string FIRST_AID = "First Aid Training Certificate";
        public const string SPECIAL_NEEDS_TRAINING = "Special Needs Training Certificate";
        public const string DEMENTIA_CARE = "Dementia Care Certificate";
        public const string MEDICATION_ADMIN = "Medication Administration License";
        public const string PALLIATIVE_CARE_TRAINING = "Palliative Care Training Certificate";
        public const string PHYSICAL_THERAPY_ASSISTANT = "Physical Therapy Assistant Certificate";
        public const string HOME_HEALTH_AIDE = "Home Health Aide (HHA) Certificate";

        public static readonly Dictionary<string, string> CertificateIssuerMapping = new()
        {
            // Educational
            { WASSCE, "West African Examinations Council (WAEC)" },
            { NECO, "National Examination Council (NECO)" },
            { NABTEB, "National Business and Technical Examinations Board (NABTEB)" },
            { NYSC, "National Youth Service Corps (NYSC)" },

            // Professional / Medical / Specialized — accept broad issuers
            { NURSING_LICENSE, "Nursing and Midwifery Council of Nigeria (NMCN)" },
            { CPR_CERTIFICATION, "Accredited CPR Training Provider" },
            { FIRST_AID, "Accredited First Aid Training Provider" },
            { SPECIAL_NEEDS_TRAINING, "Accredited Special Needs Training Institution" },
            { DEMENTIA_CARE, "Accredited Dementia Care Training Institution" },
            { MEDICATION_ADMIN, "Relevant Health Authority" },
            { PALLIATIVE_CARE_TRAINING, "Accredited Palliative Care Training Institution" },
            { PHYSICAL_THERAPY_ASSISTANT, "Accredited Physical Therapy Training Institution" },
            { HOME_HEALTH_AIDE, "Accredited Home Health Aide Training Provider" }
        };

        public static readonly List<string> ValidCertificateNames = new()
        {
            WASSCE, NECO, NABTEB, NYSC,
            NURSING_LICENSE, CPR_CERTIFICATION, FIRST_AID,
            SPECIAL_NEEDS_TRAINING, DEMENTIA_CARE, MEDICATION_ADMIN,
            PALLIATIVE_CARE_TRAINING, PHYSICAL_THERAPY_ASSISTANT, HOME_HEALTH_AIDE
        };

        /// <summary>
        /// Maps certificate names to their category: "educational", "professional", "medical", "specialized"
        /// </summary>
        public static readonly Dictionary<string, string> CertificateCategoryMapping = new()
        {
            { WASSCE, "educational" },
            { NECO, "educational" },
            { NABTEB, "educational" },
            { NYSC, "educational" },
            { NURSING_LICENSE, "medical" },
            { CPR_CERTIFICATION, "medical" },
            { FIRST_AID, "medical" },
            { MEDICATION_ADMIN, "medical" },
            { SPECIAL_NEEDS_TRAINING, "specialized" },
            { DEMENTIA_CARE, "specialized" },
            { PALLIATIVE_CARE_TRAINING, "specialized" },
            { PHYSICAL_THERAPY_ASSISTANT, "professional" },
            { HOME_HEALTH_AIDE, "professional" }
        };

        /// <summary>
        /// Maps certificate names to the service categories they help satisfy.
        /// </summary>
        public static readonly Dictionary<string, List<string>> CertificateServiceCategoryMapping = new()
        {
            { NURSING_LICENSE, new() { "MedicalSupport", "PostSurgeryCare", "PalliativeCare" } },
            { CPR_CERTIFICATION, new() { "MedicalSupport", "PostSurgeryCare", "TherapyAndWellness" } },
            { FIRST_AID, new() { "MedicalSupport", "PostSurgeryCare" } },
            { SPECIAL_NEEDS_TRAINING, new() { "SpecialNeedsCare" } },
            { DEMENTIA_CARE, new() { "SpecialNeedsCare", "PalliativeCare" } },
            { MEDICATION_ADMIN, new() { "MedicalSupport", "PostSurgeryCare", "PalliativeCare" } },
            { PALLIATIVE_CARE_TRAINING, new() { "PalliativeCare" } },
            { PHYSICAL_THERAPY_ASSISTANT, new() { "TherapyAndWellness" } },
            { HOME_HEALTH_AIDE, new() { "MedicalSupport", "PostSurgeryCare" } }
        };

        /// <summary>
        /// Professional/medical/specialized certificates may accept flexible issuers.
        /// Educational certificates require exact issuer matching.
        /// </summary>
        public static readonly HashSet<string> FlexibleIssuerCertificates = new()
        {
            NURSING_LICENSE, CPR_CERTIFICATION, FIRST_AID,
            SPECIAL_NEEDS_TRAINING, DEMENTIA_CARE, MEDICATION_ADMIN,
            PALLIATIVE_CARE_TRAINING, PHYSICAL_THERAPY_ASSISTANT, HOME_HEALTH_AIDE
        };

        public static string? GetCertificateCategory(string? certificateName)
        {
            if (string.IsNullOrWhiteSpace(certificateName)) return null;
            return CertificateCategoryMapping.TryGetValue(certificateName.Trim(), out var category) ? category : null;
        }

        public static List<string>? GetServiceCategories(string? certificateName)
        {
            if (string.IsNullOrWhiteSpace(certificateName)) return null;
            return CertificateServiceCategoryMapping.TryGetValue(certificateName.Trim(), out var categories) ? categories : null;
        }
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

            // Professional/medical/specialized certificates accept any non-empty issuer
            if (ApprovedCertificates.FlexibleIssuerCertificates.Contains(certificateName.Trim()))
                return true;

            // Educational certificates require exact issuer match
            var expectedIssuer = GetExpectedIssuer(certificateName);
            
            if (expectedIssuer == null)
                return false;

            return string.Equals(expectedIssuer, certificateIssuer.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        public static string GetValidationErrorMessage(string certificateName, string? certificateIssuer)
        {
            if (!IsValidCertificateType(certificateName))
            {
                return "Invalid certificate type. Accepted certificates include: " +
                    string.Join(", ", ApprovedCertificates.ValidCertificateNames.Select(n => $"'{n}'"));
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
