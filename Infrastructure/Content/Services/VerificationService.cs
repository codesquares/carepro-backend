using Application.DTOs;
using Application.Interfaces.Content;
using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using Org.BouncyCastle.Ocsp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Content.Services
{
    public class VerificationService : IVerificationService
    {
        private readonly CareProDbContext careProDbContext;
        private readonly ICareGiverService careGiverService;
        private readonly ILogger<VerificationService> logger;

        public VerificationService(CareProDbContext careProDbContext, ICareGiverService careGiverService, ILogger<VerificationService> logger)
        {
            this.careProDbContext = careProDbContext;
            this.careGiverService = careGiverService;
            this.logger = logger;
        }

        public async Task<string> CreateVerificationAsync(AddVerificationRequest addVerificationRequest)
        {
            //var appUser = await careGiverService.GetCaregiverUserAsync(addVerificationRequest.UserId);
            var appUser = await careProDbContext.AppUsers.FirstOrDefaultAsync(x => x.AppUserId.ToString() == addVerificationRequest.UserId);
            if (appUser == null)
            {
                throw new KeyNotFoundException("The User MessageId entered is not a Valid ID");
            }

            if (appUser.FirstName != addVerificationRequest.VerifiedFirstName && appUser.LastName != addVerificationRequest.VerifiedLastName)
            {
                throw new InvalidOperationException("The Verified data and saved data do not match.");
            }


            var existingVerification = await careProDbContext.Verifications.FirstOrDefaultAsync(x => x.UserId == addVerificationRequest.UserId);

            if (existingVerification != null)
            {
                // Option 1: Prompt or return message to update instead
                throw new InvalidOperationException("This User has already been verified. Please update the existing verification.");

                // OR Option 2: Update existing verification here instead of throwing
                // existingVerification.VerificationMethod = addVerificationRequest.VerificationMethod;
                // existingVerification.VerificationStatus = addVerificationRequest.VerificationStatus;
                // existingVerification.UpdatedOn = DateTime.Now;
                // await careProDbContext.SaveChangesAsync();
                // return existingVerification.VerificationId.ToString();
            }


            /// CONVERT DTO TO DOMAIN OBJECT            
            var verification = new Verification
            {
                VerificationMethod = addVerificationRequest.VerificationMethod,
                VerificationNo = addVerificationRequest.VerificationNo,
                VerificationStatus = addVerificationRequest.VerificationStatus,
                UserId = addVerificationRequest.UserId,

                // Assign new ID
                VerificationId = ObjectId.GenerateNewId(),
                IsVerified = true,
                VerifiedOn = DateTime.Now,
            };

            await careProDbContext.Verifications.AddAsync(verification);

            await careProDbContext.SaveChangesAsync();

            return verification.VerificationId.ToString();

        }

        public async Task<VerificationResponse> GetVerificationAsync(string userId)
        {
            var verification = await careProDbContext.Verifications.FirstOrDefaultAsync(x => x.UserId.ToString() == userId);

            if (verification == null)
            {
                throw new KeyNotFoundException($"User with ID '{userId}' has not been verified.");
            }
                        

            var verificationDTO = new VerificationResponse()
            {
                VerificationId = verification.VerificationId.ToString(),
                UserId = verification.UserId,
                VerificationMethod = verification.VerificationMethod,
                VerificationNo = verification.VerificationNo,
                VerificationStatus = verification.VerificationStatus,
                IsVerified = verification.IsVerified,
                VerifiedOn = verification.VerifiedOn,
                UpdatedOn = verification.UpdatedOn,

            };

            return verificationDTO;
        }

        public async Task<string> UpdateVerificationAsync(string verificationId, UpdateVerificationRequest updateVerificationRequest)
        {
            if (!ObjectId.TryParse(verificationId, out var objectId))
            {
                throw new ArgumentException("Invalid Verification ID format.");
            }

            var existingVerification = await careProDbContext.Verifications.FindAsync(objectId);
            if (existingVerification == null)
            {
                throw new KeyNotFoundException($"Verification with ID '{verificationId}' not found.");
            }
            existingVerification.VerificationMethod = updateVerificationRequest.VerificationMode;
            existingVerification.VerificationStatus = updateVerificationRequest.VerificationStatus;
            existingVerification.UpdatedOn = DateTime.Now;

            careProDbContext.Verifications.Update(existingVerification);
            await careProDbContext.SaveChangesAsync();

            return $"Verification with ID '{verificationId}' Updated successfully.";

        }
    }
}
