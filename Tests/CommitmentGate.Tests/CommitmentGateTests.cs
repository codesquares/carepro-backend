using System.Text.Json;
using System.Reflection;
using Application.DTOs;
using Application.Interfaces;
using Application.Interfaces.Content;
using Application.Interfaces.Email;
using Application.Commands;
using Domain.Entities;
using Domain.Settings;
using Infrastructure.Content.Data;
using Infrastructure.Content.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MongoDB.EntityFrameworkCore.Extensions;
using MongoDB.Bson;
using Xunit;

namespace CommitmentGate.Tests;

public class CommitmentGateTests
{
    [Fact]
    public async Task Case1_FlagOff_ReturnsFullFee_ZeroDeduction_NoError()
    {
        using var db = CreateDb();

        var clientId = "client-case1";
        var gigId = ObjectId.GenerateNewId().ToString();

        db.Gigs.Add(new Gig
        {
            Id = ObjectId.Parse(gigId),
            CaregiverId = "caregiver-1",
            Title = "Test Gig 1",
            Category = "General",
            Status = "active",
            Price = 20000,
            IsSpecialGig = false
        });

        // Existing commitment should be untouched/irrelevant while gate is OFF.
        db.BookingCommitments.Add(new BookingCommitment
        {
            Id = ObjectId.GenerateNewId(),
            ClientId = clientId,
            GigId = gigId,
            CaregiverId = "caregiver-1",
            Status = BookingCommitmentStatus.Completed,
            Amount = 5000,
            TransactionReference = "CAREPRO-COMMIT-TEST",
            Email = "client@example.com",
            RedirectUrl = "https://example.com",
            CreatedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow,
            IsAppliedToOrder = false
        });

        // Cached pending payment with full fee and zero commitment deduction
        db.PendingPayments.Add(new PendingPayment
        {
            Id = ObjectId.GenerateNewId(),
            ClientId = clientId,
            GigId = gigId,
            Status = PendingPaymentStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            PaymentLink = "https://pay.test/full-fee",
            TransactionReference = "CAREPRO-CASE1-REF",
            ServiceType = "monthly",
            FrequencyPerWeek = 2,
            BasePrice = 20000,
            OrderFee = 160000,
            ServiceCharge = 16000,
            FlutterwaveFees = 2000,
            TotalAmount = 178000,
            CommitmentFeeDeducted = 0,
            Currency = "NGN",
            Email = "client@example.com",
            RedirectUrl = "https://example.com"
        });
        await db.SaveChangesAsync();

        var bookingCommitmentMock = new Mock<IBookingCommitmentService>();
        bookingCommitmentMock
            .Setup(x => x.GetApplicableCommitmentAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("Commitment lookup should not run when gate is OFF"));

        var service = CreatePendingPaymentService(db, commitmentGateEnabled: false, bookingCommitmentMock.Object);

        var result = await service.CreatePendingPaymentAsync(new InitiatePaymentRequest
        {
            GigId = gigId,
            ServiceType = "monthly",
            FrequencyPerWeek = 2,
            Email = "client@example.com",
            RedirectUrl = "https://example.com"
        }, clientId);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(160000, result.Value!.Breakdown.OrderFee);
        Assert.Equal(0, result.Value.Breakdown.CommitmentFeeDeducted);
        Console.WriteLine($"CASE1_EVIDENCE orderFee={result.Value.Breakdown.OrderFee} commitmentDeducted={result.Value.Breakdown.CommitmentFeeDeducted} success={result.IsSuccess}");
    }

    [Fact]
    public async Task Case2_FlagOn_NoCommitment_Blocked()
    {
        using var db = CreateDb();

        var clientId = "client-case2";
        var gigId = ObjectId.GenerateNewId().ToString();

        db.Gigs.Add(new Gig
        {
            Id = ObjectId.Parse(gigId),
            CaregiverId = "caregiver-2",
            Title = "Test Gig 2",
            Category = "General",
            Status = "active",
            Price = 15000,
            IsSpecialGig = false
        });
        await db.SaveChangesAsync();

        var bookingCommitmentMock = new Mock<IBookingCommitmentService>();
        bookingCommitmentMock
            .Setup(x => x.GetApplicableCommitmentAsync(clientId, gigId))
            .ReturnsAsync((BookingCommitment?)null);

        var service = CreatePendingPaymentService(db, commitmentGateEnabled: true, bookingCommitmentMock.Object);

        var result = await service.CreatePendingPaymentAsync(new InitiatePaymentRequest
        {
            GigId = gigId,
            ServiceType = "monthly",
            FrequencyPerWeek = 1,
            Email = "client@example.com",
            RedirectUrl = "https://example.com"
        }, clientId);

        Assert.False(result.IsSuccess);
        Assert.Contains("booking commitment fee", string.Join(" ", result.Errors), StringComparison.OrdinalIgnoreCase);
        Console.WriteLine($"CASE2_EVIDENCE success={result.IsSuccess} errors={string.Join(" | ", result.Errors)}");
    }

    [Fact]
    public async Task Case3_FlagOn_WithCommitment_DeductedAsToday()
    {
        using var db = CreateDb();

        var clientId = "client-case3";
        var gigId = ObjectId.GenerateNewId().ToString();

        db.Gigs.Add(new Gig
        {
            Id = ObjectId.Parse(gigId),
            CaregiverId = "caregiver-3",
            Title = "Test Gig 3",
            Category = "General",
            Status = "active",
            Price = 18000,
            IsSpecialGig = false
        });

        db.PendingPayments.Add(new PendingPayment
        {
            Id = ObjectId.GenerateNewId(),
            ClientId = clientId,
            GigId = gigId,
            Status = PendingPaymentStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            PaymentLink = "https://pay.test/deducted",
            TransactionReference = "CAREPRO-CASE3-REF",
            ServiceType = "monthly",
            FrequencyPerWeek = 1,
            BasePrice = 18000,
            OrderFee = 67000,
            ServiceCharge = 6700,
            FlutterwaveFees = 1031.8m,
            TotalAmount = 74731.8m,
            BookingCommitmentId = ObjectId.GenerateNewId().ToString(),
            CommitmentFeeDeducted = 5000,
            Currency = "NGN",
            Email = "client@example.com",
            RedirectUrl = "https://example.com"
        });
        await db.SaveChangesAsync();

        var bookingCommitment = new BookingCommitment
        {
            Id = ObjectId.GenerateNewId(),
            ClientId = clientId,
            GigId = gigId,
            CaregiverId = "caregiver-3",
            Status = BookingCommitmentStatus.Completed,
            Amount = 5000,
            TransactionReference = "CAREPRO-COMMIT-CASE3",
            Email = "client@example.com",
            RedirectUrl = "https://example.com",
            CreatedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow,
            IsAppliedToOrder = false
        };

        var bookingCommitmentMock = new Mock<IBookingCommitmentService>();
        bookingCommitmentMock
            .Setup(x => x.GetApplicableCommitmentAsync(clientId, gigId))
            .ReturnsAsync(bookingCommitment);

        var service = CreatePendingPaymentService(db, commitmentGateEnabled: true, bookingCommitmentMock.Object);

        var result = await service.CreatePendingPaymentAsync(new InitiatePaymentRequest
        {
            GigId = gigId,
            ServiceType = "monthly",
            FrequencyPerWeek = 1,
            Email = "client@example.com",
            RedirectUrl = "https://example.com"
        }, clientId);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(5000, result.Value!.Breakdown.CommitmentFeeDeducted);
        Console.WriteLine($"CASE3_EVIDENCE orderFee={result.Value.Breakdown.OrderFee} commitmentDeducted={result.Value.Breakdown.CommitmentFeeDeducted} success={result.IsSuccess}");
    }

    [Fact]
    public async Task Case4_CheckEndpoint_FlagOff_ReturnsNotRequired()
    {
        using var db = CreateDb();

        var gigId = ObjectId.GenerateNewId().ToString();
        db.Gigs.Add(new Gig
        {
            Id = ObjectId.Parse(gigId),
            CaregiverId = "caregiver-4",
            Title = "Test Gig 4",
            Category = "General",
            Status = "active",
            Price = 12000,
            IsSpecialGig = false
        });
        await db.SaveChangesAsync();

        var service = CreateBookingCommitmentService(db, commitmentGateEnabled: false);
        var status = await service.GetCommitmentStatusAsync("client-case4", gigId);

        Assert.True(status.HasAccess);
        Assert.True(status.CommitmentNotRequired);
        Console.WriteLine($"CASE4_EVIDENCE hasAccess={status.HasAccess} commitmentNotRequired={status.CommitmentNotRequired}");
    }

    [Fact]
    public async Task Case5_CheckEndpoint_FlagOn_UnchangedBehavior()
    {
        using var db = CreateDb();

        var clientId = "client-case5";
        var gigId = ObjectId.GenerateNewId().ToString();

        db.Gigs.Add(new Gig
        {
            Id = ObjectId.Parse(gigId),
            CaregiverId = "caregiver-5",
            Title = "Test Gig 5",
            Category = "General",
            Status = "active",
            Price = 12000,
            IsSpecialGig = false
        });
        await db.SaveChangesAsync();

        var service = CreateBookingCommitmentService(db, commitmentGateEnabled: true);
        var status = await service.GetCommitmentStatusAsync(clientId, gigId);

        Assert.False(status.HasAccess);
        Assert.False(status.CommitmentNotRequired);
        Console.WriteLine($"CASE5_EVIDENCE hasAccess={status.HasAccess} commitmentNotRequired={status.CommitmentNotRequired}");
    }

    [Fact]
    public async Task RecurringWebhook_OrderCreationFailure_DoesNotMarkCycleSuccessful()
    {
        using var db = CreateDb();

        var subscriptionId = ObjectId.GenerateNewId().ToString();
        var txRef = "CAREPRO-RECURRING-TEST-ORDER-FAIL";

        db.Subscriptions.Add(new Subscription
        {
            Id = subscriptionId,
            ClientId = "client-r1",
            CaregiverId = "caregiver-r1",
            GigId = ObjectId.GenerateNewId().ToString(),
            OriginalOrderId = ObjectId.GenerateNewId().ToString(),
            Email = "client-r1@example.com",
            BillingCycle = "monthly",
            FrequencyPerWeek = 2,
            RecurringAmount = 120000m,
            PricePerVisit = 15000m,
            PriceBreakdown = new SubscriptionPriceBreakdown
            {
                BasePrice = 15000m,
                FrequencyPerWeek = 2,
                OrderFee = 100000m,
                ServiceCharge = 10000m,
                GatewayFees = 10000m,
                TotalAmount = 120000m
            },
            Currency = "NGN",
            Status = SubscriptionStatus.Active,
            AutoRenew = true,
            BillingCyclesCompleted = 1,
            CurrentPeriodStart = DateTime.UtcNow.AddDays(-30),
            CurrentPeriodEnd = DateTime.UtcNow,
            NextChargeDate = DateTime.UtcNow,
            PaymentHistory = new List<SubscriptionPaymentRecord>
            {
                new()
                {
                    Id = ObjectId.GenerateNewId().ToString(),
                    TransactionReference = txRef,
                    Amount = 120000m,
                    Currency = "NGN",
                    Status = "pending",
                    BillingCycleNumber = 2,
                    AttemptedAt = DateTime.UtcNow,
                    InitiatedBy = "system"
                }
            }
        });
        await db.SaveChangesAsync();

        var clientOrderService = new Mock<IClientOrderService>();
        clientOrderService
            .Setup(x => x.CreateClientOrderAsync(It.IsAny<AddClientOrderRequest>()))
            .ReturnsAsync(Result<ClientOrderDTO>.Failure(new List<string> { "forced-order-failure" }));

        var configValues = new Dictionary<string, string?>
        {
            ["Flutterwave:SecretKey"] = "test-secret",
            ["Flutterwave:PublicKey"] = "test-public",
            ["Flutterwave:EncryptionKey"] = "test-encryption",
            ["Flutterwave:WebhookSecretHash"] = "test-webhook-hash",
            ["FrontendUrl"] = "https://example.com"
        };

        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        var flutterwave = new FlutterwaveService(
            config,
            Mock.Of<ILogger<FlutterwaveService>>());

        var service = new SubscriptionService(
            db,
            clientOrderService.Object,
            flutterwave,
            Mock.Of<MediatR.IMediator>(),
            Mock.Of<ILogger<SubscriptionService>>(),
            config,
            Mock.Of<IBillingRecordService>());

        var result = await service.CompleteRecurringChargeFromWebhookAsync(
            txRef,
            "flw-tx-test-123",
            120000m);

        Assert.False(result.IsSuccess);

        var updated = await db.Subscriptions.FirstOrDefaultAsync(s => s.Id == subscriptionId);
        Assert.NotNull(updated);
        Assert.Equal(SubscriptionStatus.Suspended, updated!.Status);

        var updatedRecord = updated.PaymentHistory.First(p => p.TransactionReference == txRef);
        Assert.Equal("failed", updatedRecord.Status);
        Assert.Equal("non_retryable", updatedRecord.FailureClass);
        Assert.Null(updatedRecord.ClientOrderId);
        Assert.Contains("order creation failed", updatedRecord.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        Console.WriteLine($"RECURRING_ORDER_GUARD_EVIDENCE success={result.IsSuccess} subStatus={updated.Status} paymentStatus={updatedRecord.Status} error={updatedRecord.ErrorMessage}");
    }

    [Fact]
    public void TokenizedResolver_PrefersMetaAuthorizationRedirect_OverRedirectUrl()
    {
        var json = JsonSerializer.Deserialize<JsonElement>(@"{
          ""redirect_url"": ""https://oncarepro.com/subscription/payment-confirmed"",
          ""meta"": {
            ""authorization"": {
              ""mode"": ""redirect"",
              ""redirect"": ""https://ravesandboxapi.flutterwave.com/mockvbvpage?ref=abc123""
            }
          }
        }");

        var method = typeof(FlutterwaveService).GetMethod(
            "ResolveTokenizedAuthUrl",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var resolved = method!.Invoke(null, new object[] { json }) as string;

        Assert.Equal(
            "https://ravesandboxapi.flutterwave.com/mockvbvpage?ref=abc123",
            resolved);
    }

    [Fact]
    public async Task RecurringTimeoutFailure_IsActionRequired_StopsAutoRetry()
    {
        using var db = CreateDb();

        var subscriptionId = ObjectId.GenerateNewId().ToString();
        var txRef = "CAREPRO-RECURRING-TEST-AUTH-TIMEOUT";
        var authUrl = "https://checkout.flutterwave.com/v3/hosted/pay/test-auth-link";

        db.Subscriptions.Add(new Subscription
        {
            Id = subscriptionId,
            ClientId = "client-action-required",
            CaregiverId = "caregiver-action-required",
            GigId = ObjectId.GenerateNewId().ToString(),
            OriginalOrderId = ObjectId.GenerateNewId().ToString(),
            Email = "client-action-required@example.com",
            BillingCycle = "monthly",
            FrequencyPerWeek = 2,
            RecurringAmount = 83655m,
            PricePerVisit = 10000m,
            PriceBreakdown = new SubscriptionPriceBreakdown
            {
                BasePrice = 10000m,
                FrequencyPerWeek = 2,
                OrderFee = 70000m,
                ServiceCharge = 7000m,
                GatewayFees = 6655m,
                TotalAmount = 83655m
            },
            Currency = "NGN",
            Status = SubscriptionStatus.Active,
            AutoRenew = true,
            BillingCyclesCompleted = 1,
            CurrentPeriodStart = DateTime.UtcNow.AddDays(-30),
            CurrentPeriodEnd = DateTime.UtcNow,
            NextChargeDate = DateTime.UtcNow,
            PaymentHistory = new List<SubscriptionPaymentRecord>
            {
                new()
                {
                    Id = ObjectId.GenerateNewId().ToString(),
                    TransactionReference = txRef,
                    Amount = 83655m,
                    Currency = "NGN",
                    Status = "pending",
                    BillingCycleNumber = 2,
                    AttemptedAt = DateTime.UtcNow,
                    InitiatedBy = "system",
                    AuthorizationUrl = authUrl
                }
            }
        });
        await db.SaveChangesAsync();

        var configValues = new Dictionary<string, string?>
        {
            ["Flutterwave:SecretKey"] = "test-secret",
            ["Flutterwave:PublicKey"] = "test-public",
            ["Flutterwave:EncryptionKey"] = "test-encryption",
            ["Flutterwave:WebhookSecretHash"] = "test-webhook-hash",
            ["FrontendUrl"] = "https://example.com"
        };

        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        var flutterwave = new FlutterwaveService(
            config,
            Mock.Of<ILogger<FlutterwaveService>>());

        var mediator = new Mock<MediatR.IMediator>();
        mediator
            .Setup(m => m.Send(It.IsAny<SendNotificationCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ObjectId.GenerateNewId().ToString());

        var service = new SubscriptionService(
            db,
            Mock.Of<IClientOrderService>(),
            flutterwave,
            mediator.Object,
            Mock.Of<ILogger<SubscriptionService>>(),
            config,
            Mock.Of<IBillingRecordService>());

        var result = await service.HandleFailedRecurringChargeFromWebhookAsync(
            txRef,
            "Cardholder browser session timed out");

        Assert.True(result.IsSuccess);

        var updated = await db.Subscriptions.FirstOrDefaultAsync(s => s.Id == subscriptionId);
        Assert.NotNull(updated);
        Assert.Equal(SubscriptionStatus.PastDue, updated!.Status);
        Assert.Null(updated.NextChargeDate);
        Assert.Equal("action_required", updated.LastChargeFailureClass);

        var updatedRecord = updated.PaymentHistory.First(p => p.TransactionReference == txRef);
        Assert.Equal("failed", updatedRecord.Status);
        Assert.Equal("action_required", updatedRecord.FailureClass);
        Assert.Equal(authUrl, updatedRecord.AuthorizationUrl);

        mediator.Verify(m => m.Send(
                It.Is<SendNotificationCommand>(cmd =>
                    cmd.Type == NotificationTypes.PaymentActionRequired),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    private static CareProDbContext CreateDb()
    {
        var databaseName = $"carepro_commitment_gate_tests_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<CareProDbContext>()
            .UseMongoDB("mongodb://127.0.0.1:27018", databaseName)
            .Options;
        return new TestCareProDbContext(options);
    }

    private static PendingPaymentService CreatePendingPaymentService(
        CareProDbContext db,
        bool commitmentGateEnabled,
        IBookingCommitmentService bookingCommitmentService)
    {
        var gigServices = new Mock<IGigServices>();
        gigServices
            .Setup(x => x.GetGigAsync(It.IsAny<string>()))
            .ReturnsAsync((string gigId) =>
            {
                return new GigDTO
                {
                    Id = gigId,
                    CaregiverId = "caregiver-mock",
                    Status = "active",
                    Price = 20000,
                    IsSpecialGig = false,
                    ScopedClientId = null,
                    CareRequestId = null
                };
            });

        var clientOrderService = new Mock<IClientOrderService>();
        var subscriptionService = new Mock<ISubscriptionService>();
        var billingService = new Mock<IBillingRecordService>();
        var referralService = new Mock<IReferralService>();

        referralService
            .Setup(x => x.ValidateReferralForCheckoutAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>()))
            .ReturnsAsync(Result<ReferralCheckoutApplication>.Failure(new List<string> { "Referral not used in this test." }));

        var configValues = new Dictionary<string, string?>
        {
            ["Flutterwave:SecretKey"] = "test-secret",
            ["Flutterwave:PublicKey"] = "test-public",
            ["Flutterwave:EncryptionKey"] = "test-encryption",
            ["Flutterwave:WebhookSecretHash"] = "test-webhook-hash",
            ["FrontendUrl"] = "https://example.com"
        };

        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        var flutterwave = new FlutterwaveService(
            config,
            Mock.Of<ILogger<FlutterwaveService>>());

        return new PendingPaymentService(
            db,
            gigServices.Object,
            clientOrderService.Object,
            flutterwave,
            subscriptionService.Object,
            Mock.Of<ILogger<PendingPaymentService>>(),
            config,
            Options.Create(new CommitmentFeeSettings { Enabled = commitmentGateEnabled }),
            billingService.Object,
            bookingCommitmentService,
            referralService.Object);
    }

    private static BookingCommitmentService CreateBookingCommitmentService(CareProDbContext db, bool commitmentGateEnabled)
    {
        var gigServices = new Mock<IGigServices>();
        var flutterwaveConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Flutterwave:SecretKey"] = "test-secret",
                ["Flutterwave:PublicKey"] = "test-public",
                ["Flutterwave:EncryptionKey"] = "test-encryption",
                ["Flutterwave:WebhookSecretHash"] = "test-webhook-hash",
                ["FrontendUrl"] = "https://example.com"
            })
            .Build();

        var flutterwave = new FlutterwaveService(
            flutterwaveConfig,
            Mock.Of<ILogger<FlutterwaveService>>());

        return new BookingCommitmentService(
            db,
            gigServices.Object,
            flutterwave,
            Mock.Of<MediatR.IMediator>(),
            Mock.Of<IEmailService>(),
            Mock.Of<IReceiptPdfService>(),
            Mock.Of<ILogger<BookingCommitmentService>>(),
            Options.Create(new CommitmentFeeSettings { Enabled = commitmentGateEnabled }));
    }
}

internal sealed class TestCareProDbContext : CareProDbContext
{
    public TestCareProDbContext(DbContextOptions<CareProDbContext> options)
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
