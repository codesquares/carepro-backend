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
        Task<string> CreateVerificationAsync(AddVerificationRequest addVerificationRequest);

        //  Task<IEnumerable<VerificationResponse>> GetAllCaregiverCertificateAsync();

        Task<VerificationResponse> GetVerificationAsync(string appUserId);

        Task<string> UpdateVerificationAsync(string verificationId, UpdateVerificationRequest updateVerificationRequest);

        Task<VerificationResponse?> GetUserVerificationStatusAsync(string userId);

        Task<string> AddVerificationAsync(AddVerificationRequest addVerificationRequest);

        Task<int> BackfillCaregiverVerificationStateAsync();

        /// <summary>
        /// Admin override of the verification status — used when a verification
        /// failed for a benign reason (e.g. middle-name mismatch) and the admin
        /// has manually confirmed the rest of the data matches. Bypasses the
        /// "already verified" guard, recomputes IsVerified, syncs the caregiver
        /// profile, and writes an entry to the AdminAuditLogs collection.
        /// Does NOT change any IDs and does NOT touch the original WebhookLogs.
        /// </summary>
        Task<AdminVerificationStatusOverrideResponse> AdminOverrideVerificationStatusAsync(
            string verificationId,
            AdminVerificationStatusOverrideRequest request);

    }
}
