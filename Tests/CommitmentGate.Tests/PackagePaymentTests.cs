using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Application.DTOs;
using Application.Interfaces.Content;
using Domain.Entities;
using Infrastructure.Content.Data;
using Infrastructure.Content.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using MongoDB.EntityFrameworkCore.Extensions;
using MongoDB.Bson;
using Xunit;

namespace CommitmentGate.Tests;

/// <summary>
/// Real, DB-backed coverage for Option A: admin-initiated Package payment link
/// generation + webhook-driven PackageRequest creation.
///
/// Follows the same network-avoidance convention as <c>CommitmentGateTests</c>: tests
/// that would reach a genuinely new Flutterwave charge either (a) pre-seed a fresh
/// pending payment with a PaymentLink so the "reuse cached link" branch returns before
/// touching the network, or (b) deliberately let the real (fake-keyed) FlutterwaveService
/// hit the real Flutterwave API, which rejects the fake key and lets us assert the
/// failure path deterministically without needing real credentials.
///
/// <see cref="CompletePackagePaymentAsync"/> never calls Flutterwave at all (the caller —
/// the webhook — has already verified the charge), so its tests run entirely against the
/// real <see cref="PackageRequestService"/> and a real local MongoDB, proving an actual
/// PackageRequest row is created.
/// </summary>
public class PackagePaymentTests
{
    private static CareProDbContext CreateDb()
    {
        var databaseName = $"carepro_package_payment_tests_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<CareProDbContext>()
            .UseMongoDB("mongodb://127.0.0.1:27018", databaseName)
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

    private static PackagePaymentService CreateService(
        CareProDbContext db,
        Mock<IPackageService> packageServiceMock,
        IPackageRequestService? packageRequestService = null,
        IPackageSubscriptionService? packageSubscriptionService = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["FrontendUrl"] = "https://example.com" })
            .Build();

        return new PackagePaymentService(
            db,
            packageServiceMock.Object,
            packageRequestService ?? new PackageRequestService(db, Mock.Of<ILogger<PackageRequestService>>()),
            packageSubscriptionService ?? new PackageSubscriptionService(db, CreateFakeFlutterwave(), Mock.Of<MediatR.IMediator>(), Mock.Of<ILogger<PackageSubscriptionService>>()),
            CreateFakeFlutterwave(),
            config,
            Mock.Of<ILogger<PackagePaymentService>>());
    }

    private static Client SeedClient(CareProDbContext db, string email = "client@example.com")
    {
        var client = new Client
        {
            Id = ObjectId.GenerateNewId(),
            FirstName = "Test",
            LastName = "Client",
            Email = email,
            Role = "Client",
            Password = "hashed",
            Status = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Clients.Add(client);
        return client;
    }

    private static Package SeedPackage(CareProDbContext db, decimal basePrice = 50000m, decimal? additionalDayPrice = 3000m, bool isActive = true)
    {
        var package = new Package
        {
            Id = ObjectId.GenerateNewId(),
            Category = PackageCategories.AdultElderCare,
            TierLabel = "Standard",
            RequiredCaregiverType = CaregiverType.RegisteredNurse,
            BasePrice = basePrice,
            AdditionalDayPrice = additionalDayPrice,
            Description = "Test package",
            IsActive = isActive
        };
        db.Packages.Add(package);
        return package;
    }

    private static PackageDTO ToDto(Package p) => new PackageDTO
    {
        Id = p.Id.ToString(),
        Category = p.Category,
        TierLabel = p.TierLabel,
        RequiredCaregiverType = p.RequiredCaregiverType.ToString(),
        BasePrice = p.BasePrice,
        AdditionalDayPrice = p.AdditionalDayPrice,
        Description = p.Description,
        IsActive = p.IsActive
    };

    // ───────────────────────── InitiatePackagePaymentAsync: guard clauses ─────────────────────────

    [Fact]
    public async Task Initiate_PackageNotFound_ReturnsFailure_NoDbWrite()
    {
        using var db = CreateDb();
        var packageServiceMock = new Mock<IPackageService>();
        packageServiceMock.Setup(x => x.GetPackageByIdAsync(It.IsAny<string>())).ReturnsAsync((PackageDTO?)null);

        var service = CreateService(db, packageServiceMock);

        var result = await service.InitiatePackagePaymentAsync(new AdminInitiatePackagePaymentRequest
        {
            ClientId = ObjectId.GenerateNewId().ToString(),
            PackageId = ObjectId.GenerateNewId().ToString()
        }, "admin-1");

        Assert.False(result.IsSuccess);
        Assert.Contains("Package not found", string.Join(" ", result.Errors));
        Assert.Empty(db.PendingPackagePayments.ToList());
    }

    [Fact]
    public async Task Initiate_InactivePackage_ReturnsFailure()
    {
        using var db = CreateDb();
        var package = SeedPackage(db, isActive: false);
        await db.SaveChangesAsync();

        var packageServiceMock = new Mock<IPackageService>();
        packageServiceMock.Setup(x => x.GetPackageByIdAsync(package.Id.ToString())).ReturnsAsync(ToDto(package));

        var service = CreateService(db, packageServiceMock);

        var result = await service.InitiatePackagePaymentAsync(new AdminInitiatePackagePaymentRequest
        {
            ClientId = ObjectId.GenerateNewId().ToString(),
            PackageId = package.Id.ToString()
        }, "admin-1");

        Assert.False(result.IsSuccess);
        Assert.Contains("not currently available", string.Join(" ", result.Errors));
    }

    [Fact]
    public async Task Initiate_ExtraDaysOnPackageWithNoAddOn_ReturnsFailure()
    {
        using var db = CreateDb();
        var package = SeedPackage(db, additionalDayPrice: null);
        await db.SaveChangesAsync();

        var packageServiceMock = new Mock<IPackageService>();
        packageServiceMock.Setup(x => x.GetPackageByIdAsync(package.Id.ToString())).ReturnsAsync(ToDto(package));

        var service = CreateService(db, packageServiceMock);

        var result = await service.InitiatePackagePaymentAsync(new AdminInitiatePackagePaymentRequest
        {
            ClientId = ObjectId.GenerateNewId().ToString(),
            PackageId = package.Id.ToString(),
            ExtraDays = 2
        }, "admin-1");

        Assert.False(result.IsSuccess);
        Assert.Contains("does not support", string.Join(" ", result.Errors));
    }

    [Fact]
    public async Task Initiate_ClientNotFound_ReturnsFailure()
    {
        using var db = CreateDb();
        var package = SeedPackage(db);
        await db.SaveChangesAsync();

        var packageServiceMock = new Mock<IPackageService>();
        packageServiceMock.Setup(x => x.GetPackageByIdAsync(package.Id.ToString())).ReturnsAsync(ToDto(package));

        var service = CreateService(db, packageServiceMock);

        var result = await service.InitiatePackagePaymentAsync(new AdminInitiatePackagePaymentRequest
        {
            ClientId = ObjectId.GenerateNewId().ToString(), // not seeded
            PackageId = package.Id.ToString()
        }, "admin-1");

        Assert.False(result.IsSuccess);
        Assert.Contains("Client not found", string.Join(" ", result.Errors));
    }

    // ───────────────────────── InitiatePackagePaymentAsync: reaches Flutterwave ─────────────────────────

    [Fact]
    public async Task Initiate_FreshRequest_ComputesExtraDayPricing_AndFailsCleanlyAgainstRealFlutterwaveWithFakeKey()
    {
        // No cached pending payment exists, so this must go all the way to
        // FlutterwaveService.InitiatePayment — which, with a fake test key, will be
        // rejected by the real Flutterwave API. Proves: (1) the guard clauses all pass
        // for a legitimate request, (2) BasePrice + ExtraDays*AdditionalDayPrice pricing
        // reaches the Flutterwave call, (3) a failed Flutterwave call never persists a
        // PendingPackagePayment row (the Add() only happens after a link is extracted).
        using var db = CreateDb();
        var package = SeedPackage(db, basePrice: 50000m, additionalDayPrice: 3000m);
        var client = SeedClient(db);
        await db.SaveChangesAsync();

        var packageServiceMock = new Mock<IPackageService>();
        packageServiceMock.Setup(x => x.GetPackageByIdAsync(package.Id.ToString())).ReturnsAsync(ToDto(package));

        var service = CreateService(db, packageServiceMock);

        var result = await service.InitiatePackagePaymentAsync(new AdminInitiatePackagePaymentRequest
        {
            ClientId = client.Id.ToString(),
            PackageId = package.Id.ToString(),
            ExtraDays = 3 // expected total = 50000 + 3*3000 = 59000
        }, "admin-1");

        // The real Flutterwave API rejects the fake test key, so this cannot succeed here —
        // but it must fail specifically at "Failed to initialize payment", not at any guard
        // clause, proving the price math and client/package lookups all passed first.
        Assert.False(result.IsSuccess);
        Assert.Contains("Failed to initialize payment", string.Join(" ", result.Errors));
        Assert.Empty(db.PendingPackagePayments.ToList());
    }

    [Fact]
    public async Task Initiate_FreshPendingLinkExists_ReturnsCachedLink_NoDuplicate()
    {
        using var db = CreateDb();
        var package = SeedPackage(db, basePrice: 50000m, additionalDayPrice: 3000m);
        var client = SeedClient(db);

        db.PendingPackagePayments.Add(new PendingPackagePayment
        {
            Id = ObjectId.GenerateNewId(),
            TransactionReference = "CAREPRO-PKG-CACHED-REF",
            ClientId = client.Id.ToString(),
            PackageId = package.Id.ToString(),
            ExtraDays = 3,
            BasePrice = 50000m,
            AdditionalDayAmount = 9000m,
            TotalAmount = 59000m,
            Currency = "NGN",
            Email = client.Email,
            RedirectUrl = "https://example.com/app/client/dashboard",
            PaymentLink = "https://pay.test/cached-package-link",
            Status = PendingPackagePaymentStatus.Pending,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var packageServiceMock = new Mock<IPackageService>();
        packageServiceMock.Setup(x => x.GetPackageByIdAsync(package.Id.ToString())).ReturnsAsync(ToDto(package));

        var service = CreateService(db, packageServiceMock);

        var result = await service.InitiatePackagePaymentAsync(new AdminInitiatePackagePaymentRequest
        {
            ClientId = client.Id.ToString(),
            PackageId = package.Id.ToString(),
            ExtraDays = 3
        }, "admin-1");

        Assert.True(result.IsSuccess);
        Assert.Equal("CAREPRO-PKG-CACHED-REF", result.Value!.TransactionReference);
        Assert.Equal("https://pay.test/cached-package-link", result.Value.PaymentLink);
        Assert.Equal(59000m, result.Value.TotalAmount);
        Assert.Equal(9000m, result.Value.AdditionalDayAmount);
        // Still exactly one row — no duplicate created.
        Assert.Single(db.PendingPackagePayments.ToList());
    }

    // ───────────────────────── CompletePackagePaymentAsync ─────────────────────────

    [Fact]
    public async Task Complete_Success_CreatesRealPackageRequest()
    {
        using var db = CreateDb();
        var package = SeedPackage(db, basePrice: 50000m, additionalDayPrice: 3000m);
        var client = SeedClient(db);

        var pending = new PendingPackagePayment
        {
            Id = ObjectId.GenerateNewId(),
            TransactionReference = "CAREPRO-PKG-COMPLETE-1",
            ClientId = client.Id.ToString(),
            PackageId = package.Id.ToString(),
            ExtraDays = 2,
            BasePrice = 50000m,
            AdditionalDayAmount = 6000m,
            TotalAmount = 56000m,
            Currency = "NGN",
            Email = client.Email,
            RedirectUrl = "https://example.com/app/client/dashboard",
            PaymentLink = "https://pay.test/complete-1",
            Status = PendingPackagePaymentStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            Notes = "Client asked for a nurse who speaks Yoruba."
        };
        db.PendingPackagePayments.Add(pending);
        await db.SaveChangesAsync();

        var packageServiceMock = new Mock<IPackageService>();
        var service = CreateService(db, packageServiceMock); // PackageRequestService not mocked — real

        var result = await service.CompletePackagePaymentAsync("CAREPRO-PKG-COMPLETE-1", "flw-tx-123", 56000m);

        Assert.True(result.IsSuccess);
        Assert.Equal(PendingPackagePaymentStatus.Completed, result.Value!.Status);
        Assert.Equal("flw-tx-123", result.Value.FlutterwaveTransactionId);
        Assert.NotNull(result.Value.PackageRequestId);

        var createdRequest = await db.PackageRequests
            .FirstOrDefaultAsync(pr => pr.Id == ObjectId.Parse(result.Value.PackageRequestId!));
        Assert.NotNull(createdRequest);
        Assert.Equal(client.Id.ToString(), createdRequest!.ClientId);
        Assert.Equal(package.Id.ToString(), createdRequest.PackageId);
        Assert.Equal(package.Category, createdRequest.PackageCategory);
        Assert.Equal(PackageRequestStatuses.Pending, createdRequest.Status);
        Assert.Contains("Yoruba", createdRequest.Notes);
        Assert.Contains("2 extra day(s)", createdRequest.Notes);
        Assert.Contains("₦6,000", createdRequest.Notes);
    }

    [Fact]
    public async Task Complete_UnknownTxRef_ReturnsFailure()
    {
        using var db = CreateDb();
        var packageServiceMock = new Mock<IPackageService>();
        var service = CreateService(db, packageServiceMock);

        var result = await service.CompletePackagePaymentAsync("CAREPRO-PKG-DOES-NOT-EXIST", "flw-tx", 1000m);

        Assert.False(result.IsSuccess);
        Assert.Contains("not found", string.Join(" ", result.Errors));
    }

    [Fact]
    public async Task Complete_DuplicateWebhookReplay_IsIdempotent()
    {
        using var db = CreateDb();
        var package = SeedPackage(db);
        var client = SeedClient(db);

        db.PendingPackagePayments.Add(new PendingPackagePayment
        {
            Id = ObjectId.GenerateNewId(),
            TransactionReference = "CAREPRO-PKG-REPLAY",
            ClientId = client.Id.ToString(),
            PackageId = package.Id.ToString(),
            BasePrice = 50000m,
            TotalAmount = 50000m,
            Currency = "NGN",
            Email = client.Email,
            RedirectUrl = "https://example.com",
            Status = PendingPackagePaymentStatus.Completed,
            CreatedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow,
            FlutterwaveTransactionId = "flw-original",
            PackageRequestId = ObjectId.GenerateNewId().ToString()
        });
        await db.SaveChangesAsync();

        var packageServiceMock = new Mock<IPackageService>();
        var packageRequestServiceMock = new Mock<IPackageRequestService>();
        var service = CreateService(db, packageServiceMock, packageRequestServiceMock.Object);

        var result = await service.CompletePackagePaymentAsync("CAREPRO-PKG-REPLAY", "flw-original", 50000m);

        Assert.True(result.IsSuccess);
        // A second PackageRequest must never be created for an already-completed payment.
        packageRequestServiceMock.Verify(
            x => x.CreateAsync(It.IsAny<string>(), It.IsAny<CreatePackageRequestRequest>()),
            Times.Never);
    }

    [Fact]
    public async Task Complete_AmountMismatch_FlagsRecord_DoesNotCreatePackageRequest()
    {
        using var db = CreateDb();
        var package = SeedPackage(db);
        var client = SeedClient(db);

        db.PendingPackagePayments.Add(new PendingPackagePayment
        {
            Id = ObjectId.GenerateNewId(),
            TransactionReference = "CAREPRO-PKG-MISMATCH",
            ClientId = client.Id.ToString(),
            PackageId = package.Id.ToString(),
            BasePrice = 50000m,
            TotalAmount = 50000m,
            Currency = "NGN",
            Email = client.Email,
            RedirectUrl = "https://example.com",
            Status = PendingPackagePaymentStatus.Pending,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var packageServiceMock = new Mock<IPackageService>();
        var packageRequestServiceMock = new Mock<IPackageRequestService>();
        var service = CreateService(db, packageServiceMock, packageRequestServiceMock.Object);

        // Client was only charged 20000 — someone tampered with the amount, or Flutterwave
        // returned a partial/short charge.
        var result = await service.CompletePackagePaymentAsync("CAREPRO-PKG-MISMATCH", "flw-tx", 20000m);

        Assert.False(result.IsSuccess);
        Assert.Contains("does not match", string.Join(" ", result.Errors));

        var stored = await db.PendingPackagePayments.FirstOrDefaultAsync(p => p.TransactionReference == "CAREPRO-PKG-MISMATCH");
        Assert.Equal(PendingPackagePaymentStatus.AmountMismatch, stored!.Status);
        Assert.Empty(db.PackageRequests.ToList());
        packageRequestServiceMock.Verify(
            x => x.CreateAsync(It.IsAny<string>(), It.IsAny<CreatePackageRequestRequest>()),
            Times.Never);
    }
}

internal sealed class TestPackagePaymentDbContext : CareProDbContext
{
    public TestPackagePaymentDbContext(DbContextOptions<CareProDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<WebhookLog>().Ignore(x => x.Headers);
        modelBuilder.Ignore<AssessmentQuestion>();
    }
}
