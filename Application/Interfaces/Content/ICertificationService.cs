using Application.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Domain.Entities;

namespace Application.Interfaces.Content
{
    public interface ICertificationService
    {
        Task<CertificationUploadResponse> CreateCertificateAsync(AddCertificationRequest addCertificationRequest);

        Task<IEnumerable<CertificationResponse>> GetAllCaregiverCertificateAsync(string caregiverId);

        Task<CertificationResponse> GetCertificateAsync(string certificateId);

        Task<VerificationResultDTO> RetryVerificationAsync(string certificateId);

        Task DeleteCertificateAsync(string certificateId);

        Task DeleteAllCaregiverCertificatesAsync(string caregiverId);

        // Admin Certificate Management
        Task<IEnumerable<AdminCertificationResponse>> GetAllCertificatesAsync();

        Task<IEnumerable<AdminCertificationResponse>> GetCertificatesByStatusAsync(DocumentVerificationStatus status);

        Task<AdminCertificationResponse> GetCertificateDetailsAsync(string certificateId);

        Task<CertificateManagementResponse> ManuallyApproveCertificateAsync(string certificateId, string adminId, string? approvalNotes);

        Task<CertificateManagementResponse> ManuallyRejectCertificateAsync(string certificateId, string adminId, string rejectionReason);
    }
}
