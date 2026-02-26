using Application.DTOs;
using Application.Interfaces.Content;
using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using MongoDB.Bson;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Content.Services
{
    public class WithdrawalRequestService : IWithdrawalRequestService
    {
        private readonly CareProDbContext _dbContext;
        private readonly IEarningsService _earningsService;
        private readonly ICareGiverService _careGiverService;
        private readonly IAdminUserService _adminUserService;
        private readonly INotificationService _notificationService;
        private readonly ICaregiverWalletService _walletService;
        private readonly IEarningsLedgerService _ledgerService;

        public WithdrawalRequestService(
            CareProDbContext dbContext,
            IEarningsService earningsService,
            ICareGiverService careGiverService,
            IAdminUserService adminUserService,
            INotificationService notificationService,
            ICaregiverWalletService walletService,
            IEarningsLedgerService ledgerService)
        {
            _dbContext = dbContext;
            _earningsService = earningsService;
            _careGiverService = careGiverService;
            _adminUserService = adminUserService;
            _notificationService = notificationService;
            _walletService = walletService;
            _ledgerService = ledgerService;
        }

        public async Task<WithdrawalRequestResponse> GetWithdrawalRequestByIdAsync(string withdrawalRequestId)
        {
            // var withdrawal = await _dbContext.WithdrawalRequests.Find(w => w.Id == ObjectId.Parse(withdrawalRequestId)).FirstOrDefaultAsync();
            var withdrawal = await _dbContext.WithdrawalRequests.FirstOrDefaultAsync(w => w.Id == ObjectId.Parse(withdrawalRequestId));

            if (withdrawal == null)
                return null;

            return await MapWithdrawalToResponseAsync(withdrawal);
        }

        public async Task<WithdrawalRequestResponse> GetWithdrawalRequestByTokenAsync(string token)
        {
            //var withdrawal = await _dbContext.WithdrawalRequests.Find(w => w.Token == token).FirstOrDefaultAsync();
            var withdrawal = await _dbContext.WithdrawalRequests.FirstOrDefaultAsync(w => w.Token == token);

            if (withdrawal == null)
                return null;

            return await MapWithdrawalToResponseAsync(withdrawal);
        }


        public async Task<List<WithdrawalRequestResponse>> GetAllWithdrawalRequestsAsync()
        {
            var withdrawals = await _dbContext.WithdrawalRequests.ToListAsync();
            var responses = new List<WithdrawalRequestResponse>();

            foreach (var withdrawal in withdrawals)
            {
                responses.Add(await MapWithdrawalToResponseAsync(withdrawal));
            }

            return responses;
        }



        public async Task<List<WithdrawalRequestResponse>> GetWithdrawalRequestsByCaregiverIdAsync(string caregiverId)
        {
            var withdrawals = await _dbContext.WithdrawalRequests
                .Where(x => x.CaregiverId == caregiverId)
                .ToListAsync();
            var responses = new List<WithdrawalRequestResponse>();

            foreach (var withdrawal in withdrawals)
            {
                responses.Add(await MapWithdrawalToResponseAsync(withdrawal));
            }

            return responses;
        }


        public async Task<List<CaregiverWithdrawalHistoryResponse>> GetCaregiverWithdrawalRequestHistoryAsync(string caregiverId)
        {
            var withdrawals = await _dbContext.WithdrawalRequests
                .Where(x => x.CaregiverId == caregiverId)
                .ToListAsync();
            var responses = new List<CaregiverWithdrawalHistoryResponse>();

            foreach (var withdrawal in withdrawals)
            {
                var withdrawalDTO = new CaregiverWithdrawalHistoryResponse
                {
                    Id = withdrawal.Id.ToString(),
                    CaregiverId = withdrawal.CaregiverId,
                    AmountRequested = withdrawal.AmountRequested,
                    Activity = "Withdrawal",
                    Description = withdrawal.Status,
                    CompletedAt = withdrawal.CreatedAt,

                };
                responses.Add(withdrawalDTO);
            }

            return responses;
        }


        public async Task<List<WithdrawalRequestResponse>> GetWithdrawalRequestsByStatusAsync(string status)
        {
            var withdrawals = await _dbContext.WithdrawalRequests.
                Where(w => w.Status == status)
                .ToListAsync();
            var responses = new List<WithdrawalRequestResponse>();

            foreach (var withdrawal in withdrawals)
            {
                responses.Add(await MapWithdrawalToResponseAsync(withdrawal));
            }

            return responses;
        }

        public async Task<WithdrawalRequestResponse> CreateWithdrawalRequestAsync(CreateWithdrawalRequestRequest request)
        {
            // Check if there's already a pending withdrawal for this caregiver
            bool hasPending = await HasPendingRequest(request.CaregiverId);
            if (hasPending)
                throw new InvalidOperationException("A pending withdrawal request already exists for this caregiver");

            // Verify the caregiver has enough withdrawable funds using the wallet
            bool hasSufficientBalance = await _walletService.HasSufficientWithdrawableBalance(request.CaregiverId, request.AmountRequested);
            if (!hasSufficientBalance)
                throw new InvalidOperationException("Insufficient withdrawable funds, kindly check your Withdrawable Amount and stay within the limit");

            // Calculate service charge and final amount
            decimal serviceCharge = Math.Round(request.AmountRequested * 0.10m, 2);
            decimal finalAmount = request.AmountRequested - serviceCharge;

            // Generate a unique token
            string token = GenerateUniqueToken();

            // Check if token already exists (unlikely but possible)
            while (await TokenExists(token))
            {
                token = GenerateUniqueToken();
            }

            var withdrawal = new WithdrawalRequest
            {
                CaregiverId = request.CaregiverId,
                AmountRequested = request.AmountRequested,
                ServiceCharge = serviceCharge,
                FinalAmount = finalAmount,
                Token = token,
                Status = WithdrawalStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                AccountNumber = request.AccountNumber,
                BankName = request.BankName,
                AccountName = request.AccountName
            };

            //   await _dbContext.WithdrawalRequests.InsertOneAsync(withdrawal);

            _dbContext.WithdrawalRequests.Add(withdrawal);
            await _dbContext.SaveChangesAsync();

            // Notify all admin users about the new withdrawal request
            await NotifyAdminsAboutWithdrawalRequest(withdrawal);

            return await MapWithdrawalToResponseAsync(withdrawal);
        }


        public async Task<CaregiverWithdrawalSummaryResponse> GetTotalAmountEarnedAndWithdrawnByCaregiverIdAsync(string caregiverId)
        {
            // Read directly from the CaregiverWallet for accurate, persistent balances
            var walletSummary = await _walletService.GetWalletSummaryAsync(caregiverId);

            return new CaregiverWithdrawalSummaryResponse
            {
                TotalAmountEarned = walletSummary.TotalEarned,
                TotalAmountWithdrawn = walletSummary.TotalWithdrawn,
                WithdrawableAmount = walletSummary.WithdrawableBalance,
            };
        }


        public async Task<decimal> GetTotalWithdrawnByCaregiverIdAsync(string caregiverId)
        {
            var withdrawals = await _dbContext.WithdrawalRequests
                    .Where(e => e.CaregiverId == caregiverId && e.Status == "Completed")
                    .OrderByDescending(n => n.CreatedAt)
                    .ToListAsync();

            var withdrawalsDTO = new List<WithdrawalRequestResponse>();
            decimal totalWithdrawnAmount = 0;
            // decimal totalEarnings = 0;

            foreach (var withdrawal in withdrawals)
            {
                totalWithdrawnAmount += withdrawal.AmountRequested;

            }

            return totalWithdrawnAmount;
            //return new CaregiverWithdrawalSummaryResponse
            //{
            //    WithdrawableAmount = totalAmountWithdrawn,
            //};
        }


        public async Task<WithdrawalRequestResponse> VerifyWithdrawalRequestAsync(AdminWithdrawalVerificationRequest request)
        {
            WithdrawalRequest withdrawal = null;

            if (!string.IsNullOrEmpty(request.Token))
            {
                withdrawal = await _dbContext.WithdrawalRequests
                    .FirstOrDefaultAsync(w => w.Token == request.Token);
            }
            else if (!string.IsNullOrEmpty(request.WithdrawalId) &&
                     MongoDB.Bson.ObjectId.TryParse(request.WithdrawalId, out var verifyObjectId))
            {
                withdrawal = await _dbContext.WithdrawalRequests
                    .FirstOrDefaultAsync(w => w.Id == verifyObjectId);
            }

            if (withdrawal == null)
                throw new InvalidOperationException("Withdrawal request not found");

            if (withdrawal.Status != WithdrawalStatus.Pending)
                throw new InvalidOperationException("Withdrawal request is not in pending state");

            // Update properties directly
            withdrawal.Status = WithdrawalStatus.Verified;
            withdrawal.VerifiedAt = DateTime.UtcNow;
            withdrawal.AdminId = request.AdminId;
            withdrawal.AdminNotes = request.AdminNotes;

            // Save changes
            _dbContext.WithdrawalRequests.Update(withdrawal);
            await _dbContext.SaveChangesAsync();

            // Notify caregiver
            await NotifyCaregiverAboutWithdrawalStatusChange(withdrawal,
                "Withdrawal Request Verified",
                $"Your withdrawal request for {withdrawal.AmountRequested:C} has been verified by admin. Final amount after service charge: {withdrawal.FinalAmount:C}");

            return await MapWithdrawalToResponseAsync(withdrawal);
        }


        public async Task<WithdrawalRequestResponse> CompleteWithdrawalRequestAsync(string token, string adminId)
        {
            return await CompleteWithdrawalRequestAsync(new AdminWithdrawalVerificationRequest
            {
                Token = token,
                AdminId = adminId
            });
        }

        public async Task<WithdrawalRequestResponse> CompleteWithdrawalRequestAsync(AdminWithdrawalVerificationRequest request)
        {
            WithdrawalRequest withdrawal = null;

            if (!string.IsNullOrEmpty(request.Token))
            {
                withdrawal = await _dbContext.WithdrawalRequests
                    .FirstOrDefaultAsync(w => w.Token == request.Token);
            }
            else if (!string.IsNullOrEmpty(request.WithdrawalId) &&
                     MongoDB.Bson.ObjectId.TryParse(request.WithdrawalId, out var completeObjectId))
            {
                withdrawal = await _dbContext.WithdrawalRequests
                    .FirstOrDefaultAsync(w => w.Id == completeObjectId);
            }

            if (withdrawal == null)
                throw new InvalidOperationException("Withdrawal request not found");

            if (withdrawal.Status != WithdrawalStatus.Verified)
                throw new InvalidOperationException("Withdrawal request must be verified before completion");

            // Debit wallet and record ledger FIRST — if either fails, status
            // stays Verified so the admin can safely retry without data corruption.
            await _walletService.DebitWithdrawalAsync(withdrawal.CaregiverId, withdrawal.AmountRequested);
            await _ledgerService.RecordWithdrawalAsync(
                withdrawal.CaregiverId,
                withdrawal.AmountRequested,
                withdrawal.Id.ToString(),
                $"Withdrawal completed. Requested: {withdrawal.AmountRequested:N2}, Service charge: {withdrawal.ServiceCharge:N2}, Final: {withdrawal.FinalAmount:N2}");

            // Only mark Completed after the money has actually moved
            withdrawal.Status = WithdrawalStatus.Completed;
            withdrawal.CompletedAt = DateTime.UtcNow;
            withdrawal.AdminId = request.AdminId;

            _dbContext.WithdrawalRequests.Update(withdrawal);
            await _dbContext.SaveChangesAsync();

            // Notify caregiver
            await NotifyCaregiverAboutWithdrawalStatusChange(withdrawal,
                "Withdrawal Completed",
                $"Your withdrawal of {withdrawal.FinalAmount:C} has been completed successfully.");

            return await MapWithdrawalToResponseAsync(withdrawal);
        }



        public async Task<WithdrawalRequestResponse> RejectWithdrawalRequestAsync(AdminWithdrawalVerificationRequest request)
        {
            WithdrawalRequest withdrawal = null;

            if (!string.IsNullOrEmpty(request.Token))
            {
                withdrawal = await _dbContext.WithdrawalRequests
                    .FirstOrDefaultAsync(w => w.Token == request.Token);
            }
            else if (!string.IsNullOrEmpty(request.WithdrawalId) &&
                     MongoDB.Bson.ObjectId.TryParse(request.WithdrawalId, out var rejectObjectId))
            {
                withdrawal = await _dbContext.WithdrawalRequests
                    .FirstOrDefaultAsync(w => w.Id == rejectObjectId);
            }

            if (withdrawal == null)
                throw new InvalidOperationException("Withdrawal request not found");

            if (withdrawal.Status != WithdrawalStatus.Pending && withdrawal.Status != WithdrawalStatus.Verified)
                throw new InvalidOperationException("Withdrawal request cannot be rejected in current state");

            // Update properties directly
            withdrawal.Status = WithdrawalStatus.Rejected;
            withdrawal.RejectedAt = DateTime.UtcNow;
            withdrawal.AdminId = request.AdminId;
            withdrawal.AdminNotes = request.AdminNotes;

            // Save changes
            _dbContext.WithdrawalRequests.Update(withdrawal);
            await _dbContext.SaveChangesAsync();

            // Notify caregiver
            await NotifyCaregiverAboutWithdrawalStatusChange(withdrawal,
                "Withdrawal Request Rejected",
                $"Your withdrawal request for {withdrawal.AmountRequested:C} has been rejected. Reason: {request.AdminNotes}");

            return await MapWithdrawalToResponseAsync(withdrawal);
        }


        public async Task<bool> TokenExists(string token)
        {
            return await _dbContext.WithdrawalRequests
                .AnyAsync(w => w.Token == token);
        }



        public async Task<bool> HasPendingRequest(string caregiverId)
        {
            return await _dbContext.WithdrawalRequests
                .AnyAsync(w => w.CaregiverId == caregiverId && w.Status == WithdrawalStatus.Pending);
        }


        private string GenerateUniqueToken()
        {
            // Generate a cryptographically secure 8-character alphanumeric token
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var bytes = RandomNumberGenerator.GetBytes(8);
            var token = new string(bytes.Select(b => chars[b % chars.Length]).ToArray());
            return token;
        }

        private async Task<WithdrawalRequestResponse> MapWithdrawalToResponseAsync(WithdrawalRequest withdrawal)
        {
            string caregiverName = "Unknown";
            try
            {
                var caregiver = await _careGiverService.GetCaregiverUserAsync(withdrawal.CaregiverId);
                if (caregiver != null)
                    caregiverName = $"{caregiver.FirstName} {caregiver.LastName}";
            }
            catch (KeyNotFoundException) { /* caregiver may have been deleted — degrade gracefully */ }

            string adminName = "Not assigned";
            if (!string.IsNullOrEmpty(withdrawal.AdminId))
            {
                try
                {
                    var admin = await _adminUserService.GetAdminUserByIdAsync(withdrawal.AdminId);
                    if (admin != null)
                        adminName = $"{admin.FirstName} {admin.LastName}";
                }
                catch (KeyNotFoundException) { adminName = "Unknown Admin"; }
            }

            return new WithdrawalRequestResponse
            {
                Id = withdrawal.Id.ToString(),
                CaregiverId = withdrawal.CaregiverId,
                CaregiverName = caregiverName,
                AmountRequested = withdrawal.AmountRequested,
                ServiceCharge = withdrawal.ServiceCharge,
                FinalAmount = withdrawal.FinalAmount,
                Token = withdrawal.Token,
                Status = withdrawal.Status,
                CreatedAt = withdrawal.CreatedAt,
                VerifiedAt = withdrawal.VerifiedAt,
                CompletedAt = withdrawal.CompletedAt,
                RejectedAt = withdrawal.RejectedAt,
                AdminNotes = withdrawal.AdminNotes,
                AdminId = withdrawal.AdminId,
                AdminName = adminName,
                AccountNumber = withdrawal.AccountNumber,
                BankName = withdrawal.BankName,
                AccountName = withdrawal.AccountName
            };
        }


        private async Task NotifyAdminsAboutWithdrawalRequest(WithdrawalRequest withdrawal)
        {
            // In a real-world scenario, we'd query for all admin users and notify them
            // For now, we'll create a notification for a generic admin role

            var caregiver = await _careGiverService.GetCaregiverUserAsync(withdrawal.CaregiverId);
            string caregiverName = caregiver != null ? $"{caregiver.FirstName} {caregiver.LastName}" : "Unknown";

            var notification = new Notification
            {
                RecipientId = "admin", // This should be replaced with actual admin IDs in production
                SenderId = withdrawal.CaregiverId,
                Type = "Withdrawal Request",
                Title = "New Withdrawal Request",
                Content = $"Caregiver {caregiverName} has requested a withdrawal of {withdrawal.AmountRequested:C}. " +
                          $"Service charge: {withdrawal.ServiceCharge:C}. Final amount: {withdrawal.FinalAmount:C}. " +
                          $"Verification token: {withdrawal.Token}",
                CreatedAt = DateTime.UtcNow,
                IsRead = false,
                RelatedEntityId = withdrawal.Id.ToString()
            };

            await _dbContext.Notifications.AddAsync(notification);
            await _dbContext.SaveChangesAsync();
        }

        private async Task NotifyCaregiverAboutWithdrawalStatusChange(WithdrawalRequest withdrawal, string title, string message)
        {
            var notification = new Notification
            {
                RecipientId = withdrawal.CaregiverId,
                SenderId = withdrawal.AdminId ?? "system",
                Type = "Withdrawal Request",
                Title = title,
                Content = message,
                CreatedAt = DateTime.UtcNow,
                IsRead = false,
                RelatedEntityId = withdrawal.Id.ToString()
            };

            //  await _dbContext.Notifications.InsertOneAsync(notification);
            await _dbContext.Notifications.AddAsync(notification);
            await _dbContext.SaveChangesAsync();
        }


    }
}
