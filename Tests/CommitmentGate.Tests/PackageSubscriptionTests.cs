using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Application.DTOs;
using Application.Interfaces.Content;
using Application.Interfaces.Email;
using Domain.Entities;
using Infrastructure.Content.Data;
using Infrastructure.Content.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using MongoDB.EntityFrameworkCore.Extensions;
using MongoDB.Bson;
using Xunit;

namespace CommitmentGate.Tests;

/// <summary>
/// Phase 10 — recurring Package billing: BillingType on PackageRequest, token capture on the
/// first (Recurring) charge, the repointed PackageSubscription renewal engine, and the
/// end-to-end proof that the caregiver reference is never derived from Gig anywhere in this
/// flow — it comes solely from the existing, unchanged Assignment/Accept pipeline.
///
/// Same network-avoidance convention as the rest of this suite: FlutterwaveService is real but
/// keyed with fake test credentials, so any call that genuinely reaches the live Flutterwave API
/// (token verification, tokenized charge) gets a real, deterministic rejection — which this
/// suite asserts on directly rather than needing live sandbox credentials. Storage/retrieval of
/// a token, once captured, is tested directly and deterministically at the data layer.
/// </summary>
public class PackageSubscriptionTests
{
    private static CareProDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<CareProDbContext>()
            .UseMongoDB("mongodb://127.0.0.1:27018", $"carepro_pkgsub_tests_{Guid.NewGuid():N}")
            .Options;
        return new TestPackagePaymentDbContext(options);
    }

    private static FlutterwaveService CreateFakeFlutterwave()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Flutterwave:SecretKey"] = "test-secret",
                ["Flutterwave:PublicKey"] = "test-public",
                ["Flutterwave:EncryptionKey"] = "test-encryption",
                ["Flutterwave:WebhookSecretHash"] = "test-webhook-hash",
                ["FrontendUrl"] = "https://example.com"
            })
            .Build();
        return new FlutterwaveService(config, Mock.Of<ILogger<FlutterwaveService>>());
    }

    private static PackageSubscriptionService CreateSubscriptionService(CareProDbContext db)
        => new(db, CreateFakeFlutterwave(), Mock.Of<IMediator>(), Mock.Of<ILogger<PackageSubscriptionService>>());

    private static PackagePaymentService CreatePaymentService(CareProDbContext db, IPackageService packageService)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["FrontendUrl"] = "https://example.com" })
            .Build();

        return new PackagePaymentService(
            db,
            packageService,
            new PackageRequestService(db, Mock.Of<ILogger<PackageRequestService>>()),
            CreateSubscriptionService(db),
            CreateFakeFlutterwave(),
            config,
            Mock.Of<ILogger<PackagePaymentService>>());
    }

    private static Client SeedClient(CareProDbContext db)
    {
        var client = new Client
        {
            Id = ObjectId.GenerateNewId(),
            FirstName = "Recurring", LastName = "Client",
            Email = "recurring-client@example.com",
            Role = "Client", Password = "x", Status = true, CreatedAt = DateTime.UtcNow
        };
        db.Clients.Add(client);
        return client;
    }

    private static Package SeedPackage(CareProDbContext db, decimal basePrice = 60000m)
    {
        var package = new Package
        {
            Id = ObjectId.GenerateNewId(),
            Category = PackageCategories.AdultElderCare,
            TierLabel = "Standard",
            RequiredCaregiverType = CaregiverType.RegisteredNurse,
            BasePrice = basePrice,
            Description = "Recurring test package",
            IsActive = true
        };
        db.Packages.Add(package);
        return package;
    }

    private static Caregiver SeedCaregiver(CareProDbContext db, CaregiverType type)
    {
        var cg = new Caregiver
        {
            Id = ObjectId.GenerateNewId(),
            FirstName = "Case", LastName = "Giver",
            Email = $"cg-{Guid.NewGuid():N}@example.com",
            Password = "x", Role = "Caregiver", Status = true,
            IsAvailable = true, IsIdentityVerified = true,
            CaregiverType = type, CreatedAt = DateTime.UtcNow
        };
        db.CareGivers.Add(cg);
        return cg;
    }

    // ───────────────────────── CompleteRecurringPackagePaymentAsync ─────────────────────────

    [Fact]
    public async Task CompleteRecurring_CreatesPackageRequest_WithNoCaregiver_AndCorrectBillingType()
    {
        using var db = CreateDb();
        var package = SeedPackage(db);
        var client = SeedClient(db);
        await db.SaveChangesAsync();

        var packageServiceMock = new Mock<IPackageService>();
        packageServiceMock.Setup(x => x.GetPackageByIdAsync(package.Id.ToString())).ReturnsAsync(new PackageDTO
        {
            Id = package.Id.ToString(),
            Category = package.Category,
            TierLabel = package.TierLabel,
            RequiredCaregiverType = package.RequiredCaregiverType.ToString(),
            BasePrice = package.BasePrice,
            IsActive = true
        });

        var paymentService = CreatePaymentService(db, packageServiceMock.Object);

        db.PendingPackagePayments.Add(new PendingPackagePayment
        {
            Id = ObjectId.GenerateNewId(),
            TransactionReference = "CAREPRO-PKG-RECURRING-TEST-1",
            ClientId = client.Id.ToString(),
            PackageId = package.Id.ToString(),
            BillingType = PackageRequestBillingTypes.Recurring,
            BasePrice = 60000m,
            TotalAmount = 60000m,
            Currency = "NGN",
            Email = client.Email,
            RedirectUrl = "https://example.com",
            PaymentLink = "https://pay.test/recurring-1",
            Status = PendingPackagePaymentStatus.Pending,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        // VerifyAndExtractTokenAsync will genuinely reach the real Flutterwave API with a fake
        // test key and fake transaction id — it cannot succeed in this environment, and returns
        // no token. This proves the completion path degrades correctly (still creates the real
        // PackageRequest + PackageSubscription) rather than failing the whole webhook response
        // when token capture can't succeed — see the LogCritical in the implementation for the
        // loud-follow-up-needed signal that fires in this exact case.
        var result = await paymentService.CompleteRecurringPackagePaymentAsync(
            "CAREPRO-PKG-RECURRING-TEST-1", "flw-tx-recurring-1", 60000m);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value!.PackageRequestId);
        Assert.NotNull(result.Value.PackageSubscriptionId);

        var packageRequest = await db.PackageRequests.FirstOrDefaultAsync(
            pr => pr.Id == ObjectId.Parse(result.Value.PackageRequestId!));
        Assert.NotNull(packageRequest);
        Assert.Equal(PackageRequestBillingTypes.Recurring, packageRequest!.BillingType);
        Assert.Null(packageRequest.ConfirmedCaregiverId); // no caregiver at creation — confirmed constraint
        Assert.Equal(PackageRequestStatuses.Pending, packageRequest.Status);

        var subscription = await db.PackageSubscriptions.FirstOrDefaultAsync(
            s => s.Id == ObjectId.Parse(result.Value.PackageSubscriptionId!));
        Assert.NotNull(subscription);
        Assert.Equal(packageRequest.Id.ToString(), subscription!.PackageRequestId);
        Assert.Equal(60000m, subscription.RecurringAmount);
        Assert.Equal(0, subscription.BillingCyclesCompleted);
        Assert.NotNull(subscription.NextChargeDate);
    }

    [Fact]
    public async Task CompleteRecurring_CalledForOneTimePayment_IsRefusedDefensively()
    {
        using var db = CreateDb();
        var package = SeedPackage(db);
        var client = SeedClient(db);

        db.PendingPackagePayments.Add(new PendingPackagePayment
        {
            Id = ObjectId.GenerateNewId(),
            TransactionReference = "CAREPRO-PKG-ONETIME-GUARD",
            ClientId = client.Id.ToString(),
            PackageId = package.Id.ToString(),
            BillingType = PackageRequestBillingTypes.OneTime, // NOT Recurring
            TotalAmount = 60000m,
            Currency = "NGN",
            Email = client.Email,
            RedirectUrl = "https://example.com",
            Status = PendingPackagePaymentStatus.Pending,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var packageServiceMock = new Mock<IPackageService>();
        var paymentService = CreatePaymentService(db, packageServiceMock.Object);

        var result = await paymentService.CompleteRecurringPackagePaymentAsync("CAREPRO-PKG-ONETIME-GUARD", "flw-tx", 60000m);

        Assert.False(result.IsSuccess);
        Assert.Contains("one-time payment path", string.Join(" ", result.Errors));
        Assert.Empty(db.PackageRequests.ToList());
        Assert.Empty(db.PackageSubscriptions.ToList());
    }

    [Fact]
    public async Task CompleteOneTime_CalledForRecurringPayment_IsRefusedDefensively()
    {
        using var db = CreateDb();
        var package = SeedPackage(db);
        var client = SeedClient(db);

        db.PendingPackagePayments.Add(new PendingPackagePayment
        {
            Id = ObjectId.GenerateNewId(),
            TransactionReference = "CAREPRO-PKG-RECURRING-GUARD",
            ClientId = client.Id.ToString(),
            PackageId = package.Id.ToString(),
            BillingType = PackageRequestBillingTypes.Recurring, // NOT OneTime
            TotalAmount = 60000m,
            Currency = "NGN",
            Email = client.Email,
            RedirectUrl = "https://example.com",
            Status = PendingPackagePaymentStatus.Pending,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var packageServiceMock = new Mock<IPackageService>();
        var paymentService = CreatePaymentService(db, packageServiceMock.Object);

        var result = await paymentService.CompletePackagePaymentAsync("CAREPRO-PKG-RECURRING-GUARD", "flw-tx", 60000m);

        Assert.False(result.IsSuccess);
        Assert.Contains("recurring payment path", string.Join(" ", result.Errors));
        Assert.Empty(db.PackageRequests.ToList());
    }

    // ───────────────────────── PackageSubscriptionService: storage + due-for-billing ─────────────────────────

    [Fact]
    public async Task CreatePackageSubscription_StoresToken_AndBecomesDueForBillingOnceMatured()
    {
        using var db = CreateDb();
        var client = SeedClient(db);
        var packageRequestId = ObjectId.GenerateNewId().ToString();
        await db.SaveChangesAsync();

        var service = CreateSubscriptionService(db);

        var subscription = await service.CreatePackageSubscriptionAsync(new CreatePackageSubscriptionRequest
        {
            PackageRequestId = packageRequestId,
            ClientId = client.Id.ToString(),
            RecurringAmount = 60000m,
            Currency = "NGN",
            Email = client.Email,
            FlutterwavePaymentToken = "fake-captured-token-abc",
            CardLastFour = "4081",
            CardBrand = "VISA"
        });

        Assert.Equal("fake-captured-token-abc", subscription.FlutterwavePaymentToken);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);

        // Not due yet — NextChargeDate is 30 days out.
        var dueNow = await service.GetPackageSubscriptionsDueForBillingAsync();
        Assert.DoesNotContain(dueNow, s => s.Id == subscription.Id);

        // Simulate 30 days passing.
        var stored = await db.PackageSubscriptions.FirstAsync(s => s.Id == subscription.Id);
        stored.NextChargeDate = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();

        var dueAfterMaturity = await service.GetPackageSubscriptionsDueForBillingAsync();
        Assert.Contains(dueAfterMaturity, s => s.Id == subscription.Id);
    }

    [Fact]
    public async Task GetDueForBilling_ExcludesSubscriptionsWithNoToken()
    {
        using var db = CreateDb();
        var client = SeedClient(db);
        await db.SaveChangesAsync();

        var service = CreateSubscriptionService(db);

        // Mirrors the real degraded case: token capture failed on the initial charge.
        await service.CreatePackageSubscriptionAsync(new CreatePackageSubscriptionRequest
        {
            PackageRequestId = ObjectId.GenerateNewId().ToString(),
            ClientId = client.Id.ToString(),
            RecurringAmount = 60000m,
            Currency = "NGN",
            Email = client.Email,
            FlutterwavePaymentToken = null
        });

        var noTokenSub = await db.PackageSubscriptions.FirstAsync();
        noTokenSub.NextChargeDate = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();

        var due = await service.GetPackageSubscriptionsDueForBillingAsync();
        Assert.Empty(due); // never billable without a token — renewal engine correctly skips it
    }

    // ───────────────────────── ProcessRecurringPackageChargeAsync ─────────────────────────

    [Fact]
    public async Task ProcessRecurringCharge_ReachesRealFlutterwave_FailsCleanly_NeverTouchesClientOrders()
    {
        using var db = CreateDb();
        var client = SeedClient(db);
        await db.SaveChangesAsync();

        var service = CreateSubscriptionService(db);
        var subscription = await service.CreatePackageSubscriptionAsync(new CreatePackageSubscriptionRequest
        {
            PackageRequestId = ObjectId.GenerateNewId().ToString(),
            ClientId = client.Id.ToString(),
            RecurringAmount = 60000m,
            Currency = "NGN",
            Email = client.Email,
            FlutterwavePaymentToken = "fake-token-for-charge-test"
        });

        var result = await service.ProcessRecurringPackageChargeAsync(subscription.Id.ToString());

        // The fake test key means ChargeWithToken is genuinely rejected by the live Flutterwave
        // API — this proves the renewal engine actually reaches the charge call with the stored
        // token/email/amount (not a guard-clause short-circuit), and handles the real failure
        // response correctly (classifies it, records it, schedules backoff) rather than crashing.
        Assert.False(result.IsSuccess);

        var stored = await db.PackageSubscriptions.FirstAsync(s => s.Id == subscription.Id);
        Assert.Single(stored.PaymentHistory);
        Assert.Equal(1, stored.FailedChargeAttempts);
        Assert.NotEqual(SubscriptionStatus.Charging, stored.Status); // restored out of Charging
        Assert.True(stored.NextChargeDate > DateTime.UtcNow); // backoff scheduled forward, not left due

        // The structural guarantee this whole phase exists for: no ClientOrder, ever.
        Assert.Empty(db.ClientOrders.ToList());
    }

    [Fact]
    public async Task ProcessRecurringCharge_IdempotencyGuard_SameCycleAttempt_NeverDoubleCharges()
    {
        using var db = CreateDb();
        var client = SeedClient(db);
        await db.SaveChangesAsync();

        var service = CreateSubscriptionService(db);
        var subscription = await service.CreatePackageSubscriptionAsync(new CreatePackageSubscriptionRequest
        {
            PackageRequestId = ObjectId.GenerateNewId().ToString(),
            ClientId = client.Id.ToString(),
            RecurringAmount = 60000m,
            Currency = "NGN",
            Email = client.Email,
            FlutterwavePaymentToken = "fake-token-idempotency"
        });

        // Pre-seed a "successful" attempt for cycle 1 / attempt 1 directly (simulating an
        // already-completed renewal), matching the exact idempotency key format the engine uses.
        var stored = await db.PackageSubscriptions.FirstAsync(s => s.Id == subscription.Id);
        var key = $"pkg-renewal:{stored.Id}:1:1";
        stored.PaymentHistory.Add(new PackageSubscriptionPaymentRecord
        {
            TransactionReference = "CAREPRO-PKGSUB-ALREADY-DONE",
            RecurringAttemptKey = key,
            Amount = 60000m,
            Status = "successful",
            BillingCycleNumber = 1,
            AttemptedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await service.ProcessRecurringPackageChargeAsync(subscription.Id.ToString());

        Assert.True(result.IsSuccess);
        Assert.Equal("CAREPRO-PKGSUB-ALREADY-DONE", result.Value!.TransactionReference);
        // Still exactly one payment history entry — no second charge was attempted.
        var afterward = await db.PackageSubscriptions.FirstAsync(s => s.Id == subscription.Id);
        Assert.Single(afterward.PaymentHistory);
    }

    [Fact]
    public async Task ProcessRecurringCharge_ChargingStateGuard_SkipsConcurrentRun()
    {
        using var db = CreateDb();
        var client = SeedClient(db);
        await db.SaveChangesAsync();

        var service = CreateSubscriptionService(db);
        var subscription = await service.CreatePackageSubscriptionAsync(new CreatePackageSubscriptionRequest
        {
            PackageRequestId = ObjectId.GenerateNewId().ToString(),
            ClientId = client.Id.ToString(),
            RecurringAmount = 60000m,
            Currency = "NGN",
            Email = client.Email,
            FlutterwavePaymentToken = "fake-token-charging-guard"
        });

        var stored = await db.PackageSubscriptions.FirstAsync(s => s.Id == subscription.Id);
        stored.Status = SubscriptionStatus.Charging; // simulate a concurrent in-flight charge
        await db.SaveChangesAsync();

        var result = await service.ProcessRecurringPackageChargeAsync(subscription.Id.ToString());

        Assert.False(result.IsSuccess);
        Assert.Contains("already in progress", string.Join(" ", result.Errors));
    }

    // ───────────────────────── End-to-end: payment → Assignment/Accept → caregiver visible ─────────────────────────

    [Fact]
    public async Task EndToEnd_RecurringPurchase_ThenAssignAndAccept_CaregiverAppears_NoGigOrClientOrderAnywhere()
    {
        using var db = CreateDb();
        var package = SeedPackage(db);
        var client = SeedClient(db);
        var caregiver = SeedCaregiver(db, package.RequiredCaregiverType);
        await db.SaveChangesAsync();

        var packageServiceMock = new Mock<IPackageService>();
        packageServiceMock.Setup(x => x.GetPackageByIdAsync(package.Id.ToString())).ReturnsAsync(new PackageDTO
        {
            Id = package.Id.ToString(),
            Category = package.Category,
            TierLabel = package.TierLabel,
            RequiredCaregiverType = package.RequiredCaregiverType.ToString(),
            BasePrice = package.BasePrice,
            IsActive = true
        });

        var paymentService = CreatePaymentService(db, packageServiceMock.Object);

        db.PendingPackagePayments.Add(new PendingPackagePayment
        {
            Id = ObjectId.GenerateNewId(),
            TransactionReference = "CAREPRO-PKG-RECURRING-E2E",
            ClientId = client.Id.ToString(),
            PackageId = package.Id.ToString(),
            BillingType = PackageRequestBillingTypes.Recurring,
            TotalAmount = package.BasePrice,
            Currency = "NGN",
            Email = client.Email,
            RedirectUrl = "https://example.com",
            Status = PendingPackagePaymentStatus.Pending,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        // ── Step 1: webhook-driven completion (the new Phase 10 piece) ──
        var completion = await paymentService.CompleteRecurringPackagePaymentAsync(
            "CAREPRO-PKG-RECURRING-E2E", "flw-tx-e2e", package.BasePrice);
        Assert.True(completion.IsSuccess);
        var packageRequestId = completion.Value!.PackageRequestId!;

        var requestBeforeAssignment = await db.PackageRequests.FirstAsync(pr => pr.Id == ObjectId.Parse(packageRequestId));
        Assert.Null(requestBeforeAssignment.ConfirmedCaregiverId);
        Assert.Equal(PackageRequestBillingTypes.Recurring, requestBeforeAssignment.BillingType);

        // ── Step 2: staff assign + caregiver accepts (existing Phase 4 flow, completely unchanged) ──
        var readiness = new Mock<ICaregiverReadinessService>();
        readiness.Setup(r => r.GetReadinessAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool?>()))
            .ReturnsAsync(new CaregiverReadinessResult { IsReady = true });

        var assignmentService = new AssignmentService(
            db, Mock.Of<IMediator>(), Mock.Of<IEmailService>(), readiness.Object,
            Mock.Of<IPackageContractService>(), Mock.Of<ILogger<AssignmentService>>());
        var requestService = new PackageRequestService(db, Mock.Of<ILogger<PackageRequestService>>());

        var assignment = await assignmentService.AssignAsync(packageRequestId, caregiver.Id.ToString(), "admin-1", "ops@carepro.test", "staff", 88.0);
        Assert.Equal(AssignmentStatuses.PendingAcceptance, assignment.Status);

        var acceptResult = await assignmentService.AcceptAsync(assignment.Id, caregiver.Id.ToString());
        Assert.True(acceptResult.Success);

        // ── Step 3: the caregiver reference now correctly appears, and it came from Assignment, never Gig ──
        var confirmedRequest = await requestService.GetForClientAsync(client.Id.ToString(), packageRequestId);
        Assert.Equal(PackageRequestStatuses.Confirmed, confirmedRequest.Status);
        Assert.NotNull(confirmedRequest.ConfirmedCaregiver);
        Assert.Equal(caregiver.Id.ToString(), confirmedRequest.ConfirmedCaregiver!.CaregiverId);

        var finalAssignment = await db.Assignments.FirstAsync(a => a.Id == ObjectId.Parse(assignment.Id));
        Assert.Equal(caregiver.Id.ToString(), finalAssignment.CaregiverId); // set directly by AssignAsync's own parameter

        // ── Structural proof this never touched the Gig/ClientOrder chokepoint ──
        Assert.Empty(db.Gigs.ToList());        // no caregiver ever had (or needed) a Gig in this flow
        Assert.Empty(db.ClientOrders.ToList()); // no ClientOrder was ever created

        var subscription = await db.PackageSubscriptions.FirstAsync(s => s.PackageRequestId == packageRequestId);
        Assert.Equal(client.Id.ToString(), subscription.ClientId);
        // PackageSubscription has no CaregiverId property at all — compile-time proof, not just
        // a runtime assertion, that this record can never carry a Gig-derived (or any) caregiver id.
    }
}
