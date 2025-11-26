using Application.DTOs;
using Application.Interfaces.Content;
using Application.Interfaces.Email;
using Domain.Entities;
using Infrastructure.Content.Data;
using Infrastructure.Content.Helpers;
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
        private readonly INotificationService notificationService;
        private readonly IEmailService emailService;

        public CertificationService(
            CareProDbContext careProDbContext, 
            ICareGiverService careGiverService, 
            ILogger<CertificationService> logger,
            CloudinaryService cloudinaryService,
            DojahDocumentVerificationService dojahVerificationService,
            INotificationService notificationService,
            IEmailService emailService)
        {
            this.careProDbContext = careProDbContext;
            this.careGiverService = careGiverService;
            this.logger = logger;
            this.cloudinaryService = cloudinaryService;
            this.dojahVerificationService = dojahVerificationService;
            this.notificationService = notificationService;
            this.emailService = emailService;
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

            // Validate certificate type
            if (!CertificateValidationHelper.IsValidCertificateType(addCertificationRequest.CertificateName))
            {
                throw new ArgumentException("Invalid certificate type. Only WASSCE, NECO SSCE, NABTEB, and NYSC certificates are accepted.", nameof(addCertificationRequest.CertificateName));
            }

            // Validate certificate issuer matches the certificate type
            if (!CertificateValidationHelper.ValidateIssuerMatch(addCertificationRequest.CertificateName, addCertificationRequest.CertificateIssuer))
            {
                var expectedIssuer = CertificateValidationHelper.GetExpectedIssuer(addCertificationRequest.CertificateName ?? "");
                throw new ArgumentException($"Invalid certificate issuer. Expected issuer for this certificate type is: {expectedIssuer}", nameof(addCertificationRequest.CertificateIssuer));
            }

            // Check for duplicate certificate type
            var existingCertificate = await careProDbContext.Certifications
                .FirstOrDefaultAsync(c => c.CaregiverId == addCertificationRequest.CaregiverId 
                                       && c.CertificateName == addCertificationRequest.CertificateName);
            
            if (existingCertificate != null)
            {
                throw new InvalidOperationException($"A certificate of type '{addCertificationRequest.CertificateName}' has already been uploaded for this caregiver.");
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
                        
                        // Enhanced validation checks for certificate genuineness
                        var validationMessages = new List<string>();
                        var finalStatus = dojahResult.Status;
                        var shouldReject = false;

                        // 1. Confidence threshold validation
                        var (isConfidentValid, confidenceStatus, confidenceMessage) = 
                            CertificateValidationHelper.ValidateConfidenceThreshold(dojahResult.Confidence);
                        
                        if (!isConfidentValid)
                        {
                            validationMessages.Add(confidenceMessage);
                            finalStatus = confidenceStatus;
                            
                            if (confidenceStatus == DocumentVerificationStatus.Invalid)
                            {
                                shouldReject = true;
                            }
                        }

                        // 2. Name matching validation (only if Dojah extracted names)
                        if (dojahResult.ExtractedInfo != null && 
                            !string.IsNullOrWhiteSpace(dojahResult.ExtractedInfo.FirstName) &&
                            !string.IsNullOrWhiteSpace(dojahResult.ExtractedInfo.LastName))
                        {
                            var namesMatch = CertificateValidationHelper.ValidateNameMatch(
                                dojahResult.ExtractedInfo.FirstName,
                                dojahResult.ExtractedInfo.LastName,
                                careGiver.FirstName,
                                careGiver.LastName
                            );

                            if (!namesMatch)
                            {
                                validationMessages.Add(
                                    $"Name mismatch: Certificate shows '{dojahResult.ExtractedInfo.FirstName} {dojahResult.ExtractedInfo.LastName}' " +
                                    $"but profile shows '{careGiver.FirstName} {careGiver.LastName}'. Manual review required."
                                );
                                finalStatus = DocumentVerificationStatus.ManualReviewRequired;
                            }
                        }

                        // 3. Country validation
                        if (dojahResult.DocumentType != null && 
                            !string.IsNullOrWhiteSpace(dojahResult.DocumentType.CountryCode))
                        {
                            var isValidCountry = CertificateValidationHelper.ValidateCountryCode(
                                dojahResult.DocumentType.CountryCode
                            );

                            if (!isValidCountry)
                            {
                                validationMessages.Add(
                                    $"Invalid certificate country: Expected Nigeria (NG) but detected '{dojahResult.DocumentType.CountryCode}'. " +
                                    $"Only Nigerian educational certificates are accepted."
                                );
                                finalStatus = DocumentVerificationStatus.Invalid;
                                shouldReject = true;
                            }
                        }

                        // 4. Document type cross-validation
                        if (dojahResult.DocumentType != null && 
                            !string.IsNullOrWhiteSpace(dojahResult.DocumentType.DocumentName))
                        {
                            var typeMatches = CertificateValidationHelper.ValidateDocumentTypeMatch(
                                addCertificationRequest.CertificateName,
                                dojahResult.DocumentType.DocumentName
                            );

                            if (!typeMatches)
                            {
                                validationMessages.Add(
                                    $"Document type mismatch: You claimed '{addCertificationRequest.CertificateName}' " +
                                    $"but Dojah detected '{dojahResult.DocumentType.DocumentName}'. This may indicate a forged certificate."
                                );
                                finalStatus = DocumentVerificationStatus.Invalid;
                                shouldReject = true;
                            }
                        }

                        // 5. Issue date validation (if extracted)
                        if (dojahResult.ExtractedInfo?.IssueDate != null)
                        {
                            var (isDateValid, dateMessage) = CertificateValidationHelper.ValidateIssueDate(
                                dojahResult.ExtractedInfo.IssueDate
                            );

                            if (!isDateValid)
                            {
                                validationMessages.Add(dateMessage);
                                finalStatus = DocumentVerificationStatus.Invalid;
                                shouldReject = true;
                            }
                        }

                        // Update certification with verification results
                        certification.VerificationStatus = finalStatus;
                        certification.VerificationDate = DateTime.UtcNow;
                        certification.VerificationConfidence = dojahResult.Confidence;
                        certification.DojahVerificationResponse = dojahResult.RawResponse;
                        certification.VerificationAttempts = 1;
                        certification.IsVerified = finalStatus == DocumentVerificationStatus.Verified;
                        
                        // Store extracted information as JSON
                        if (dojahResult.ExtractedInfo != null)
                        {
                            certification.ExtractedCertificateInfo = JsonSerializer.Serialize(dojahResult.ExtractedInfo);
                        }

                        // Combine all validation messages
                        var errorMessage = validationMessages.Count > 0 
                            ? string.Join(" | ", validationMessages)
                            : dojahResult.ErrorMessage;

                        verificationResult = new VerificationResultDTO
                        {
                            Status = finalStatus,
                            Confidence = dojahResult.Confidence,
                            VerifiedAt = DateTime.UtcNow,
                            ErrorMessage = errorMessage,
                            ExtractedInfo = dojahResult.ExtractedInfo != null ? MapExtractedInfo(dojahResult.ExtractedInfo) : null
                        };

                        var logMessage = shouldReject 
                            ? $"Certificate REJECTED. Status: {finalStatus}, Confidence: {dojahResult.Confidence}, Reasons: {errorMessage}"
                            : $"Certificate verification completed. Status: {finalStatus}, Confidence: {dojahResult.Confidence}";
                        
                        LogAuditEvent(logMessage, addCertificationRequest.CaregiverId);
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

                // Send notification and email after verification
                if (addCertificationRequest.VerifyImmediately && verificationResult != null)
                {
                    await SendVerificationNotificationAsync(careGiver, certification, verificationResult.Status);
                }

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

                // Get caregiver details for name validation
                var caregiver = await careGiverService.GetCaregiverUserAsync(certificate.CaregiverId);
                if (caregiver == null)
                {
                    throw new KeyNotFoundException($"Caregiver with ID '{certificate.CaregiverId}' not found.");
                }

                // Enhanced validation checks for certificate genuineness
                var validationMessages = new List<string>();
                var finalStatus = dojahResult.Status;
                var shouldReject = false;

                // 1. Confidence threshold validation
                var (isConfidentValid, confidenceStatus, confidenceMessage) = 
                    CertificateValidationHelper.ValidateConfidenceThreshold(dojahResult.Confidence);
                
                if (!isConfidentValid)
                {
                    validationMessages.Add(confidenceMessage);
                    finalStatus = confidenceStatus;
                    
                    if (confidenceStatus == DocumentVerificationStatus.Invalid)
                    {
                        shouldReject = true;
                    }
                }

                // 2. Name matching validation (only if Dojah extracted names)
                if (dojahResult.ExtractedInfo != null && 
                    !string.IsNullOrWhiteSpace(dojahResult.ExtractedInfo.FirstName) &&
                    !string.IsNullOrWhiteSpace(dojahResult.ExtractedInfo.LastName))
                {
                    var namesMatch = CertificateValidationHelper.ValidateNameMatch(
                        dojahResult.ExtractedInfo.FirstName,
                        dojahResult.ExtractedInfo.LastName,
                        caregiver.FirstName,
                        caregiver.LastName
                    );

                    if (!namesMatch)
                    {
                        validationMessages.Add(
                            $"Name mismatch: Certificate shows '{dojahResult.ExtractedInfo.FirstName} {dojahResult.ExtractedInfo.LastName}' " +
                            $"but profile shows '{caregiver.FirstName} {caregiver.LastName}'. Manual review required."
                        );
                        finalStatus = DocumentVerificationStatus.ManualReviewRequired;
                    }
                }

                // 3. Country validation
                if (dojahResult.DocumentType != null && 
                    !string.IsNullOrWhiteSpace(dojahResult.DocumentType.CountryCode))
                {
                    var isValidCountry = CertificateValidationHelper.ValidateCountryCode(
                        dojahResult.DocumentType.CountryCode
                    );

                    if (!isValidCountry)
                    {
                        validationMessages.Add(
                            $"Invalid certificate country: Expected Nigeria (NG) but detected '{dojahResult.DocumentType.CountryCode}'. " +
                            $"Only Nigerian educational certificates are accepted."
                        );
                        finalStatus = DocumentVerificationStatus.Invalid;
                        shouldReject = true;
                    }
                }

                // 4. Document type cross-validation
                if (dojahResult.DocumentType != null && 
                    !string.IsNullOrWhiteSpace(dojahResult.DocumentType.DocumentName))
                {
                    var typeMatches = CertificateValidationHelper.ValidateDocumentTypeMatch(
                        certificate.CertificateName,
                        dojahResult.DocumentType.DocumentName
                    );

                    if (!typeMatches)
                    {
                        validationMessages.Add(
                            $"Document type mismatch: Certificate claimed as '{certificate.CertificateName}' " +
                            $"but Dojah detected '{dojahResult.DocumentType.DocumentName}'. This may indicate a forged certificate."
                        );
                        finalStatus = DocumentVerificationStatus.Invalid;
                        shouldReject = true;
                    }
                }

                // 5. Issue date validation (if extracted)
                if (dojahResult.ExtractedInfo?.IssueDate != null)
                {
                    var (isDateValid, dateMessage) = CertificateValidationHelper.ValidateIssueDate(
                        dojahResult.ExtractedInfo.IssueDate
                    );

                    if (!isDateValid)
                    {
                        validationMessages.Add(dateMessage);
                        finalStatus = DocumentVerificationStatus.Invalid;
                        shouldReject = true;
                    }
                }

                // Update certificate with new verification results
                certificate.VerificationStatus = finalStatus;
                certificate.VerificationDate = DateTime.UtcNow;
                certificate.VerificationConfidence = dojahResult.Confidence;
                certificate.DojahVerificationResponse = dojahResult.RawResponse;
                certificate.VerificationAttempts += 1;
                certificate.IsVerified = finalStatus == DocumentVerificationStatus.Verified;

                if (dojahResult.ExtractedInfo != null)
                {
                    certificate.ExtractedCertificateInfo = JsonSerializer.Serialize(dojahResult.ExtractedInfo);
                }

                await careProDbContext.SaveChangesAsync();

                // Combine all validation messages
                var errorMessage = validationMessages.Count > 0 
                    ? string.Join(" | ", validationMessages)
                    : dojahResult.ErrorMessage;

                var logMessage = shouldReject 
                    ? $"Certificate retry REJECTED. Status: {finalStatus}, Confidence: {dojahResult.Confidence}, Reasons: {errorMessage}"
                    : $"Certificate verification retry completed. Status: {finalStatus}, Confidence: {dojahResult.Confidence}";
                
                LogAuditEvent(logMessage, certificate.CaregiverId);

                // Send notification after retry verification
                await SendVerificationNotificationAsync(caregiver, certificate, finalStatus);

                return new VerificationResultDTO
                {
                    Status = finalStatus,
                    Confidence = dojahResult.Confidence,
                    VerifiedAt = DateTime.UtcNow,
                    ErrorMessage = errorMessage,
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

        private async Task SendVerificationNotificationAsync(CaregiverResponse caregiver, Certification certification, DocumentVerificationStatus status)
        {
            try
            {
                string notificationTitle;
                string notificationContent;
                string emailSubject;
                string emailContent;

                switch (status)
                {
                    case DocumentVerificationStatus.Verified:
                        notificationTitle = "Certificate Verified Successfully";
                        notificationContent = $"Your {certification.CertificateName} has been successfully verified and approved.";
                        emailSubject = "Certificate Verification Successful - CarePro";
                        emailContent = $"Dear {caregiver.FirstName},\\n\\nGreat news! Your {certification.CertificateName} has been successfully verified and approved.\\n\\nYou can now proceed with your profile setup.\\n\\nBest regards,\\nCarePro Team";
                        break;

                    case DocumentVerificationStatus.Invalid:
                        notificationTitle = "Certificate Verification Failed";
                        notificationContent = $"Your {certification.CertificateName} could not be verified. Please upload a clearer image or contact support.";
                        emailSubject = "Certificate Verification Failed - CarePro";
                        emailContent = $"Dear {caregiver.FirstName},\\n\\nUnfortunately, we could not verify your {certification.CertificateName}. This may be due to image quality or document authenticity issues.\\n\\nPlease re-upload a clearer image of your certificate or contact our support team for assistance.\\n\\nBest regards,\\nCarePro Team";
                        break;

                    case DocumentVerificationStatus.ManualReviewRequired:
                        notificationTitle = "Certificate Under Review";
                        notificationContent = $"Your {certification.CertificateName} is under manual review. We'll notify you once the review is complete.";
                        emailSubject = "Certificate Under Manual Review - CarePro";
                        emailContent = $"Dear {caregiver.FirstName},\\n\\nYour {certification.CertificateName} requires manual review by our team. This typically takes 1-2 business days.\\n\\nWe'll notify you as soon as the review is complete.\\n\\nBest regards,\\nCarePro Team";
                        break;

                    case DocumentVerificationStatus.VerificationFailed:
                        notificationTitle = "Certificate Verification Error";
                        notificationContent = $"There was an error verifying your {certification.CertificateName}. Please try again or contact support.";
                        emailSubject = "Certificate Verification Error - CarePro";
                        emailContent = $"Dear {caregiver.FirstName},\\n\\nWe encountered a technical error while verifying your {certification.CertificateName}.\\n\\nPlease try uploading again, or contact our support team if the issue persists.\\n\\nBest regards,\\nCarePro Team";
                        break;

                    default:
                        return; // Don't send notification for pending status
                }

                // Send in-app notification
                await notificationService.CreateNotificationAsync(
                    recipientId: certification.CaregiverId,
                    senderId: "System",
                    type: "CertificateVerification",
                    content: notificationContent,
                    Title: notificationTitle,
                    relatedEntityId: certification.Id.ToString()
                );

                // Send email notification
                await emailService.SendGenericNotificationEmailAsync(
                    toEmail: caregiver.Email,
                    firstName: caregiver.FirstName,
                    subject: emailSubject,
                    content: emailContent
                );

                logger.LogInformation("Sent verification notification to caregiver {CaregiverId} for certificate {CertificateId} with status {Status}",
                    certification.CaregiverId, certification.Id, status);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send verification notification to caregiver {CaregiverId}", certification.CaregiverId);
                // Don't throw - notification failure shouldn't fail the main operation
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
