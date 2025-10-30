using Application.DTOs;
using Application.Interfaces.Common;

namespace Infrastructure.Services.Common
{
    public class DojahDataFormattingService : IDojahDataFormattingService
    {
        public AddVerificationRequest FormatWebhookData(DojahWebhookRequest webhook, string userId)
        {
            var verificationNo = "";
            var verificationMethod = "";
            var firstName = "";
            var lastName = "";

            // Extract data from complex Dojah structure
            var governmentData = webhook.Data?.GovernmentData;
            var userData = webhook.Data?.UserData?.Data;
            var idData = webhook.Data?.Id?.Data;

            // Get BVN information if available
            if (governmentData?.Bvn?.Entity != null)
            {
                var bvnEntity = governmentData.Bvn.Entity;
                verificationNo = bvnEntity.Bvn;
                verificationMethod = "BVN";
                firstName = bvnEntity.FirstName?.Trim() ?? "";
                lastName = bvnEntity.LastName?.Trim() ?? "";
            }
            // Get NIN information if no BVN found
            else if (governmentData?.Nin?.Entity != null)
            {
                var ninEntity = governmentData.Nin.Entity;
                verificationNo = ninEntity.Nin;
                verificationMethod = "NIN";
                firstName = ninEntity.FirstName?.Trim() ?? "";
                lastName = ninEntity.LastName?.Trim() ?? "";
            }

            // Fallback to user data if government data not available
            if (string.IsNullOrEmpty(firstName) && userData != null)
            {
                firstName = userData.FirstName ?? "";
                lastName = userData.LastName ?? "";
            }

            // Fallback to ID data if still not available
            if (string.IsNullOrEmpty(firstName) && idData != null)
            {
                firstName = idData.FirstName ?? "";
                lastName = idData.LastName?.Replace(",", "") ?? "";
            }

            // Get verification method from main webhook data if not found
            if (string.IsNullOrEmpty(verificationMethod))
            {
                verificationMethod = webhook.IdType ?? webhook.VerificationType ?? "DOJAH_VERIFICATION";
            }

            // Get verification number from main webhook data if not found
            if (string.IsNullOrEmpty(verificationNo))
            {
                verificationNo = webhook.Value ?? webhook.ReferenceId ?? "";
            }

            // Ensure minimum required data
            firstName = string.IsNullOrEmpty(firstName) ? "Unknown" : firstName;
            lastName = string.IsNullOrEmpty(lastName) ? "User" : lastName;

            // Map Dojah status to backend expected status
            var verificationStatus = MapDojahStatus(webhook);

            return new AddVerificationRequest
            {
                UserId = userId,
                VerifiedFirstName = firstName,
                VerifiedLastName = lastName,
                VerificationMethod = verificationMethod,
                VerificationNo = verificationNo,
                VerificationStatus = verificationStatus
            };
        }

        private string MapDojahStatus(DojahWebhookRequest webhook)
        {
            // Handle different Dojah status formats
            if (webhook.Status == true || 
                webhook.VerificationStatus?.ToLower() == "success" || 
                webhook.VerificationStatus?.ToLower() == "completed")
            {
                return "success";
            }
            else if (webhook.VerificationStatus?.ToLower() == "pending" || 
                     webhook.VerificationStatus?.ToLower() == "processing")
            {
                return "pending";
            }
            else if (webhook.Status == false || 
                     webhook.VerificationStatus?.ToLower() == "failed" || 
                     webhook.VerificationStatus?.ToLower() == "cancelled")
            {
                return "failed";
            }

            return "failed"; // Default fallback
        }
    }
}