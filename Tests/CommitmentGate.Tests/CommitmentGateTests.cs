using System.Text.Json;
using System.Reflection;
using Application.DTOs;
using Application.Interfaces;
using Application.Interfaces.Authentication;
using Application.Interfaces.Common;
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
    public async Task Renewal_ExistingConflictingActiveOrders_BlocksNewOrderCreationAndSuspendsSubscription()
    {
        using var db = CreateDb();

        var subscriptionId = ObjectId.GenerateNewId().ToString();
        var txRef = "CAREPRO-RECURRING-TEST-CONFLICT-GUARD";

        db.Subscriptions.Add(new Subscription
        {
            Id = subscriptionId,
            ClientId = "client-conflict-guard",
            CaregiverId = "caregiver-conflict-guard",
            GigId = ObjectId.GenerateNewId().ToString(),
            OriginalOrderId = ObjectId.GenerateNewId().ToString(),
            Email = "client-conflict-guard@example.com",
            BillingCycle = "monthly",
            FrequencyPerWeek = 1,
            RecurringAmount = 120000m,
            PricePerVisit = 15000m,
            PriceBreakdown = new SubscriptionPriceBreakdown
            {
                BasePrice = 15000m,
                FrequencyPerWeek = 1,
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

        // Simulates a pre-existing broken state (2+ non-terminal orders already exist for
        // this subscription) — the guard must refuse to add a third order on top of it.
        var clientOrderServiceMock = new Mock<IClientOrderService>();
        clientOrderServiceMock
            .Setup(x => x.HasConflictingActiveOrdersAsync(subscriptionId))
            .ReturnsAsync(true);

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

        var service = new SubscriptionService(
            db,
            clientOrderServiceMock.Object,
            flutterwave,
            Mock.Of<MediatR.IMediator>(),
            Mock.Of<ILogger<SubscriptionService>>(),
            config,
            Mock.Of<IBillingRecordService>());

        var result = await service.CompleteRecurringChargeFromWebhookAsync(txRef, "flw-tx-conflict-guard", 120000m);

        Assert.False(result.IsSuccess);

        // The decisive assertion: order creation must never even be attempted once the
        // guard reports a conflict — this is prevention, not just logging after the fact.
        clientOrderServiceMock.Verify(x => x.CreateClientOrderAsync(It.IsAny<AddClientOrderRequest>()), Times.Never);

        var updated = await db.Subscriptions.FirstOrDefaultAsync(s => s.Id == subscriptionId);
        Assert.NotNull(updated);
        Assert.Equal(SubscriptionStatus.Suspended, updated!.Status);

        var updatedRecord = updated.PaymentHistory.First(p => p.TransactionReference == txRef);
        Assert.Equal("failed", updatedRecord.Status);
        Assert.Contains("conflicting active orders", updatedRecord.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        Console.WriteLine($"RENEWAL_CONFLICT_GUARD_EVIDENCE success={result.IsSuccess} createOrderCalled=false subStatus={updated.Status} error={updatedRecord.ErrorMessage}");
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

    [Fact]
    public async Task Renewal_SecondCycle_SupersedesPreviousOrderAndReleasesSubmittedVisit()
    {
        using var db = CreateDb();

        var subscriptionId = ObjectId.GenerateNewId().ToString();
        var clientId = "client-supersede-1";
        var caregiverId = "caregiver-supersede-1";
        var gigId = ObjectId.GenerateNewId().ToString();
        var txRef = "CAREPRO-RECURRING-TEST-SUPERSEDE";

        // Cycle 1 order — simulates an order created after order-subscription linking
        // shipped, so SubscriptionId is already populated.
        var cycle1OrderId = ObjectId.GenerateNewId();
        db.ClientOrders.Add(new ClientOrder
        {
            Id = cycle1OrderId,
            ClientId = clientId,
            GigId = gigId,
            CaregiverId = caregiverId,
            PaymentOption = "monthly",
            Amount = 120000,
            OrderFee = 100000m,
            CaregiverSharePercentageAtCreation = 0.6m,
            TransactionId = "flw-tx-cycle1",
            ClientOrderStatus = "In Progress",
            IsOrderStatusApproved = true,
            HasDispute = false,
            OrderCreatedAt = DateTime.UtcNow.AddDays(-30),
            FrequencyPerWeek = 1,
            ServiceType = "monthly",
            SubscriptionId = subscriptionId,
            BillingCycleNumber = 1
        });

        var submittedTaskSheetId = ObjectId.GenerateNewId();
        db.TaskSheets.Add(new TaskSheet
        {
            Id = submittedTaskSheetId,
            OrderId = cycle1OrderId.ToString(),
            CaregiverId = caregiverId,
            SheetNumber = 3,
            BillingCycleNumber = 1,
            Status = "submitted",
            ClientReviewStatus = "Pending"
        });

        var pendingTaskSheetId = ObjectId.GenerateNewId();
        db.TaskSheets.Add(new TaskSheet
        {
            Id = pendingTaskSheetId,
            OrderId = cycle1OrderId.ToString(),
            CaregiverId = caregiverId,
            SheetNumber = 4,
            BillingCycleNumber = 1,
            Status = "in-progress"
        });

        db.Subscriptions.Add(new Subscription
        {
            Id = subscriptionId,
            ClientId = clientId,
            CaregiverId = caregiverId,
            GigId = gigId,
            OriginalOrderId = cycle1OrderId.ToString(),
            Email = "client-supersede-1@example.com",
            BillingCycle = "monthly",
            FrequencyPerWeek = 1,
            RecurringAmount = 120000m,
            PricePerVisit = 15000m,
            PriceBreakdown = new SubscriptionPriceBreakdown
            {
                BasePrice = 15000m,
                FrequencyPerWeek = 1,
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

        var gigServicesMock = new Mock<IGigServices>();
        gigServicesMock
            .Setup(x => x.GetGigAsync(gigId))
            .ReturnsAsync(new GigDTO
            {
                Id = gigId,
                CaregiverId = caregiverId,
                Title = "Weekly Companionship",
                Category = "General",
                Status = "active",
                Price = 15000
            });

        var clientServiceMock = new Mock<IClientService>();
        clientServiceMock
            .Setup(x => x.GetClientUserAsync(clientId))
            .ReturnsAsync(new ClientResponse
            {
                Id = clientId,
                FirstName = "Test",
                LastName = "Client",
                Email = "client-supersede-1@example.com"
            });

        var caregiverServiceMock = new Mock<ICareGiverService>();
        caregiverServiceMock
            .Setup(x => x.GetCaregiverUserAsync(caregiverId))
            .ReturnsAsync(new CaregiverResponse
            {
                Id = caregiverId,
                FirstName = "Test",
                LastName = "Caregiver",
                Email = "caregiver-supersede-1@example.com"
            });

        var walletServiceMock = new Mock<ICaregiverWalletService>();
        var ledgerServiceMock = new Mock<IEarningsLedgerService>();

        var clientOrderService = new ClientOrderService(
            db,
            gigServicesMock.Object,
            caregiverServiceMock.Object,
            clientServiceMock.Object,
            Mock.Of<ILogger<GigServices>>(),
            Mock.Of<MediatR.IMediator>(),
            Mock.Of<IOrderTasksService>(),
            Mock.Of<IContractService>(),
            walletServiceMock.Object,
            ledgerServiceMock.Object,
            Mock.Of<IBookingCommitmentService>(),
            Mock.Of<IEmailService>(),
            Mock.Of<IClientWalletService>(),
            Mock.Of<IAnalyticsService>(),
            Options.Create(new CaregiverEarningsSettings { CaregiverSharePercentage = 0.6m }));

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

        var subscriptionService = new SubscriptionService(
            db,
            clientOrderService,
            flutterwave,
            Mock.Of<MediatR.IMediator>(),
            Mock.Of<ILogger<SubscriptionService>>(),
            config,
            Mock.Of<IBillingRecordService>());

        var result = await subscriptionService.CompleteRecurringChargeFromWebhookAsync(
            txRef, "flw-tx-cycle2", 120000m);

        Assert.True(result.IsSuccess);

        var previousOrder = await db.ClientOrders.FindAsync(cycle1OrderId);
        Assert.NotNull(previousOrder);
        Assert.Equal("Superseded", previousOrder!.ClientOrderStatus);

        var newOrder = await db.ClientOrders
            .Where(o => o.SubscriptionId == subscriptionId && o.BillingCycleNumber == 2)
            .FirstOrDefaultAsync();
        Assert.NotNull(newOrder);
        Assert.Equal("In Progress", newOrder!.ClientOrderStatus);
        Assert.Equal(subscriptionId, newOrder.SubscriptionId);

        var refreshedSubmittedSheet = await db.TaskSheets.FindAsync(submittedTaskSheetId);
        Assert.Equal("submitted", refreshedSubmittedSheet!.Status);
        ledgerServiceMock.Verify(x => x.RecordVisitApprovedAsync(
            caregiverId, It.IsAny<decimal>(), cycle1OrderId.ToString(), submittedTaskSheetId.ToString(),
            subscriptionId, 1, "monthly", It.IsAny<string>()), Times.Once);
        walletServiceMock.Verify(x => x.CreditVisitApprovedAsync(caregiverId, It.IsAny<decimal>()), Times.Once);

        var refreshedPendingSheet = await db.TaskSheets.FindAsync(pendingTaskSheetId);
        Assert.Equal("cancelled", refreshedPendingSheet!.Status);

        Console.WriteLine($"RENEWAL_SUPERSEDE_EVIDENCE success={result.IsSuccess} previousOrderStatus={previousOrder.ClientOrderStatus} newOrderStatus={newOrder.ClientOrderStatus} newOrderSubscriptionId={newOrder.SubscriptionId} submittedSheetStatus={refreshedSubmittedSheet.Status} pendingSheetStatus={refreshedPendingSheet.Status}");
    }

    [Fact]
    public async Task Renewal_PreviousOrderMissingSubscriptionLink_SupersedeIsNoOpButRenewalStillSucceeds()
    {
        using var db = CreateDb();

        var subscriptionId = ObjectId.GenerateNewId().ToString();
        var clientId = "client-no-backfill";
        var caregiverId = "caregiver-no-backfill";
        var gigId = ObjectId.GenerateNewId().ToString();
        var txRef = "CAREPRO-RECURRING-TEST-NO-BACKFILL";

        // Simulates data that existed before order-subscription linking shipped: the
        // previous cycle's order was created without SubscriptionId populated.
        var priorOrderId = ObjectId.GenerateNewId();
        db.ClientOrders.Add(new ClientOrder
        {
            Id = priorOrderId,
            ClientId = clientId,
            GigId = gigId,
            CaregiverId = caregiverId,
            PaymentOption = "monthly",
            Amount = 120000,
            OrderFee = 100000m,
            TransactionId = "flw-tx-legacy-cycle1",
            ClientOrderStatus = "In Progress",
            OrderCreatedAt = DateTime.UtcNow.AddDays(-30),
            FrequencyPerWeek = 1,
            ServiceType = "monthly",
            SubscriptionId = null,
            BillingCycleNumber = 1
        });

        db.Subscriptions.Add(new Subscription
        {
            Id = subscriptionId,
            ClientId = clientId,
            CaregiverId = caregiverId,
            GigId = gigId,
            OriginalOrderId = priorOrderId.ToString(),
            Email = "client-no-backfill@example.com",
            BillingCycle = "monthly",
            FrequencyPerWeek = 1,
            RecurringAmount = 120000m,
            PricePerVisit = 15000m,
            PriceBreakdown = new SubscriptionPriceBreakdown
            {
                BasePrice = 15000m,
                FrequencyPerWeek = 1,
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

        var gigServicesMock = new Mock<IGigServices>();
        gigServicesMock.Setup(x => x.GetGigAsync(gigId)).ReturnsAsync(new GigDTO
        {
            Id = gigId,
            CaregiverId = caregiverId,
            Title = "Weekly Companionship",
            Category = "General",
            Status = "active",
            Price = 15000
        });

        var clientServiceMock = new Mock<IClientService>();
        clientServiceMock.Setup(x => x.GetClientUserAsync(clientId)).ReturnsAsync(new ClientResponse
        {
            Id = clientId,
            FirstName = "Test",
            LastName = "Client",
            Email = "client-no-backfill@example.com"
        });

        var caregiverServiceMock = new Mock<ICareGiverService>();
        caregiverServiceMock.Setup(x => x.GetCaregiverUserAsync(caregiverId)).ReturnsAsync(new CaregiverResponse
        {
            Id = caregiverId,
            FirstName = "Test",
            LastName = "Caregiver",
            Email = "caregiver-no-backfill@example.com"
        });

        var clientOrderService = new ClientOrderService(
            db,
            gigServicesMock.Object,
            caregiverServiceMock.Object,
            clientServiceMock.Object,
            Mock.Of<ILogger<GigServices>>(),
            Mock.Of<MediatR.IMediator>(),
            Mock.Of<IOrderTasksService>(),
            Mock.Of<IContractService>(),
            Mock.Of<ICaregiverWalletService>(),
            Mock.Of<IEarningsLedgerService>(),
            Mock.Of<IBookingCommitmentService>(),
            Mock.Of<IEmailService>(),
            Mock.Of<IClientWalletService>(),
            Mock.Of<IAnalyticsService>(),
            Options.Create(new CaregiverEarningsSettings { CaregiverSharePercentage = 0.6m }));

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

        var subscriptionService = new SubscriptionService(
            db,
            clientOrderService,
            flutterwave,
            Mock.Of<MediatR.IMediator>(),
            Mock.Of<ILogger<SubscriptionService>>(),
            config,
            Mock.Of<IBillingRecordService>());

        var result = await subscriptionService.CompleteRecurringChargeFromWebhookAsync(
            txRef, "flw-tx-cycle2-legacy", 120000m);

        Assert.True(result.IsSuccess);

        // The legacy, unlinked order is untouched — supersession had nothing to find,
        // and that must not fail the renewal (matches the confirmed no-backfill decision).
        var untouchedPriorOrder = await db.ClientOrders.FindAsync(priorOrderId);
        Assert.Equal("In Progress", untouchedPriorOrder!.ClientOrderStatus);

        var newOrder = await db.ClientOrders
            .Where(o => o.SubscriptionId == subscriptionId && o.BillingCycleNumber == 2)
            .FirstOrDefaultAsync();
        Assert.NotNull(newOrder);
        Assert.Equal("In Progress", newOrder!.ClientOrderStatus);

        Console.WriteLine($"RENEWAL_NO_BACKFILL_EVIDENCE success={result.IsSuccess} priorOrderStatus={untouchedPriorOrder.ClientOrderStatus} newOrderCreated={newOrder != null}");
    }

    [Fact]
    public async Task SupersedeOrderForRenewal_UnexpectedDuplicateActiveOrder_LogsCriticalSafetyNet()
    {
        using var db = CreateDb();

        var subscriptionId = ObjectId.GenerateNewId().ToString();
        var caregiverId = "caregiver-duplicate-guard";
        var gigId = ObjectId.GenerateNewId().ToString();
        var clientId = "client-duplicate-guard";

        // Two non-terminal orders for the same subscription+cycle — a data-integrity
        // anomaly this fix should never itself produce, but the safety-net guard must
        // detect if it ever occurs (e.g. from some other bug or race condition).
        var orderAId = ObjectId.GenerateNewId();
        var orderBId = ObjectId.GenerateNewId();

        db.ClientOrders.Add(new ClientOrder
        {
            Id = orderAId,
            ClientId = clientId,
            GigId = gigId,
            CaregiverId = caregiverId,
            PaymentOption = "monthly",
            Amount = 50000,
            OrderFee = 40000m,
            CaregiverSharePercentageAtCreation = 0.6m,
            ClientOrderStatus = "In Progress",
            OrderCreatedAt = DateTime.UtcNow.AddDays(-30),
            FrequencyPerWeek = 1,
            SubscriptionId = subscriptionId,
            BillingCycleNumber = 1
        });
        db.ClientOrders.Add(new ClientOrder
        {
            Id = orderBId,
            ClientId = clientId,
            GigId = gigId,
            CaregiverId = caregiverId,
            PaymentOption = "monthly",
            Amount = 50000,
            OrderFee = 40000m,
            CaregiverSharePercentageAtCreation = 0.6m,
            ClientOrderStatus = "In Progress",
            OrderCreatedAt = DateTime.UtcNow.AddDays(-30),
            FrequencyPerWeek = 1,
            SubscriptionId = subscriptionId,
            BillingCycleNumber = 1
        });
        await db.SaveChangesAsync();

        var loggerMock = new Mock<ILogger<GigServices>>();

        var clientOrderService = new ClientOrderService(
            db,
            Mock.Of<IGigServices>(),
            Mock.Of<ICareGiverService>(),
            Mock.Of<IClientService>(),
            loggerMock.Object,
            Mock.Of<MediatR.IMediator>(),
            Mock.Of<IOrderTasksService>(),
            Mock.Of<IContractService>(),
            Mock.Of<ICaregiverWalletService>(),
            Mock.Of<IEarningsLedgerService>(),
            Mock.Of<IBookingCommitmentService>(),
            Mock.Of<IEmailService>(),
            Mock.Of<IClientWalletService>(),
            Mock.Of<IAnalyticsService>(),
            Options.Create(new CaregiverEarningsSettings { CaregiverSharePercentage = 0.6m }));

        await clientOrderService.SupersedeOrderForRenewalAsync(subscriptionId, 2);

        var supersededCount = await db.ClientOrders
            .Where(o => o.SubscriptionId == subscriptionId && o.ClientOrderStatus == "Superseded")
            .CountAsync();
        var stillActiveCount = await db.ClientOrders
            .Where(o => o.SubscriptionId == subscriptionId && o.ClientOrderStatus == "In Progress")
            .CountAsync();

        Assert.Equal(1, supersededCount);
        Assert.Equal(1, stillActiveCount);

        loggerMock.Verify(
            x => x.Log(
                LogLevel.Critical,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
            Times.Once);

        Console.WriteLine($"RENEWAL_DUPLICATE_GUARD_EVIDENCE supersededCount={supersededCount} stillActiveCount={stillActiveCount}");
    }

    [Fact]
    public async Task SupersededOrder_DoesNotBlockGigDeletion()
    {
        using var db = CreateDb();

        var gigId = ObjectId.GenerateNewId().ToString();
        var caregiverId = "caregiver-deletion-check";

        db.Gigs.Add(new Gig
        {
            Id = ObjectId.Parse(gigId),
            CaregiverId = caregiverId,
            Title = "Test Gig Deletion",
            Category = "General",
            Status = "active",
            Price = 15000,
            IsSpecialGig = false
        });

        db.ClientOrders.Add(new ClientOrder
        {
            Id = ObjectId.GenerateNewId(),
            ClientId = "client-deletion-check",
            GigId = gigId,
            CaregiverId = caregiverId,
            PaymentOption = "monthly",
            Amount = 50000,
            OrderFee = 40000m,
            ClientOrderStatus = "Superseded",
            OrderCreatedAt = DateTime.UtcNow.AddDays(-60),
            SubscriptionId = ObjectId.GenerateNewId().ToString(),
            BillingCycleNumber = 1
        });

        await db.SaveChangesAsync();

        var gigServices = new GigServices(
            db,
            Mock.Of<ICareGiverService>(),
            Mock.Of<ILogger<GigServices>>(),
            null!,
            Mock.Of<IEligibilityService>(),
            Mock.Of<ICaregiverProfileService>(),
            Mock.Of<IGigImageModerationService>(),
            Mock.Of<MediatR.IMediator>());

        var resultMessage = await gigServices.SoftDeleteGigAsync(gigId, caregiverId);

        Assert.Contains("successfully deleted", resultMessage, StringComparison.OrdinalIgnoreCase);

        var deletedGig = await db.Gigs.FindAsync(ObjectId.Parse(gigId));
        Assert.True(deletedGig!.IsDeleted);

        Console.WriteLine($"DELETION_UNBLOCKED_GIG_EVIDENCE result={resultMessage} isDeleted={deletedGig.IsDeleted}");
    }

    [Fact]
    public async Task SupersededOrder_DoesNotBlockClientAccountDeletion()
    {
        using var db = CreateDb();

        var clientId = "client-account-deletion-check";

        db.ClientOrders.Add(new ClientOrder
        {
            Id = ObjectId.GenerateNewId(),
            ClientId = clientId,
            GigId = ObjectId.GenerateNewId().ToString(),
            CaregiverId = "caregiver-account-deletion-check",
            PaymentOption = "monthly",
            Amount = 50000,
            OrderFee = 40000m,
            ClientOrderStatus = "Superseded",
            OrderCreatedAt = DateTime.UtcNow.AddDays(-60),
            SubscriptionId = ObjectId.GenerateNewId().ToString(),
            BillingCycleNumber = 1
        });

        await db.SaveChangesAsync();

        var userDeletionService = new UserDeletionService(
            db,
            Mock.Of<MediatR.IMediator>(),
            Mock.Of<IEmailService>(),
            Mock.Of<ILogger<UserDeletionService>>(),
            Mock.Of<ITokenHandler>(),
            Mock.Of<IOriginValidationService>());

        var method = typeof(UserDeletionService).GetMethod(
            "GetClientBlockersAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        var blockersTask = (Task<List<string>>)method!.Invoke(userDeletionService, new object[] { clientId })!;
        var blockers = await blockersTask;

        Assert.Empty(blockers);

        Console.WriteLine($"DELETION_UNBLOCKED_CLIENT_EVIDENCE blockerCount={blockers.Count}");
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
            referralService.Object,
            Mock.Of<ICaregiverReadinessService>(),
            Mock.Of<MediatR.IMediator>());
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
            Mock.Of<IMarketingSyncService>(),
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
