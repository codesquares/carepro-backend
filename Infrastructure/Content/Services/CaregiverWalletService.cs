using Application.DTOs;
using Application.Interfaces.Content;
using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;

namespace Infrastructure.Content.Services
{
    public class CaregiverWalletService : ICaregiverWalletService
    {
        private readonly CareProDbContext _dbContext;
        private readonly ICareGiverService _careGiverService;
        private readonly ILogger<CaregiverWalletService> _logger;
        private readonly WalletLockManager _lockManager;

        // Maximum allowed single transaction amount (safety cap)
        private const decimal MAX_TRANSACTION_AMOUNT = 50_000_000m; // 50 million NGN

        public CaregiverWalletService(
            CareProDbContext dbContext,
            ICareGiverService careGiverService,
            ILogger<CaregiverWalletService> logger,
            WalletLockManager lockManager)
        {
            _dbContext = dbContext;
            _careGiverService = careGiverService;
            _logger = logger;
            _lockManager = lockManager;
        }

        // ── Security: Input validation helpers ──

        private static void ValidateCaregiverId(string caregiverId)
        {
            if (string.IsNullOrWhiteSpace(caregiverId))
                throw new ArgumentException("CaregiverId cannot be null or empty.", nameof(caregiverId));
        }

        private static void ValidateAmount(decimal amount, string operation)
        {
            if (amount <= 0)
                throw new ArgumentException($"Amount must be positive for {operation}. Received: {amount}");
            if (amount > MAX_TRANSACTION_AMOUNT)
                throw new ArgumentException($"Amount {amount} exceeds maximum allowed transaction amount for {operation}.");
        }

        /// <summary>
        /// Rounds to 2 decimal places to prevent precision drift attacks.
        /// </summary>
        private static decimal SafeRound(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

        public async Task<CaregiverWalletDTO> GetOrCreateWalletAsync(string caregiverId)
        {
            ValidateCaregiverId(caregiverId);

            var wallet = await _dbContext.CaregiverWallets
                .FirstOrDefaultAsync(w => w.CaregiverId == caregiverId);

            if (wallet == null)
            {
                wallet = new CaregiverWallet
                {
                    Id = ObjectId.GenerateNewId().ToString(),
                    CaregiverId = caregiverId,
                    TotalEarned = 0,
                    WithdrawableBalance = 0,
                    PendingBalance = 0,
                    TotalWithdrawn = 0,
                    Version = 0,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _dbContext.CaregiverWallets.Add(wallet);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("Created wallet for caregiver {CaregiverId}", caregiverId);
            }

            return MapToDTO(wallet);
        }

        public async Task<WalletSummaryResponse> GetWalletSummaryAsync(string caregiverId)
        {
            ValidateCaregiverId(caregiverId);

            var walletDto = await GetOrCreateWalletAsync(caregiverId);
            var caregiver = await _careGiverService.GetCaregiverUserAsync(caregiverId);

            return new WalletSummaryResponse
            {
                CaregiverId = caregiverId,
                CaregiverName = caregiver != null ? $"{caregiver.FirstName} {caregiver.LastName}" : "Unknown",
                TotalEarned = walletDto.TotalEarned,
                WithdrawableBalance = walletDto.WithdrawableBalance,
                PendingBalance = walletDto.PendingBalance,
                TotalWithdrawn = walletDto.TotalWithdrawn
            };
        }

        public async Task CreditOrderReceivedAsync(string caregiverId, decimal amount, bool isRecurring)
        {
            ValidateCaregiverId(caregiverId);
            ValidateAmount(amount, "CreditOrderReceived");
            amount = SafeRound(amount);

            await _lockManager.ExecuteWithLockAsync(caregiverId, async () =>
            {
                var wallet = await GetOrCreateWalletEntityAsync(caregiverId);

                wallet.TotalEarned = SafeRound(wallet.TotalEarned + amount);

                if (isRecurring)
                {
                    wallet.WithdrawableBalance = SafeRound(wallet.WithdrawableBalance + amount);
                }
                else
                {
                    wallet.PendingBalance = SafeRound(wallet.PendingBalance + amount);
                }

                wallet.Version++;
                wallet.UpdatedAt = DateTime.UtcNow;

                _dbContext.CaregiverWallets.Update(wallet);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation(
                    "Wallet credited for caregiver {CaregiverId}: +{Amount} (Recurring: {IsRecurring}). TotalEarned: {TotalEarned}, Pending: {Pending}, Withdrawable: {Withdrawable}, Version: {Version}",
                    caregiverId, amount, isRecurring, wallet.TotalEarned, wallet.PendingBalance, wallet.WithdrawableBalance, wallet.Version);
            });
        }

        public async Task ReleasePendingFundsAsync(string caregiverId, decimal amount)
        {
            ValidateCaregiverId(caregiverId);
            ValidateAmount(amount, "ReleasePendingFunds");
            amount = SafeRound(amount);

            await _lockManager.ExecuteWithLockAsync(caregiverId, async () =>
            {
                var wallet = await GetOrCreateWalletEntityAsync(caregiverId);

                // Guard: don't release more than what's pending
                var releaseAmount = Math.Min(amount, wallet.PendingBalance);
                if (releaseAmount <= 0)
                {
                    _logger.LogWarning(
                        "Attempted to release {Amount} for caregiver {CaregiverId} but PendingBalance is {Pending}",
                        amount, caregiverId, wallet.PendingBalance);
                    return;
                }

                wallet.PendingBalance = SafeRound(wallet.PendingBalance - releaseAmount);
                wallet.WithdrawableBalance = SafeRound(wallet.WithdrawableBalance + releaseAmount);
                wallet.Version++;
                wallet.UpdatedAt = DateTime.UtcNow;

                _dbContext.CaregiverWallets.Update(wallet);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation(
                    "Released pending funds for caregiver {CaregiverId}: {Amount}. Pending: {Pending}, Withdrawable: {Withdrawable}, Version: {Version}",
                    caregiverId, releaseAmount, wallet.PendingBalance, wallet.WithdrawableBalance, wallet.Version);
            });
        }

        public async Task CreditRecurringPaymentAsync(string caregiverId, decimal amount)
        {
            ValidateCaregiverId(caregiverId);
            ValidateAmount(amount, "CreditRecurringPayment");
            amount = SafeRound(amount);

            await _lockManager.ExecuteWithLockAsync(caregiverId, async () =>
            {
                var wallet = await GetOrCreateWalletEntityAsync(caregiverId);

                wallet.TotalEarned = SafeRound(wallet.TotalEarned + amount);
                wallet.WithdrawableBalance = SafeRound(wallet.WithdrawableBalance + amount);
                wallet.Version++;
                wallet.UpdatedAt = DateTime.UtcNow;

                _dbContext.CaregiverWallets.Update(wallet);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation(
                    "Recurring payment credited for caregiver {CaregiverId}: +{Amount}. TotalEarned: {TotalEarned}, Withdrawable: {Withdrawable}, Version: {Version}",
                    caregiverId, amount, wallet.TotalEarned, wallet.WithdrawableBalance, wallet.Version);
            });
        }

        public async Task DebitWithdrawalAsync(string caregiverId, decimal amount)
        {
            ValidateCaregiverId(caregiverId);
            ValidateAmount(amount, "DebitWithdrawal");
            amount = SafeRound(amount);

            await _lockManager.ExecuteWithLockAsync(caregiverId, async () =>
            {
                var wallet = await GetOrCreateWalletEntityAsync(caregiverId);

                if (wallet.WithdrawableBalance < amount)
                {
                    _logger.LogCritical(
                        "SECURITY: Withdrawal amount {Amount} exceeds withdrawable balance {Balance} for caregiver {CaregiverId}. Possible race condition or manipulation.",
                        amount, wallet.WithdrawableBalance, caregiverId);
                    throw new InvalidOperationException(
                        $"Insufficient withdrawable balance. Available: {wallet.WithdrawableBalance}, Requested: {amount}");
                }

                wallet.WithdrawableBalance = SafeRound(wallet.WithdrawableBalance - amount);
                wallet.TotalWithdrawn = SafeRound(wallet.TotalWithdrawn + amount);
                wallet.Version++;
                wallet.UpdatedAt = DateTime.UtcNow;

                _dbContext.CaregiverWallets.Update(wallet);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation(
                    "Withdrawal debited for caregiver {CaregiverId}: -{Amount}. Withdrawable: {Withdrawable}, TotalWithdrawn: {TotalWithdrawn}, Version: {Version}",
                    caregiverId, amount, wallet.WithdrawableBalance, wallet.TotalWithdrawn, wallet.Version);
            });
        }

        public async Task DebitRefundAsync(string caregiverId, decimal amount)
        {
            ValidateCaregiverId(caregiverId);
            ValidateAmount(amount, "DebitRefund");
            amount = SafeRound(amount);

            await _lockManager.ExecuteWithLockAsync(caregiverId, async () =>
            {
                var wallet = await GetOrCreateWalletEntityAsync(caregiverId);

                // Debit from withdrawable first, then pending if needed
                if (wallet.WithdrawableBalance >= amount)
                {
                    wallet.WithdrawableBalance = SafeRound(wallet.WithdrawableBalance - amount);
                }
                else
                {
                    var remainingDebit = SafeRound(amount - wallet.WithdrawableBalance);
                    wallet.WithdrawableBalance = 0;
                    wallet.PendingBalance = SafeRound(Math.Max(0, wallet.PendingBalance - remainingDebit));
                }

                wallet.TotalEarned = SafeRound(Math.Max(0, wallet.TotalEarned - amount));
                wallet.Version++;
                wallet.UpdatedAt = DateTime.UtcNow;

                _dbContext.CaregiverWallets.Update(wallet);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation(
                    "Refund debited for caregiver {CaregiverId}: -{Amount}. TotalEarned: {TotalEarned}, Withdrawable: {Withdrawable}, Version: {Version}",
                    caregiverId, amount, wallet.TotalEarned, wallet.WithdrawableBalance, wallet.Version);
            });
        }

        public async Task<bool> HasSufficientWithdrawableBalance(string caregiverId, decimal amount)
        {
            ValidateCaregiverId(caregiverId);

            if (amount <= 0)
                return false;

            var wallet = await _dbContext.CaregiverWallets
                .FirstOrDefaultAsync(w => w.CaregiverId == caregiverId);

            if (wallet == null)
                return false;

            return wallet.WithdrawableBalance >= SafeRound(amount);
        }

        // ── Private Helpers ──

        private async Task<CaregiverWallet> GetOrCreateWalletEntityAsync(string caregiverId)
        {
            var wallet = await _dbContext.CaregiverWallets
                .FirstOrDefaultAsync(w => w.CaregiverId == caregiverId);

            if (wallet == null)
            {
                wallet = new CaregiverWallet
                {
                    Id = ObjectId.GenerateNewId().ToString(),
                    CaregiverId = caregiverId,
                    TotalEarned = 0,
                    WithdrawableBalance = 0,
                    PendingBalance = 0,
                    TotalWithdrawn = 0,
                    Version = 0,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _dbContext.CaregiverWallets.Add(wallet);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("Created wallet for caregiver {CaregiverId}", caregiverId);
            }

            // Integrity check: no balance should ever be negative
            if (wallet.WithdrawableBalance < 0 || wallet.PendingBalance < 0 || wallet.TotalEarned < 0)
            {
                _logger.LogCritical(
                    "SECURITY: Negative balance detected for caregiver {CaregiverId}! Withdrawable: {W}, Pending: {P}, TotalEarned: {T}. Possible data corruption.",
                    caregiverId, wallet.WithdrawableBalance, wallet.PendingBalance, wallet.TotalEarned);
            }

            return wallet;
        }

        private static CaregiverWalletDTO MapToDTO(CaregiverWallet wallet)
        {
            return new CaregiverWalletDTO
            {
                Id = wallet.Id,
                CaregiverId = wallet.CaregiverId,
                TotalEarned = wallet.TotalEarned,
                WithdrawableBalance = wallet.WithdrawableBalance,
                PendingBalance = wallet.PendingBalance,
                TotalWithdrawn = wallet.TotalWithdrawn,
                Version = wallet.Version,
                UpdatedAt = wallet.UpdatedAt
            };
        }
    }
}
