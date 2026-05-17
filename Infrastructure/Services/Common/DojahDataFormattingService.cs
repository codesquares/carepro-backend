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
            if (governmentData?.Data?.Bvn?.Entity != null)
            {
                var bvnEntity = governmentData.Data.Bvn.Entity;
                verificationNo = bvnEntity.Bvn;
                verificationMethod = "BVN";
                firstName = bvnEntity.FirstName?.Trim() ?? "";
                lastName = bvnEntity.LastName?.Trim() ?? "";
            }
            // Get NIN information if no BVN found
            else if (governmentData?.Data?.Nin?.Entity != null)
            {
                var ninEntity = governmentData.Data.Nin.Entity;
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
            if (string.IsNullOrEmpty(firstName) && idData?.IdData != null)
            {
                firstName = idData.IdData.FirstName ?? "";
                lastName = idData.IdData.LastName?.Replace(",", "") ?? "";
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

            // Resolve UserType (case-insensitive, with reference_id fallback).
            // Priority order:
            //   1. webhook.Metadata.UserType (frontend-supplied)
            //   2. reference_id prefix ("caregiver_..." / "client_...")
            //   3. fallback to "Caregiver" for backward compatibility
            var userType = ResolveUserType(webhook);

            return new AddVerificationRequest
            {
                UserId = userId,
                VerifiedFirstName = firstName,
                VerifiedLastName = lastName,
                VerificationMethod = verificationMethod,
                VerificationNo = verificationNo,
                VerificationStatus = verificationStatus,
                UserType = userType
            };
        }

        private static string ResolveUserType(DojahWebhookRequest webhook)
        {
            var metaUserType = webhook?.Metadata?.UserType;
            if (!string.IsNullOrWhiteSpace(metaUserType))
            {
                var normalised = metaUserType.Trim().ToLowerInvariant();
                if (normalised == "client") return "Client";
                if (normalised == "caregiver") return "Caregiver";
            }

            // Fallback: parse reference_id prefix.
            // Top-level reference_id is the canonical source; metadata.reference_id
            // is the redundant copy frontend sends inside the widget config.
            var referenceId = webhook?.ReferenceId;
            if (string.IsNullOrWhiteSpace(referenceId))
            {
                referenceId = webhook?.Metadata?.ReferenceId;
            }

            if (!string.IsNullOrWhiteSpace(referenceId))
            {
                var rid = referenceId.Trim().ToLowerInvariant();
                if (rid.StartsWith("client_")) return "Client";
                if (rid.StartsWith("caregiver_")) return "Caregiver";
            }

            // Backward-compat default — pre-May-2026 webhooks have no UserType
            return "Caregiver";
        }

        private string MapDojahStatus(DojahWebhookRequest webhook)
        {
            // Dojah verification_status values: Ongoing, Abandoned, Completed, Pending, Failed
            var status = webhook.VerificationStatus?.ToLower();

            if (webhook.Status == true ||
                status == "success" ||
                status == "completed")
            {
                return "success";
            }
            else if (status == "pending" ||
                     status == "processing" ||
                     status == "ongoing")
            {
                // Treat Ongoing as pending - user is still in the verification flow.
                return "pending";
            }
            else if (webhook.Status == false ||
                     status == "failed" ||
                     status == "cancelled" ||
                     status == "abandoned")
            {
                // Abandoned means the user did not complete the widget, so we don't have
                // all the required details - treat it as a failure.
                return "failed";
            }

            return "failed"; // Default fallback
        }
    }
}