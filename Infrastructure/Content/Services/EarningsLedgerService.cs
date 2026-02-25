using Application.DTOs;
using Application.Interfaces.Content;
using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;

namespace Infrastructure.Content.Services
{
    public class EarningsLedgerService : IEarningsLedgerService
    {
        private readonly CareProDbContext _dbContext;
        private readonly ICaregiverWalletService _walletService;
        private readonly ILogger<EarningsLedgerService> _logger;

        public EarningsLedgerService(
            CareProDbContext dbContext,
            ICaregiverWalletService walletService,
            ILogger<EarningsLedgerService> logger)
        {
            _dbContext = dbContext;
            _walletService = walletService;
            _logger = logger;
        }

        // ── Security: Input validation ──
        private static void ValidateInput(string caregiverId, decimal amount)
        {
            if (string.IsNullOrWhiteSpace(caregiverId))
                throw new ArgumentException("CaregiverId cannot be null or empty.");
            if (amount == 0)
                throw new ArgumentException("Amount cannot be zero for ledger entry.");
        }

        public async Task RecordOrderReceivedAsync(string caregiverId, decimal amount, string clientOrderId,
            string? subscriptionId, int? billingCycleNumber, string serviceType, string description)
        {
            if (string.IsNullOrWhiteSpace(caregiverId))
                throw new ArgumentException("CaregiverId cannot be null or empty.");

            // Double-credit prevention: check if an OrderReceived entry already exists for this order
            var alreadyCredited = await _dbContext.EarningsLedger
                .AnyAsync(e => e.ClientOrderId == clientOrderId && e.Type == LedgerEntryType.OrderReceived);
            if (alreadyCredited)
            {
                _logger.LogWarning(
                    "SECURITY: Duplicate OrderReceived attempt for OrderId {OrderId}, CaregiverId {CaregiverId}. Blocked.",
                    clientOrderId, caregiverId);
                return;
            }

            var wallet = await _walletService.GetOrCreateWalletAsync(caregiverId);

            var entry = new EarningsLedger
            {
                Id = ObjectId.GenerateNewId().ToString(),
                CaregiverId = caregiverId,
                Type = LedgerEntryType.OrderReceived,
                Amount = amount,
                ClientOrderId = clientOrderId,
                SubscriptionId = subscriptionId,
                BillingCycleNumber = billingCycleNumber,
                ServiceType = serviceType,
                Description = description,
                BalanceAfter = wallet.WithdrawableBalance,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.EarningsLedger.Add(entry);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Ledger: OrderReceived for caregiver {CaregiverId}, order {OrderId}, amount {Amount}",
                caregiverId, clientOrderId, amount);
        }

        public async Task RecordFundsReleasedAsync(string caregiverId, decimal amount, string clientOrderId,
            string? subscriptionId, int? billingCycleNumber, string serviceType,
            string releaseReason, string description)
        {
            ValidateInput(caregiverId, amount);

            // Double-release prevention: check if FundsReleased already exists for this order
            var alreadyReleased = await _dbContext.EarningsLedger
                .AnyAsync(e => e.ClientOrderId == clientOrderId && e.Type == LedgerEntryType.FundsReleased);
            if (alreadyReleased)
            {
                _logger.LogWarning(
                    "SECURITY: Duplicate FundsReleased attempt for OrderId {OrderId}, CaregiverId {CaregiverId}. Blocked.",
                    clientOrderId, caregiverId);
                return;
            }

            var wallet = await _walletService.GetOrCreateWalletAsync(caregiverId);

            var entry = new EarningsLedger
            {
                Id = ObjectId.GenerateNewId().ToString(),
                CaregiverId = caregiverId,
                Type = LedgerEntryType.FundsReleased,
                Amount = amount,
                ClientOrderId = clientOrderId,
                SubscriptionId = subscriptionId,
                BillingCycleNumber = billingCycleNumber,
                ServiceType = serviceType,
                Description = description,
                BalanceAfter = wallet.WithdrawableBalance,
                ReleaseReason = releaseReason,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.EarningsLedger.Add(entry);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Ledger: FundsReleased for caregiver {CaregiverId}, order {OrderId}, amount {Amount}, reason {Reason}",
                caregiverId, clientOrderId, amount, releaseReason);
        }

        public async Task RecordWithdrawalAsync(string caregiverId, decimal amount, string withdrawalRequestId, string description)
        {
            ValidateInput(caregiverId, amount);

            var wallet = await _walletService.GetOrCreateWalletAsync(caregiverId);

            var entry = new EarningsLedger
            {
                Id = ObjectId.GenerateNewId().ToString(),
                CaregiverId = caregiverId,
                Type = LedgerEntryType.WithdrawalCompleted,
                Amount = -amount, // Negative for debits
                Description = description,
                BalanceAfter = wallet.WithdrawableBalance,
                WithdrawalRequestId = withdrawalRequestId,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.EarningsLedger.Add(entry);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Ledger: WithdrawalCompleted for caregiver {CaregiverId}, withdrawal {WithdrawalId}, amount -{Amount}",
                caregiverId, withdrawalRequestId, amount);
        }

        public async Task RecordRefundAsync(string caregiverId, decimal amount, string? clientOrderId,
            string? subscriptionId, string description)
        {
            ValidateInput(caregiverId, amount);

            var wallet = await _walletService.GetOrCreateWalletAsync(caregiverId);

            var entry = new EarningsLedger
            {
                Id = ObjectId.GenerateNewId().ToString(),
                CaregiverId = caregiverId,
                Type = LedgerEntryType.Refund,
                Amount = -amount, // Negative for debits
                ClientOrderId = clientOrderId,
                SubscriptionId = subscriptionId,
                Description = description,
                BalanceAfter = wallet.WithdrawableBalance,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.EarningsLedger.Add(entry);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Ledger: Refund for caregiver {CaregiverId}, amount -{Amount}",
                caregiverId, amount);
        }

        public async Task RecordDisputeHoldAsync(string caregiverId, decimal amount, string clientOrderId, string description)
        {
            if (string.IsNullOrWhiteSpace(caregiverId))
                throw new ArgumentException("CaregiverId cannot be null or empty.");
            // DisputeHold amount is 0 (informational), so we skip amount validation

            var wallet = await _walletService.GetOrCreateWalletAsync(caregiverId);

            var entry = new EarningsLedger
            {
                Id = ObjectId.GenerateNewId().ToString(),
                CaregiverId = caregiverId,
                Type = LedgerEntryType.DisputeHold,
                Amount = 0, // Hold doesn't change balance, it blocks release
                ClientOrderId = clientOrderId,
                Description = description,
                BalanceAfter = wallet.WithdrawableBalance,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.EarningsLedger.Add(entry);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Ledger: DisputeHold for caregiver {CaregiverId}, order {OrderId}",
                caregiverId, clientOrderId);
        }

        public async Task<List<LedgerHistoryResponse>> GetLedgerHistoryAsync(string caregiverId, int? limit = null)
        {
            var query = _dbContext.EarningsLedger
                .Where(e => e.CaregiverId == caregiverId)
                .OrderByDescending(e => e.CreatedAt);

            var entries = limit.HasValue
                ? await query.Take(limit.Value).ToListAsync()
                : await query.ToListAsync();

            return entries.Select(e => new LedgerHistoryResponse
            {
                Id = e.Id,
                Type = e.Type,
                Amount = e.Amount,
                Description = e.Description,
                ServiceType = e.ServiceType,
                BillingCycleNumber = e.BillingCycleNumber,
                BalanceAfter = e.BalanceAfter,
                CreatedAt = e.CreatedAt
            }).ToList();
        }

        public async Task<List<TransactionHistoryResponse>> GetTransactionHistoryAsync(string caregiverId)
        {
            var entries = await _dbContext.EarningsLedger
                .Where(e => e.CaregiverId == caregiverId)
                .OrderByDescending(e => e.CreatedAt)
                .ToListAsync();

            return entries.Select(e => new TransactionHistoryResponse
            {
                Id = e.Id,
                CaregiverId = e.CaregiverId,
                Amount = Math.Abs(e.Amount),
                Activity = e.Type,
                Description = e.Description,
                Status = e.Type,
                CreatedAt = e.CreatedAt
            }).ToList();
        }

        public async Task<bool> HasFundsBeenReleasedForOrderAsync(string clientOrderId)
        {
            return await _dbContext.EarningsLedger
                .AnyAsync(e => e.ClientOrderId == clientOrderId && e.Type == LedgerEntryType.FundsReleased);
        }
    }
}
