using Application.Commands;
using Application.DTOs;
using Application.Interfaces;
using Application.Interfaces.Content;
using Application.Interfaces.Email;
using Domain.Entities;
using Domain.Settings;
using Infrastructure.Content.Data;
using Infrastructure.Content.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MongoDB.EntityFrameworkCore.Extensions;
using MongoDB.Bson;
using Xunit;

namespace CommitmentGate.Tests;

/// <summary>
/// Covers the caregiver-readiness gate introduced for the hire-conversion work:
/// the consolidated readiness service, the hire-time gate (CareRequestResponseService),
/// and the negotiation-agree backstop (GigPriceNegotiationService). Uses the same real
/// local MongoDB instance as CommitmentGateTests (see CreateDb()), not an in-memory fake —
/// these are integration tests, not pure unit tests with everything mocked away.
/// </summary>
public class CaregiverReadinessAndSafeguardsTests
{
    private static CareProDbContext CreateDb()
    {
        var databaseName = $"carepro_readiness_tests_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<CareProDbContext>()
            .UseMongoDB("mongodb://127.0.0.1:27018", databaseName)
            .Options;
        return new TestCareProDbContext(options);
    }

    private static CaregiverReadinessService CreateReadinessService(CareProDbContext db)
    {
        var eligibilityService = new EligibilityService(db, Mock.Of<ILogger<EligibilityService>>());
        return new CaregiverReadinessService(db, eligibilityService, Mock.Of<ILogger<CaregiverReadinessService>>());
    }

    // ─────────────────────────────────────────────────────────────
    //  CaregiverReadinessService
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetReadinessAsync_UnverifiedNoGig_ReturnsBothReasons()
    {
        using var db = CreateDb();
        var caregiverId = ObjectId.GenerateNewId();
        db.CareGivers.Add(new Caregiver
        {
            Id = caregiverId,
            FirstName = "Unready",
            LastName = "Caregiver",
            Email = "unready@example.com",
            Role = "Caregiver",
            Status = true,
            IsAvailable = true,
            IsIdentityVerified = false,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = CreateReadinessService(db);
        var result = await service.GetReadinessAsync(caregiverId.ToString(), "General");

        Assert.False(result.IsReady);
        Assert.Contains(CaregiverReadinessReasons.NotIdentityVerified, result.IneligibilityReasons);
        Assert.Contains(CaregiverReadinessReasons.NoActiveGig, result.IneligibilityReasons);
    }

    [Fact]
    public async Task GetReadinessAsync_VerifiedWithActiveGigGeneralCategory_IsReady()
    {
        using var db = CreateDb();
        var caregiverId = ObjectId.GenerateNewId();
        db.CareGivers.Add(new Caregiver
        {
            Id = caregiverId,
            FirstName = "Ready",
            LastName = "Caregiver",
            Email = "ready@example.com",
            Role = "Caregiver",
            Status = true,
            IsAvailable = true,
            IsIdentityVerified = true,
            CreatedAt = DateTime.UtcNow
        });
        db.Gigs.Add(new Gig
        {
            Id = ObjectId.GenerateNewId(),
            CaregiverId = caregiverId.ToString(),
            Title = "General Care",
            Category = "General",
            Status = "Active",
            Price = 15000,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = CreateReadinessService(db);
        // No ServiceRequirement configured for "General" — general-tier bypass must apply,
        // not silently fail the caregiver on a missing assessment.
        var result = await service.GetReadinessAsync(caregiverId.ToString(), "General");

        Assert.True(result.IsReady);
        Assert.Empty(result.IneligibilityReasons);
        Assert.True(result.IsIdentityVerified);
        Assert.True(result.HasActiveGig);
    }

    [Fact]
    public async Task GetReadinessAsync_KnownHasActiveGigTrue_SkipsInternalGigQuery()
    {
        using var db = CreateDb();
        var caregiverId = ObjectId.GenerateNewId();
        db.CareGivers.Add(new Caregiver
        {
            Id = caregiverId,
            FirstName = "Trusted",
            LastName = "Caller",
            Email = "trusted@example.com",
            Role = "Caregiver",
            Status = true,
            IsAvailable = true,
            IsIdentityVerified = true,
            CreatedAt = DateTime.UtcNow
        });
        // Deliberately NO gig seeded — proves the caller-supplied value is trusted
        // instead of the (empty) real gig data, which is the N+1 fix from the review.
        await db.SaveChangesAsync();

        var service = CreateReadinessService(db);
        var result = await service.GetReadinessAsync(caregiverId.ToString(), "General", knownHasActiveGig: true);

        Assert.True(result.HasActiveGig);
        Assert.DoesNotContain(CaregiverReadinessReasons.NoActiveGig, result.IneligibilityReasons);
    }

    [Fact]
    public async Task GetReadinessBulkAsync_PreloadedCaregivers_MatchesPerCaregiverReadiness()
    {
        using var db = CreateDb();
        var readyId = ObjectId.GenerateNewId();
        var unreadyId = ObjectId.GenerateNewId();

        var ready = new Caregiver { Id = readyId, FirstName = "R", LastName = "R", Email = "r@example.com", Role = "Caregiver", Status = true, IsAvailable = true, IsIdentityVerified = true, CreatedAt = DateTime.UtcNow };
        var unready = new Caregiver { Id = unreadyId, FirstName = "U", LastName = "U", Email = "u@example.com", Role = "Caregiver", Status = true, IsAvailable = true, IsIdentityVerified = false, CreatedAt = DateTime.UtcNow };
        db.CareGivers.AddRange(ready, unready);
        db.Gigs.Add(new Gig { Id = ObjectId.GenerateNewId(), CaregiverId = readyId.ToString(), Title = "G", Category = "General", Status = "Active", Price = 10000, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var service = CreateReadinessService(db);
        var result = await service.GetReadinessBulkAsync(new List<Caregiver> { ready, unready }, "General");

        Assert.True(result[readyId.ToString()].IsReady);
        Assert.False(result[unreadyId.ToString()].IsReady);
        Assert.Contains(CaregiverReadinessReasons.NotIdentityVerified, result[unreadyId.ToString()].IneligibilityReasons);
    }

    // ─────────────────────────────────────────────────────────────
    //  Hire-time gate — CareRequestResponseService.HireResponderAsync
    // ─────────────────────────────────────────────────────────────

    private static (CareRequestResponseService service, ObjectId requestId, ObjectId responseId) SeedHireScenario(
        CareProDbContext db, bool caregiverReady)
    {
        var clientId = ObjectId.GenerateNewId();
        var caregiverId = ObjectId.GenerateNewId();
        var requestId = ObjectId.GenerateNewId();
        var responseId = ObjectId.GenerateNewId();

        db.Clients.Add(new Client { Id = clientId, FirstName = "Client", LastName = "Test", Email = "client@example.com", Role = "Client", Password = "x", IsDeleted = false }); // pragma: allowlist-secret
        db.CareGivers.Add(new Caregiver
        {
            Id = caregiverId,
            FirstName = "Caregiver",
            LastName = "Test",
            Email = "caregiver@example.com",
            Role = "Caregiver",
            Status = true,
            IsAvailable = true,
            IsIdentityVerified = caregiverReady,
            CreatedAt = DateTime.UtcNow
        });
        if (caregiverReady)
        {
            db.Gigs.Add(new Gig { Id = ObjectId.GenerateNewId(), CaregiverId = caregiverId.ToString(), Title = "G", Category = "General", Status = "Active", Price = 10000, CreatedAt = DateTime.UtcNow });
        }
        db.CareRequests.Add(new CareRequest
        {
            Id = requestId,
            ClientId = clientId.ToString(),
            ServiceCategory = "General",
            Title = "Need care",
            Status = "matched",
            CreatedAt = DateTime.UtcNow
        });
        db.CareRequestResponses.Add(new Domain.Entities.CareRequestResponse
        {
            Id = responseId,
            CareRequestId = requestId.ToString(),
            CaregiverId = caregiverId.ToString(),
            Status = "pending",
            RespondedAt = DateTime.UtcNow
        });
        db.SaveChangesAsync().GetAwaiter().GetResult();

        var negotiationService = new Mock<IGigPriceNegotiationService>();
        negotiationService
            .Setup(x => x.InitiateFromCareRequestHireAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<string>>()))
            .ReturnsAsync(new GigPriceNegotiationResponseDTO { NegotiationId = ObjectId.GenerateNewId().ToString() });

        var readinessService = CreateReadinessService(db);

        var service = new CareRequestResponseService(
            db,
            Mock.Of<IMediator>(),
            Mock.Of<IEmailService>(),
            Mock.Of<IGeocodingService>(),
            negotiationService.Object,
            readinessService,
            Mock.Of<ILogger<CareRequestResponseService>>());

        return (service, requestId, responseId);
    }

    [Fact]
    public async Task HireResponderAsync_CaregiverNotReady_ThrowsCaregiverNotReadyException()
    {
        using var db = CreateDb();
        var (service, requestId, responseId) = SeedHireScenario(db, caregiverReady: false);
        var request = await db.CareRequests.FindAsync(requestId);

        var ex = await Assert.ThrowsAsync<CaregiverNotReadyException>(
            () => service.HireResponderAsync(requestId.ToString(), responseId.ToString(), request!.ClientId));

        Assert.NotEmpty(ex.Reasons);
        Assert.Equal("CAREGIVER_NOT_READY", ex.ErrorCode);

        // Confirm the block actually prevented the hire — response must NOT be marked hired.
        var response = await db.CareRequestResponses.FindAsync(responseId);
        Assert.Equal("pending", response!.Status);
    }

    [Fact]
    public async Task HireResponderAsync_CaregiverReady_Succeeds()
    {
        using var db = CreateDb();
        var (service, requestId, responseId) = SeedHireScenario(db, caregiverReady: true);
        var request = await db.CareRequests.FindAsync(requestId);

        var result = await service.HireResponderAsync(requestId.ToString(), responseId.ToString(), request!.ClientId);

        Assert.True(result.Success);
        var response = await db.CareRequestResponses.FindAsync(responseId);
        Assert.Equal("hired", response!.Status);
    }

    // ─────────────────────────────────────────────────────────────
    //  Negotiation-agree backstop — GigPriceNegotiationService.ClientAcceptAsync
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ClientAcceptAsync_CaregiverBecameIneligible_ThrowsCaregiverNotReadyException()
    {
        using var db = CreateDb();
        var clientId = ObjectId.GenerateNewId().ToString();
        var caregiverId = ObjectId.GenerateNewId();
        var negotiationId = ObjectId.GenerateNewId();

        // Caregiver was ready when hired, but is unverified now — the exact scenario
        // the backstop exists for.
        db.CareGivers.Add(new Caregiver
        {
            Id = caregiverId,
            FirstName = "Caregiver",
            LastName = "Test",
            Email = "caregiver@example.com",
            Role = "Caregiver",
            Status = true,
            IsAvailable = true,
            IsIdentityVerified = false,
            CreatedAt = DateTime.UtcNow
        });
        db.GigPriceNegotiations.Add(new GigPriceNegotiation
        {
            Id = negotiationId,
            ClientId = clientId,
            CaregiverId = caregiverId.ToString(),
            EntrySource = "CareRequestHire",
            GigTitleSnapshot = "Care",
            GigCategorySnapshot = "General",
            OriginalGigPrice = 10000,
            LatestProposedPrice = 10000,
            ProposedBy = "Caregiver",
            Status = GigPriceNegotiationStatus.Initiated,
            ExpiresAt = DateTime.UtcNow.AddHours(48),
            Version = 0
        });
        await db.SaveChangesAsync();

        var readinessService = CreateReadinessService(db);
        var service = new GigPriceNegotiationService(
            db,
            Mock.Of<IMediator>(),
            Mock.Of<IEmailService>(),
            readinessService,
            Mock.Of<ILogger<GigPriceNegotiationService>>());

        var ex = await Assert.ThrowsAsync<CaregiverNotReadyException>(
            () => service.ClientAcceptAsync(clientId, negotiationId.ToString(), version: 0));

        Assert.NotEmpty(ex.Reasons);

        // Confirm the block actually prevented agreement — status must NOT be Agreed.
        var negotiation = await db.GigPriceNegotiations.FindAsync(negotiationId);
        Assert.NotEqual(GigPriceNegotiationStatus.Agreed, negotiation!.Status);
    }

    // ─────────────────────────────────────────────────────────────
    //  Payment-completion backstop — PendingPaymentService.CompletePaymentAsync
    //  (non-blocking: money is already captured by Flutterwave by this point, so this
    //  must still create the order but flag it — see report for why blocking here would
    //  strand an already-charged client with nothing.)
    // ─────────────────────────────────────────────────────────────

    private static PendingPaymentService CreatePendingPaymentServiceForReadinessTest(
        CareProDbContext db,
        ICaregiverReadinessService readinessService,
        Mock<IMediator> mediatorMock,
        string caregiverId)
    {
        var gigServices = new Mock<IGigServices>();
        gigServices
            .Setup(x => x.GetGigAsync(It.IsAny<string>()))
            .ReturnsAsync((string gigId) => new GigDTO
            {
                Id = gigId,
                CaregiverId = caregiverId,
                Status = "active",
                Price = 20000,
                IsSpecialGig = false
            });

        var clientOrderService = new Mock<IClientOrderService>();
        clientOrderService
            .Setup(x => x.CreateClientOrderAsync(It.IsAny<AddClientOrderRequest>()))
            .ReturnsAsync(Result<ClientOrderDTO>.Success(new ClientOrderDTO { Id = ObjectId.GenerateNewId().ToString() }));

        var configValues = new Dictionary<string, string?>
        {
            ["Flutterwave:SecretKey"] = "test-secret",
            ["Flutterwave:PublicKey"] = "test-public",
            ["Flutterwave:EncryptionKey"] = "test-encryption",
            ["Flutterwave:WebhookSecretHash"] = "test-webhook-hash",
            ["FrontendUrl"] = "https://example.com"
        };
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(configValues).Build();
        var flutterwave = new FlutterwaveService(config, Mock.Of<ILogger<FlutterwaveService>>());

        return new PendingPaymentService(
            db,
            gigServices.Object,
            clientOrderService.Object,
            flutterwave,
            Mock.Of<ISubscriptionService>(),
            Mock.Of<ILogger<PendingPaymentService>>(),
            config,
            Options.Create(new CommitmentFeeSettings { Enabled = false }),
            Mock.Of<IBillingRecordService>(),
            Mock.Of<IBookingCommitmentService>(),
            Mock.Of<IReferralService>(),
            readinessService,
            mediatorMock.Object);
    }

    private static async Task<(PendingPaymentService service, string txRef)> SeedPaymentScenario(CareProDbContext db, string caregiverId)
    {
        var clientId = ObjectId.GenerateNewId().ToString();
        var gigId = ObjectId.GenerateNewId();
        const string txRef = "CAREPRO-READINESS-TEST-REF";

        db.Gigs.Add(new Gig
        {
            Id = gigId,
            CaregiverId = caregiverId,
            Title = "Test Gig",
            Category = "General",
            Status = "Active",
            Price = 20000
        });
        db.PendingPayments.Add(new PendingPayment
        {
            Id = ObjectId.GenerateNewId(),
            ClientId = clientId,
            GigId = gigId.ToString(),
            TransactionReference = txRef,
            ServiceType = "one-time",
            FrequencyPerWeek = 1,
            BasePrice = 20000,
            OrderFee = 20000,
            ServiceCharge = 2000,
            FlutterwaveFees = 500,
            TotalAmount = 22500,
            Currency = "NGN",
            Email = "client@example.com",
            RedirectUrl = "https://example.com",
            Status = PendingPaymentStatus.Pending
        });
        await db.SaveChangesAsync();

        return (null!, txRef); // service constructed by caller once readiness mock is ready
    }

    [Fact]
    public async Task CompletePaymentAsync_CaregiverBecameIneligible_StillCreatesOrder_ButFlagsAndNotifiesClient()
    {
        using var db = CreateDb();
        var caregiverId = ObjectId.GenerateNewId().ToString();
        var (_, txRef) = await SeedPaymentScenario(db, caregiverId);

        var readinessMock = new Mock<ICaregiverReadinessService>();
        readinessMock
            .Setup(x => x.GetReadinessAsync(caregiverId, It.IsAny<string>(), It.IsAny<bool?>()))
            .ReturnsAsync(new CaregiverReadinessResult
            {
                IsReady = false,
                IneligibilityReasons = new List<string> { CaregiverReadinessReasons.NotIdentityVerified }
            });

        var mediatorMock = new Mock<IMediator>();
        var service = CreatePendingPaymentServiceForReadinessTest(db, readinessMock.Object, mediatorMock, caregiverId);

        var result = await service.CompletePaymentAsync(txRef, "flw-tx-123", 22500m);

        Assert.True(result.IsSuccess);
        // The order must still be created — money was already captured, blocking would
        // strand a paid client with nothing.
        Assert.Equal(PendingPaymentStatus.Completed, result.Value!.Status);
        Assert.NotNull(result.Value.ClientOrderId);

        // But the client must be told something's wrong.
        mediatorMock.Verify(x => x.Send(
            It.Is<SendNotificationCommand>(cmd => cmd.Type == NotificationTypes.CaregiverBecameIneligible),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CompletePaymentAsync_CaregiverStillReady_DoesNotNotify()
    {
        using var db = CreateDb();
        var caregiverId = ObjectId.GenerateNewId().ToString();
        var (_, txRef) = await SeedPaymentScenario(db, caregiverId);

        var readinessMock = new Mock<ICaregiverReadinessService>();
        readinessMock
            .Setup(x => x.GetReadinessAsync(caregiverId, It.IsAny<string>(), It.IsAny<bool?>()))
            .ReturnsAsync(new CaregiverReadinessResult { IsReady = true });

        var mediatorMock = new Mock<IMediator>();
        var service = CreatePendingPaymentServiceForReadinessTest(db, readinessMock.Object, mediatorMock, caregiverId);

        var result = await service.CompletePaymentAsync(txRef, "flw-tx-456", 22500m);

        Assert.True(result.IsSuccess);
        Assert.Equal(PendingPaymentStatus.Completed, result.Value!.Status);
        Assert.NotNull(result.Value.ClientOrderId);

        // The only _mediator.Send call anywhere in PendingPaymentService is the
        // ineligibility notification — a ready caregiver must never trigger it.
        mediatorMock.Verify(x => x.Send(It.IsAny<SendNotificationCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ─────────────────────────────────────────────────────────────
    //  Consent single-source-of-truth — IEmailNotificationTrackingService.HasGeneralMarketingConsent
    // ─────────────────────────────────────────────────────────────

    private static EmailNotificationTrackingService CreateTrackingService(CareProDbContext db)
        => new(db, Mock.Of<ILogger<EmailNotificationTrackingService>>());

    [Fact]
    public void HasGeneralMarketingConsent_NullPreferences_ReturnsFalse()
    {
        using var db = CreateDb();
        var service = CreateTrackingService(db);

        Assert.False(service.HasGeneralMarketingConsent((CaregiverNotificationPreferences?)null));
        Assert.False(service.HasGeneralMarketingConsent((NotificationPreferences?)null));
    }

    [Fact]
    public void HasGeneralMarketingConsent_EmailNotificationsOff_ReturnsFalseEvenIfMarketingTrue()
    {
        using var db = CreateDb();
        var service = CreateTrackingService(db);

        var prefs = new CaregiverNotificationPreferences { EmailNotifications = false, MarketingEmails = true };
        Assert.False(service.HasGeneralMarketingConsent(prefs));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void HasGeneralMarketingConsent_Caregiver_GatesOnMarketingEmailsOnly(bool marketing, bool expected)
    {
        using var db = CreateDb();
        var service = CreateTrackingService(db);

        // Caregiver preferences have a single marketing toggle (the former "promotions"
        // flag was collapsed into MarketingEmails — they had identical effect).
        var caregiverPrefs = new CaregiverNotificationPreferences { EmailNotifications = true, MarketingEmails = marketing };
        Assert.Equal(expected, service.HasGeneralMarketingConsent(caregiverPrefs));
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    public void HasGeneralMarketingConsent_Client_EitherFlagGrantsConsent(bool marketing, bool promotions, bool expected)
    {
        using var db = CreateDb();
        var service = CreateTrackingService(db);

        var clientPrefs = new NotificationPreferences { EmailNotifications = true, MarketingEmails = marketing, Promotions = promotions };

        Assert.Equal(expected, service.HasGeneralMarketingConsent(clientPrefs));
    }
}
