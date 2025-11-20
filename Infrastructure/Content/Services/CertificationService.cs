using Application.DTOs;
using Application.Interfaces.Content;
using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Infrastructure.Content.Services
{
    public class CertificationService : ICertificationService
    {
        private readonly CareProDbContext careProDbContext;
        private readonly ICareGiverService careGiverService;
        private readonly ILogger<CertificationService> logger;
        private readonly CloudinaryService cloudinaryService;
        private readonly DojahDocumentVerificationService dojahVerificationService;

        public CertificationService(
            CareProDbContext careProDbContext, 
            ICareGiverService careGiverService, 
            ILogger<CertificationService> logger,
            CloudinaryService cloudinaryService,
            DojahDocumentVerificationService dojahVerificationService)
        {
            this.careProDbContext = careProDbContext;
            this.careGiverService = careGiverService;
            this.logger = logger;
            this.cloudinaryService = cloudinaryService;
            this.dojahVerificationService = dojahVerificationService;
        }

        public async Task<CertificationUploadResponse> CreateCertificateAsync(AddCertificationRequest addCertificationRequest)
        {
            if (string.IsNullOrWhiteSpace(addCertificationRequest?.CaregiverId))
                throw new ArgumentException("CaregiverId is required");

            if (string.IsNullOrWhiteSpace(addCertificationRequest.Certificate))
                throw new ArgumentException("Certificate data is required");

            var careGiver = await careGiverService.GetCaregiverUserAsync(addCertificationRequest.CaregiverId);
            if (careGiver == null)
            {
                throw new KeyNotFoundException("The CaregiverID entered is not a Valid ID");
            }

            try
            {
                // Convert base64 to byte array
                var certificateBytes = Convert.FromBase64String(addCertificationRequest.Certificate);
                
                // Upload to Cloudinary
                var fileName = $"certificate_{addCertificationRequest.CaregiverId}_{DateTime.UtcNow:yyyyMMddHHmmss}";
                var (cloudinaryUrl, publicId) = await cloudinaryService.UploadCertificateAsync(certificateBytes, fileName);

                // Create initial certification entity
                var certification = new Certification
                {
                    CertificateName = addCertificationRequest.CertificateName ?? "",
                    CertificateIssuer = addCertificationRequest.CertificateIssuer ?? "",
                    CloudinaryUrl = cloudinaryUrl,
                    CloudinaryPublicId = publicId,
                    YearObtained = addCertificationRequest.YearObtained,
                    CaregiverId = addCertificationRequest.CaregiverId,
                    Id = ObjectId.GenerateNewId(),
                    IsVerified = false,
                    VerificationStatus = DocumentVerificationStatus.PendingVerification,
                    SubmittedOn = DateTime.UtcNow,
                    VerificationAttempts = 0
                };

                // Perform document verification if requested
                VerificationResultDTO? verificationResult = null;
                if (addCertificationRequest.VerifyImmediately)
                {
                    try
                    {
                        var dojahResult = await dojahVerificationService.VerifyDocumentAsync(certificateBytes, fileName);
                        
                        // Update certification with verification results
                        certification.VerificationStatus = dojahResult.Status;
                        certification.VerificationDate = DateTime.UtcNow;
                        certification.VerificationConfidence = dojahResult.Confidence;
                        certification.DojahVerificationResponse = dojahResult.RawResponse;
                        certification.VerificationAttempts = 1;
                        certification.IsVerified = dojahResult.Status == DocumentVerificationStatus.Verified;
                        
                        // Store extracted information as JSON
                        if (dojahResult.ExtractedInfo != null)
                        {
                            certification.ExtractedCertificateInfo = JsonSerializer.Serialize(dojahResult.ExtractedInfo);
                        }

                        verificationResult = new VerificationResultDTO
                        {
                            Status = dojahResult.Status,
                            Confidence = dojahResult.Confidence,
                            VerifiedAt = DateTime.UtcNow,
                            ErrorMessage = dojahResult.ErrorMessage,
                            ExtractedInfo = dojahResult.ExtractedInfo != null ? MapExtractedInfo(dojahResult.ExtractedInfo) : null
                        };

                        LogAuditEvent($"Certificate verification completed. Status: {dojahResult.Status}, Confidence: {dojahResult.Confidence}", 
                            addCertificationRequest.CaregiverId);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Certificate verification failed for caregiver {CaregiverId}", addCertificationRequest.CaregiverId);
                        
                        certification.VerificationStatus = DocumentVerificationStatus.VerificationFailed;
                        certification.VerificationAttempts = 1;
                        
                        verificationResult = new VerificationResultDTO
                        {
                            Status = DocumentVerificationStatus.VerificationFailed,
                            Confidence = 0,
                            ErrorMessage = "Verification service unavailable"
                        };
                    }
                }

                // Save to database
                await careProDbContext.Certifications.AddAsync(certification);
                await careProDbContext.SaveChangesAsync();

                LogAuditEvent($"Certificate uploaded and saved. ID: {certification.Id}", addCertificationRequest.CaregiverId);

                return new CertificationUploadResponse
                {
                    CertificateId = certification.Id.ToString(),
                    UploadStatus = "success",
                    CertificateUrl = cloudinaryUrl,
                    Verification = verificationResult
                };
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error creating certificate for caregiver {CaregiverId}", addCertificationRequest.CaregiverId);
                throw;
            }
        }

        public async Task<IEnumerable<CertificationResponse>> GetAllCaregiverCertificateAsync(string caregiverId)
        {
            var certificates = new List<Certification>();
            
            try
            {
                // Handle potential schema migration issues with legacy documents
                certificates = await careProDbContext.Certifications
                    .Where(x => x.CaregiverId == caregiverId)
                    .OrderBy(x => x.SubmittedOn)
                    .ToListAsync();
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Document element is missing"))
            {
                logger.LogWarning(ex, "Legacy certificate documents found with missing schema fields for caregiver {CaregiverId}. Attempting manual cleanup.", caregiverId);
                
                // Try to handle legacy documents manually
                try
                {
                    await HandleLegacyDocumentsAsync(caregiverId);
                    // After cleanup, return empty list since legacy documents are incompatible
                    return new List<CertificationResponse>();
                }
                catch (Exception cleanupEx)
                {
                    logger.LogError(cleanupEx, "Failed to handle legacy documents for caregiver {CaregiverId}", caregiverId);
                    throw new ApplicationException($"Unable to retrieve certificates due to legacy data schema issues. Please contact support to migrate your certificate data.", cleanupEx);
                }
            }

            var certificatesDTOs = new List<CertificationResponse>();

            foreach (var certificate in certificates)
            {
                var certificateDTO = new CertificationResponse()
                {
                    Id = certificate.Id.ToString(),
                    CaregiverId = certificate.CaregiverId,
                    CertificateName = certificate.CertificateName,
                    CertificateIssuer = certificate.CertificateIssuer,
                    CertificateUrl = certificate.CloudinaryUrl ?? string.Empty,
                    YearObtained = certificate.YearObtained,
                    IsVerified = certificate.IsVerified,
                    VerificationStatus = certificate.VerificationStatus ?? DocumentVerificationStatus.PendingVerification,
                    VerificationDate = certificate.VerificationDate,
                    VerificationConfidence = certificate.VerificationConfidence,
                    SubmittedOn = certificate.SubmittedOn,
                    ExtractedInfo = certificate.ExtractedCertificateInfo != null ? ParseExtractedInfo(certificate.ExtractedCertificateInfo) : null
                };
                
                certificatesDTOs.Add(certificateDTO);
            }

            return certificatesDTOs;
        }

        public async Task<CertificationResponse> GetCertificateAsync(string certificateId)
        {
            var certificate = await careProDbContext.Certifications.FirstOrDefaultAsync(x => x.Id.ToString() == certificateId);

            if (certificate == null)
            {
                throw new KeyNotFoundException($"Certificate with ID '{certificateId}' not found.");
            }

            var certificateDTO = new CertificationResponse()
            {
                Id = certificate.Id.ToString(),
                CaregiverId = certificate.CaregiverId,
                CertificateName = certificate.CertificateName,
                CertificateIssuer = certificate.CertificateIssuer,
                CertificateUrl = certificate.CloudinaryUrl ?? string.Empty,
                YearObtained = certificate.YearObtained,
                IsVerified = certificate.IsVerified,
                VerificationStatus = certificate.VerificationStatus ?? DocumentVerificationStatus.PendingVerification,
                VerificationDate = certificate.VerificationDate,
                VerificationConfidence = certificate.VerificationConfidence,
                SubmittedOn = certificate.SubmittedOn,
                ExtractedInfo = certificate.ExtractedCertificateInfo != null ? ParseExtractedInfo(certificate.ExtractedCertificateInfo) : null
            };

            return certificateDTO;
        }

        public async Task<VerificationResultDTO> RetryVerificationAsync(string certificateId)
        {
            var certificate = await careProDbContext.Certifications.FirstOrDefaultAsync(x => x.Id.ToString() == certificateId);

            if (certificate == null)
            {
                throw new KeyNotFoundException($"Certificate with ID '{certificateId}' not found.");
            }

            try
            {
                // Download the image from Cloudinary
                using var httpClient = new HttpClient();
                var imageBytes = await httpClient.GetByteArrayAsync(certificate.CloudinaryUrl);

                // Perform verification
                var dojahResult = await dojahVerificationService.VerifyDocumentAsync(imageBytes, $"retry_{certificateId}");

                // Update certificate with new verification results
                certificate.VerificationStatus = dojahResult.Status;
                certificate.VerificationDate = DateTime.UtcNow;
                certificate.VerificationConfidence = dojahResult.Confidence;
                certificate.DojahVerificationResponse = dojahResult.RawResponse;
                certificate.VerificationAttempts += 1;
                certificate.IsVerified = dojahResult.Status == DocumentVerificationStatus.Verified;

                if (dojahResult.ExtractedInfo != null)
                {
                    certificate.ExtractedCertificateInfo = JsonSerializer.Serialize(dojahResult.ExtractedInfo);
                }

                await careProDbContext.SaveChangesAsync();

                LogAuditEvent($"Certificate verification retry completed. Status: {dojahResult.Status}", certificate.CaregiverId);

                return new VerificationResultDTO
                {
                    Status = dojahResult.Status,
                    Confidence = dojahResult.Confidence,
                    VerifiedAt = DateTime.UtcNow,
                    ErrorMessage = dojahResult.ErrorMessage,
                    ExtractedInfo = dojahResult.ExtractedInfo != null ? MapExtractedInfo(dojahResult.ExtractedInfo) : null
                };
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error retrying verification for certificate {CertificateId}", certificateId);
                
                certificate.VerificationAttempts = (certificate.VerificationAttempts ?? 0) + 1;
                await careProDbContext.SaveChangesAsync();
                
                throw;
            }
        }

        public async Task DeleteCertificateAsync(string certificateId)
        {
            var certificate = await careProDbContext.Certifications.FirstOrDefaultAsync(x => x.Id.ToString() == certificateId);

            if (certificate == null)
            {
                throw new KeyNotFoundException($"Certificate with ID '{certificateId}' not found.");
            }

            try
            {
                // Delete from Cloudinary if CloudinaryPublicId exists
                if (!string.IsNullOrEmpty(certificate.CloudinaryPublicId))
                {
                    await cloudinaryService.DeleteFileAsync(certificate.CloudinaryPublicId);
                    logger.LogInformation($"Deleted certificate image from Cloudinary: {certificate.CloudinaryPublicId}");
                }

                // Delete from database
                careProDbContext.Certifications.Remove(certificate);
                await careProDbContext.SaveChangesAsync();

                LogAuditEvent($"Certificate deleted. ID: {certificateId}", certificate.CaregiverId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error deleting certificate {CertificateId}", certificateId);
                throw;
            }
        }

        public async Task DeleteAllCaregiverCertificatesAsync(string caregiverId)
        {
            try
            {
                // Get all certificates for the caregiver, but handle schema issues
                var certificates = new List<Certification>();
                
                try
                {
                    // Try normal EF approach first
                    certificates = await careProDbContext.Certifications
                        .Where(x => x.CaregiverId == caregiverId)
                        .ToListAsync();
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("Document element is missing"))
                {
                    logger.LogWarning(ex, "Schema mismatch detected. Cannot delete certificates using EF for caregiver {CaregiverId}", caregiverId);
                    throw new ApplicationException($"Cannot delete certificates due to legacy data schema issues. Manual database cleanup required for caregiver {caregiverId}.", ex);
                }

                if (!certificates.Any())
                {
                    logger.LogInformation($"No certificates found for caregiver {caregiverId}");
                    return;
                }

                // Delete from Cloudinary if applicable
                foreach (var certificate in certificates)
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(certificate.CloudinaryPublicId))
                        {
                            await cloudinaryService.DeleteFileAsync(certificate.CloudinaryPublicId);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to delete image from Cloudinary for certificate {CertificateId}", certificate.Id);
                    }
                }

                // Delete from database
                careProDbContext.Certifications.RemoveRange(certificates);
                await careProDbContext.SaveChangesAsync();

                LogAuditEvent($"Deleted {certificates.Count} certificates for caregiver", caregiverId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error deleting all certificates for caregiver {CaregiverId}", caregiverId);
                throw;
            }
        }

        private CertificateExtractedInfoDTO? MapExtractedInfo(Infrastructure.Content.Services.CertificateExtractedInfo? extractedInfo)
        {
            if (extractedInfo == null) return null;

            return new CertificateExtractedInfoDTO
            {
                HolderName = !string.IsNullOrWhiteSpace(extractedInfo.FullName) 
                    ? extractedInfo.FullName 
                    : $"{extractedInfo.FirstName} {extractedInfo.LastName}".Trim(),
                DocumentNumber = extractedInfo.DocumentNumber,
                ExpiryDate = extractedInfo.ExpiryDate,
                IssueDate = extractedInfo.IssueDate
            };
        }

        private CertificateExtractedInfoDTO? ParseExtractedInfo(string extractedInfoJson)
        {
            if (string.IsNullOrWhiteSpace(extractedInfoJson)) return null;

            try
            {
                var extractedInfo = JsonSerializer.Deserialize<Infrastructure.Content.Services.CertificateExtractedInfo>(extractedInfoJson);
                return extractedInfo != null ? MapExtractedInfo(extractedInfo) : null;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to parse extracted certificate information");
                return null;
            }
        }

        private async Task HandleLegacyDocumentsAsync(string caregiverId)
        {
            try
            {
                // Use raw MongoDB operations to delete problematic documents
                logger.LogInformation("Attempting to clean up legacy certificate documents for caregiver {CaregiverId}", caregiverId);
                
                // This is a placeholder - in production you might want to use MongoDB driver directly
                // For now, we'll just log that manual cleanup is needed
                logger.LogWarning("Legacy document cleanup requires manual intervention for caregiver {CaregiverId}", caregiverId);
                
                throw new NotSupportedException("Legacy document cleanup not yet implemented. Please use database tools to remove incompatible documents.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to handle legacy documents for caregiver {CaregiverId}", caregiverId);
                throw;
            }
        }

        private void LogException(Exception ex)
        {
            logger.LogError(ex, "Exception occurred");
        }

        private void LogAuditEvent(object message, string? caregiverId)
        {
            logger.LogInformation($"Audit Event: {message}. User ID: {caregiverId}. Timestamp: {DateTime.UtcNow}");
        }
    }
}
