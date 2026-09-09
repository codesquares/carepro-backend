using Application.DTOs;
using Application.Interfaces.Content;
using Application.Interfaces.Email;
using Domain.Entities;
using Infrastructure.Content.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using MongoDB.Bson;
using Xunit;

namespace CommitmentGate.Tests;

/// <summary>
/// Phase 7.2 — FullRefund / PartialRefund dispute resolutions execute a real wallet
/// credit. (7.1's "caregiver wallet credit at internal-assignment confirmation" tests
/// were removed in Phase 9.8, along with PackageSettlementService itself — package
/// assignments are now paid exclusively through admin-approved Payroll records.)
/// Real local MongoDB (replica set on 127.0.0.1:27018).
/// </summary>
public class Phase7WalletAndRefundTests
{
    private static Infrastructure.Content.Data.CareProDbContext CreateDb(string name)
    {
        var options = new DbContextOptionsBuilder<Infrastructure.Content.Data.CareProDbContext>()
            .UseMongoDB("mongodb://127.0.0.1:27018", name).Options;
        return new TestCareProDbContext(options);
    }

    private static string NewDbName() => $"carepro_phase7_{Guid.NewGuid():N}";

    private static WalletLockManager Lock() => new();

    private static CaregiverWalletService CgWallet(Infrastructure.Content.Data.CareProDbContext db) =>
        new(db, Mock.Of<ICareGiverService>(), Mock.Of<ILogger<CaregiverWalletService>>(), Lock());

    private static ClientWalletService ClWallet(Infrastructure.Content.Data.CareProDbContext db) =>
        new(db, Mock.Of<ILogger<ClientWalletService>>(), Lock());

    private static EarningsLedgerService Ledger(Infrastructure.Content.Data.CareProDbContext db, ICaregiverWalletService w) =>
        new(db, w, Mock.Of<ILogger<EarningsLedgerService>>());

    // ═══════════════════════ 7.2 — dispute refund execution ═══════════════════════

    private static DisputeService CreateDisputeService(Infrastructure.Content.Data.CareProDbContext db, Mock<IMediator>? mediator = null)
    {
        var cgWallet = CgWallet(db);
        return new DisputeService(
            db, (mediator ?? new Mock<IMediator>()).Object,
            Ledger(db, cgWallet), cgWallet, ClWallet(db),
            Mock.Of<ILogger<DisputeService>>(), Mock.Of<IEmailService>());
    }

    private static async Task<(string disputeId, string clientId, string caregiverId, string orderId)>
        SeedDisputedOrder(Infrastructure.Content.Data.CareProDbContext db, int orderAmount = 50000,
            decimal caregiverPrePending = 30000m)
    {
        var clientId = ObjectId.GenerateNewId().ToString();
        var caregiverId = ObjectId.GenerateNewId().ToString();
        var order = new ClientOrder
        {
            Id = ObjectId.GenerateNewId(),
            ClientId = clientId, CaregiverId = caregiverId, GigId = ObjectId.GenerateNewId().ToString(),
            PaymentOption = "one-time", TransactionId = "TX-" + Guid.NewGuid().ToString("N"),
            Amount = orderAmount, OrderFee = orderAmount, CaregiverSharePercentageAtCreation = 0.60m,
            ClientOrderStatus = "Disputed", HasDispute = true, OrderCreatedAt = DateTime.UtcNow
        };
        db.ClientOrders.Add(order);
        var dispute = new Dispute
        {
            Id = ObjectId.GenerateNewId(),
            OrderId = order.Id.ToString(),
            DisputeType = DisputeType.Order, Category = "IncorrectAmount",
            Reason = "Charged more than agreed", RaisedBy = clientId,
            ClientId = clientId, CaregiverId = caregiverId, Status = DisputeStatus.Open,
            CreatedAt = DateTime.UtcNow
        };
        db.Disputes.Add(dispute);
        await db.SaveChangesAsync();

        if (caregiverPrePending > 0)
            await CgWallet(db).CreditOrderReceivedAsync(caregiverId, caregiverPrePending, false);

        return (dispute.Id.ToString(), clientId, caregiverId, order.Id.ToString());
    }

    private static ResolveDisputeRequest Resolve(string action, decimal? amount = null) => new()
    {
        ResolutionAction = action,
        AdminNotes = "Reviewed billing records and platform logs; client's claim is valid.",
        ResolutionSummary = "Refund issued to client wallet.",
        RefundAmount = amount
    };

    [Fact]
    public async Task ResolveDispute_FullRefund_CreditsClientWallet_AndClawsBackCaregiverPending()
    {
        var dbName = NewDbName();
        string disputeId, clientId, caregiverId;

        using (var db = CreateDb(dbName))
        {
            (disputeId, clientId, caregiverId, _) = await SeedDisputedOrder(db, orderAmount: 50000, caregiverPrePending: 30000m);
            var svc = CreateDisputeService(db);

            var result = await svc.ResolveDisputeAsync(disputeId, Resolve(DisputeResolutionAction.FullRefund), "admin-9");
            Assert.Equal(50000m, result.RefundAmount);
            Assert.NotNull(result.RefundExecutedAt);
        }

        using (var db = CreateDb(dbName))
        {
            // Real client wallet credit.
            var clientWallet = await ClWallet(db).GetOrCreateWalletAsync(clientId);
            Assert.Equal(50000m, clientWallet.CreditBalance);

            var ledgers = await db.ClientWalletLedgers.Where(l => l.ClientId == clientId).ToListAsync();
            Assert.Contains(ledgers, l => l.Type == ClientLedgerEntryType.DisputeRefundCredit && l.Amount == 50000m);

            // Caregiver's pending share clawed back: 30000 - (50000 * 0.60 = 30000) = 0.
            var cgWallet = await CgWallet(db).GetOrCreateWalletAsync(caregiverId);
            Assert.Equal(0m, cgWallet.PendingBalance);
            Assert.Contains(await db.EarningsLedger.Where(e => e.CaregiverId == caregiverId).ToListAsync(),
                e => e.Type == LedgerEntryType.Refund);

            var dispute = await db.Disputes.FirstAsync(d => d.Id == ObjectId.Parse(disputeId));
            Assert.Equal(50000m, dispute.RefundAmount);
            Assert.NotNull(dispute.RefundExecutedAt);
        }
    }

    [Fact]
    public async Task ResolveDispute_PartialRefund_CreditsExactAdminEnteredAmount()
    {
        using var db = CreateDb(NewDbName());
        var (disputeId, clientId, caregiverId, _) = await SeedDisputedOrder(db, orderAmount: 50000, caregiverPrePending: 30000m);

        var result = await CreateDisputeService(db).ResolveDisputeAsync(
            disputeId, Resolve(DisputeResolutionAction.PartialRefund, amount: 20000m), "admin-9");

        Assert.Equal(20000m, result.RefundAmount);

        var clientWallet = await ClWallet(db).GetOrCreateWalletAsync(clientId);
        Assert.Equal(20000m, clientWallet.CreditBalance);

        // Caregiver clawback proportional: 20000 * 0.60 = 12000 → pending 30000 - 12000 = 18000.
        var cgWallet = await CgWallet(db).GetOrCreateWalletAsync(caregiverId);
        Assert.Equal(18000m, cgWallet.PendingBalance);
    }

    [Fact]
    public async Task ResolveDispute_PartialRefund_MissingAmount_Throws()
    {
        using var db = CreateDb(NewDbName());
        var (disputeId, _, _, _) = await SeedDisputedOrder(db);
        await Assert.ThrowsAsync<ArgumentException>(() => CreateDisputeService(db)
            .ResolveDisputeAsync(disputeId, Resolve(DisputeResolutionAction.PartialRefund, amount: null), "admin-9"));
    }

    [Fact]
    public async Task ResolveDispute_PartialRefund_AmountExceedingOrder_Throws()
    {
        using var db = CreateDb(NewDbName());
        var (disputeId, _, _, _) = await SeedDisputedOrder(db, orderAmount: 50000);
        await Assert.ThrowsAsync<ArgumentException>(() => CreateDisputeService(db)
            .ResolveDisputeAsync(disputeId, Resolve(DisputeResolutionAction.PartialRefund, amount: 60000m), "admin-9"));
    }

    [Fact]
    public async Task ResolveDispute_PartialRefund_NegativeAmount_Throws()
    {
        using var db = CreateDb(NewDbName());
        var (disputeId, _, _, _) = await SeedDisputedOrder(db);
        await Assert.ThrowsAsync<ArgumentException>(() => CreateDisputeService(db)
            .ResolveDisputeAsync(disputeId, Resolve(DisputeResolutionAction.PartialRefund, amount: -5m), "admin-9"));
    }

    [Fact]
    public async Task ResolveDispute_NonRefundAction_DoesNotCreditWallet()
    {
        using var db = CreateDb(NewDbName());
        var (disputeId, clientId, _, _) = await SeedDisputedOrder(db);

        await CreateDisputeService(db).ResolveDisputeAsync(disputeId, Resolve(DisputeResolutionAction.FundsReleased), "admin-9");

        var clientWallet = await ClWallet(db).GetOrCreateWalletAsync(clientId);
        Assert.Equal(0m, clientWallet.CreditBalance);
        var dispute = await db.Disputes.FirstAsync(d => d.Id == ObjectId.Parse(disputeId));
        Assert.Null(dispute.RefundExecutedAt);
    }
}
