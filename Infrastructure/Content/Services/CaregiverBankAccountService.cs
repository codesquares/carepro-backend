using Application.DTOs;
using Application.Interfaces.Content;
using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using MongoDB.Bson;

namespace Infrastructure.Content.Services
{
    public class CaregiverBankAccountService : ICaregiverBankAccountService
    {
        private readonly CareProDbContext _dbContext;
        private readonly ICareGiverService _careGiverService;
        private readonly ICaregiverWalletService _walletService;

        public CaregiverBankAccountService(
            CareProDbContext dbContext,
            ICareGiverService careGiverService,
            ICaregiverWalletService walletService)
        {
            _dbContext = dbContext;
            _careGiverService = careGiverService;
            _walletService = walletService;
        }

        public async Task<CaregiverBankAccountResponse> GetBankAccountAsync(string caregiverId)
        {
            var account = await _dbContext.CaregiverBankAccounts
                .FirstOrDefaultAsync(a => a.CaregiverId == caregiverId);

            if (account == null)
                return null;

            return MapToResponse(account);
        }

        public async Task<CaregiverBankAccountResponse> CreateOrUpdateBankAccountAsync(
            string caregiverId, CaregiverBankAccountRequest request)
        {
            var existing = await _dbContext.CaregiverBankAccounts
                .FirstOrDefaultAsync(a => a.CaregiverId == caregiverId);

            if (existing != null)
            {
                // Update
                existing.FullName = request.FullName;
                existing.BankName = request.BankName;
                existing.AccountNumber = request.AccountNumber;
                existing.AccountName = request.AccountName;
                existing.UpdatedAt = DateTime.UtcNow;

                _dbContext.CaregiverBankAccounts.Update(existing);
                await _dbContext.SaveChangesAsync();

                return MapToResponse(existing);
            }
            else
            {
                // Create
                var account = new CaregiverBankAccount
                {
                    Id = ObjectId.GenerateNewId().ToString(),
                    CaregiverId = caregiverId,
                    FullName = request.FullName,
                    BankName = request.BankName,
                    AccountNumber = request.AccountNumber,
                    AccountName = request.AccountName,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _dbContext.CaregiverBankAccounts.Add(account);
                await _dbContext.SaveChangesAsync();

                return MapToResponse(account);
            }
        }

        public async Task<AdminCaregiverFinancialSummary> GetAdminCaregiverFinancialSummaryAsync(string caregiverId)
        {
            // Get caregiver profile
            var caregiver = await _careGiverService.GetCaregiverUserAsync(caregiverId);

            // Get wallet
            var wallet = await _walletService.GetWalletSummaryAsync(caregiverId);

            // Get bank account (may be null)
            var bankAccount = await GetBankAccountAsync(caregiverId);

            return new AdminCaregiverFinancialSummary
            {
                CaregiverId = caregiverId,
                CaregiverName = caregiver != null ? $"{caregiver.FirstName} {caregiver.LastName}" : "Unknown",
                Email = caregiver?.Email ?? "Unknown",
                PhoneNo = caregiver?.PhoneNo ?? "Unknown",
                TotalEarned = wallet.TotalEarned,
                WithdrawableBalance = wallet.WithdrawableBalance,
                PendingBalance = wallet.PendingBalance,
                TotalWithdrawn = wallet.TotalWithdrawn,
                BankAccount = bankAccount
            };
        }

        private static CaregiverBankAccountResponse MapToResponse(CaregiverBankAccount account)
        {
            return new CaregiverBankAccountResponse
            {
                Id = account.Id,
                CaregiverId = account.CaregiverId,
                FullName = account.FullName,
                BankName = account.BankName,
                AccountNumber = account.AccountNumber,
                AccountName = account.AccountName,
                CreatedAt = account.CreatedAt,
                UpdatedAt = account.UpdatedAt
            };
        }
    }
}
