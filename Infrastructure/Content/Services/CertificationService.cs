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
using System.Threading.Tasks;

namespace Infrastructure.Content.Services
{
    public class CertificationService : ICertificationService
    {
        private readonly CareProDbContext careProDbContext;
        private readonly ICareGiverService careGiverService;
        private readonly ILogger<CertificationService> logger;

        public CertificationService(CareProDbContext careProDbContext, ICareGiverService careGiverService, ILogger<CertificationService> logger)
        {
            this.careProDbContext = careProDbContext;
            this.careGiverService = careGiverService;
            this.logger = logger;
        }


        public async Task<string> CreateCertificateAsync(AddCertificationRequest addCertificationRequest)
        {
            var careGiver = await careGiverService.GetCaregiverUserAsync(addCertificationRequest.CaregiverId);
            if (careGiver == null)
            {
                throw new KeyNotFoundException("The CaregiverID entered is not a Valid ID");
            }

            // Convert the Base 64 string to a byte array
            var certificateBytes = Convert.FromBase64String(addCertificationRequest.Certificate);

            /// CONVERT DTO TO DOMAIN OBJECT            
            var certification = new Certification
            {
                CertificateName = addCertificationRequest.CertificateName,
                CertificateIssuer = addCertificationRequest.CertificateIssuer,
                Certificate = certificateBytes,
                YearObtained = addCertificationRequest.YearObtained,                            
                CaregiverId = addCertificationRequest.CaregiverId,

                // Assign new ID
                Id = ObjectId.GenerateNewId(),
                IsVerified = false,
                SubmittedOn = DateTime.Now,
            };

            await careProDbContext.Certifications.AddAsync(certification);

            await careProDbContext.SaveChangesAsync();

            return certification.Id.ToString();
        }

        public async Task<IEnumerable<CertificationResponse>> GetAllCaregiverCertificateAsync(string caregiverId)
        {
            var certificates = await careProDbContext.Certifications
                .Where(x => x.CaregiverId == caregiverId )
                .OrderBy(x => x.SubmittedOn)
                .ToListAsync();

            var certificatesDTOs = new List<CertificationResponse>();


            foreach (var certificate in certificates)
            {
                // Determine the image format based on the binary data
                string certificateBase64 = null;
                if (certificate.Certificate != null)
                {
                    string certificateBytesFormat = GetCertificateFormat(certificate.Certificate);  // This method detects the image format
                    certificateBase64 = $"data:image/{certificateBytesFormat};base64,{Convert.ToBase64String(certificate.Certificate)}";
                }

                var certificateDTO = new CertificationResponse()
                {
                    Id = certificate.Id.ToString(),
                    CaregiverId = certificate.CaregiverId,
                    CertificateName = certificate.CertificateName,
                    CertificateIssuer = certificate.CertificateIssuer,
                    Certificate = certificateBase64,
                    YearObtained = certificate.YearObtained,
                    IsVerified = certificate.IsVerified,
                    SubmittedOn = certificate.SubmittedOn,                    

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

            // Determine the image format based on the binary data
            string certificateBase64 = null;
            if (certificate.Certificate != null)
            {
                string certificateBytesFormat = GetCertificateFormat(certificate.Certificate);  // This method detects the image format
                certificateBase64 = $"data:image/{certificateBytesFormat};base64,{Convert.ToBase64String(certificate.Certificate)}";
            }

            var certificateDTO = new CertificationResponse()
            {
                Id = certificate.Id.ToString(),
                CaregiverId = certificate.CaregiverId,
                CertificateName = certificate.CertificateName,
                CertificateIssuer = certificate.CertificateIssuer,
                Certificate = certificateBase64,
                YearObtained = certificate.YearObtained,
                IsVerified = certificate.IsVerified,
                SubmittedOn = certificate.SubmittedOn,

            };

            return certificateDTO;
        }

        public string GetCertificateFormat(byte[] certificateByteData)
        {
            // Basic detection of common image formats based on header bytes
            if (certificateByteData.Length >= 4)
            {
                // PNG: 89 50 4E 47
                if (certificateByteData[0] == 0x89 && certificateByteData[1] == 0x50 && certificateByteData[2] == 0x4E && certificateByteData[3] == 0x47)
                    return "png";

                // JPEG/JPG: FF D8 FF
                if (certificateByteData[0] == 0xFF && certificateByteData[1] == 0xD8 && certificateByteData[2] == 0xFF)
                    return "jpeg";

                // GIF: 47 49 46
                if (certificateByteData[0] == 0x47 && certificateByteData[1] == 0x49 && certificateByteData[2] == 0x46)
                    return "gif";
            }
            return "jpeg";  // Default to jpeg if format is not identifiable
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
