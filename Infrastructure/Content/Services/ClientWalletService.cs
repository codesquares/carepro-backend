using Application.DTOs;
using Application.Interfaces.Content;
using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;

namespace Infrastructure.Content.Services
{
    public class ClientWalletService : IClientWalletService
    {
        private readonly CareProDbContext _dbContext;
        private readonly ILogger<ClientWalletService> _logger;
        private readonly WalletLockManager _lockManager;

        private const decimal MAX_TRANSACTION_AMOUNT = 50_000_000m;

        public ClientWalletService(
            CareProDbContext dbContext,
            ILogger<ClientWalletService> logger,
            WalletLockManager lockManager)
        {
            _dbContext = dbContext;
            _logger = logger;
            _lockManager = lockManager;
        }

        public async Task<ClientWalletDTO> GetOrCreateWalletAsync(string clientId)
        {
            if (string.IsNullOrWhiteSpace(clientId))
                throw new ArgumentException("ClientId cannot be null or empty.", nameof(clientId));

            var wallet = await _dbContext.ClientWallets
                .FirstOrDefaultAsync(w => w.ClientId == clientId);

            if (wallet == null)
            {
                wallet = new ClientWallet
                {
                    Id = ObjectId.GenerateNewId().ToString(),
                    ClientId = clientId,
                    CreditBalance = 0,
                    TotalCredited = 0,
                    TotalSpent = 0,
                    Version = 0,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _dbContext.ClientWallets.Add(wallet);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("Created wallet for client {ClientId}", clientId);
            }

            return new ClientWalletDTO
            {
                Id = wallet.Id,
                ClientId = wallet.ClientId,
                CreditBalance = wallet.CreditBalance,
                TotalCredited = wallet.TotalCredited,
                TotalSpent = wallet.TotalSpent,
                UpdatedAt = wallet.UpdatedAt
            };
        }

        public async Task CreditAsync(string clientId, decimal amount, string description, string? orderId = null, string? taskSheetId = null, string? ledgerType = null)
        {
            if (string.IsNullOrWhiteSpace(clientId))
                throw new ArgumentException("ClientId cannot be null or empty.", nameof(clientId));
            if (amount <= 0)
                throw new ArgumentException($"Amount must be positive. Received: {amount}");
            if (amount > MAX_TRANSACTION_AMOUNT)
                throw new ArgumentException($"Amount {amount} exceeds maximum allowed transaction amount.");

            amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero);

            await _lockManager.ExecuteWithLockAsync(clientId, async () =>
            {
                var wallet = await _dbContext.ClientWallets
                    .FirstOrDefaultAsync(w => w.ClientId == clientId);

                if (wallet == null)
                {
                    wallet = new ClientWallet
                    {
                        Id = ObjectId.GenerateNewId().ToString(),
                        ClientId = clientId,
                        Version = 0,
                        CreatedAt = DateTime.UtcNow
                    };
                    _dbContext.ClientWallets.Add(wallet);
                }

                wallet.CreditBalance += amount;
                wallet.TotalCredited += amount;
                wallet.Version++;
                wallet.UpdatedAt = DateTime.UtcNow;

                var ledger = new ClientWalletLedger
                {
                    Id = ObjectId.GenerateNewId().ToString(),
                    ClientId = clientId,
                    Type = ledgerType ?? ClientLedgerEntryType.CancellationCredit,
                    Amount = amount,
                    ClientOrderId = orderId,
                    TaskSheetId = taskSheetId,
                    Description = description,
                    BalanceAfter = wallet.CreditBalance,
                    CreatedAt = DateTime.UtcNow
                };

                _dbContext.ClientWalletLedgers.Add(ledger);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("Credited {Amount} to client {ClientId} wallet. New balance: {Balance}. Reason: {Description}",
                    amount, clientId, wallet.CreditBalance, description);
            });
        }

        public async Task DebitAsync(string clientId, decimal amount, string description, string? orderId = null)
        {
            if (string.IsNullOrWhiteSpace(clientId))
                throw new ArgumentException("ClientId cannot be null or empty.", nameof(clientId));
            if (amount <= 0)
                throw new ArgumentException($"Amount must be positive. Received: {amount}");
            if (amount > MAX_TRANSACTION_AMOUNT)
                throw new ArgumentException($"Amount {amount} exceeds maximum allowed transaction amount.");

            amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero);

            await _lockManager.ExecuteWithLockAsync(clientId, async () =>
            {
                var wallet = await _dbContext.ClientWallets
                    .FirstOrDefaultAsync(w => w.ClientId == clientId);

                if (wallet == null || wallet.CreditBalance < amount)
                {
                    throw new InvalidOperationException(
                        $"Insufficient wallet balance. Available: {wallet?.CreditBalance ?? 0}, Requested: {amount}");
                }

                wallet.CreditBalance -= amount;
                wallet.TotalSpent += amount;
                wallet.Version++;
                wallet.UpdatedAt = DateTime.UtcNow;

                var ledger = new ClientWalletLedger
                {
                    Id = ObjectId.GenerateNewId().ToString(),
                    ClientId = clientId,
                    Type = ClientLedgerEntryType.RefundDebit,
                    Amount = -amount,
                    ClientOrderId = orderId,
                    Description = description,
                    BalanceAfter = wallet.CreditBalance,
                    CreatedAt = DateTime.UtcNow
                };

                _dbContext.ClientWalletLedgers.Add(ledger);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("Debited {Amount} from client {ClientId} wallet. New balance: {Balance}. Reason: {Description}",
                    amount, clientId, wallet.CreditBalance, description);
            });
        }

        public async Task<List<ClientWalletLedgerDTO>> GetLedgerHistoryAsync(string clientId)
        {
            if (string.IsNullOrWhiteSpace(clientId))
                throw new ArgumentException("ClientId cannot be null or empty.", nameof(clientId));

            var entries = await _dbContext.ClientWalletLedgers
                .Where(l => l.ClientId == clientId)
                .OrderByDescending(l => l.CreatedAt)
                .ToListAsync();

            return entries.Select(e => new ClientWalletLedgerDTO
            {
                Id = e.Id,
                Type = e.Type,
                Amount = e.Amount,
                ClientOrderId = e.ClientOrderId,
                Description = e.Description,
                BalanceAfter = e.BalanceAfter,
                CreatedAt = e.CreatedAt
            }).ToList();
        }
    }
}
