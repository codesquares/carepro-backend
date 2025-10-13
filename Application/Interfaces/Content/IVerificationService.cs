using Application.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    public interface IVerificationService
    {
        Task<string> CreateVerificationAsync(AddVerificationRequest addVerificationRequest );

      //  Task<IEnumerable<VerificationResponse>> GetAllCaregiverCertificateAsync();

        Task<VerificationResponse> GetVerificationAsync(string appUserId);

        Task<string> UpdateVerificationAsync(string verificationId, UpdateVerificationRequest updateVerificationRequest );

    }
}
