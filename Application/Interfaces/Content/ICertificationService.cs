using Application.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
    }
}
