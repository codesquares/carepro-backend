using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Application.DTOs;
using Application.Interfaces.Content;
using Domain.Entities;
using Infrastructure.Content.Data;
using Infrastructure.Content.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using MongoDB.Bson;
using MongoDB.EntityFrameworkCore.Extensions;
using Xunit;

namespace CommitmentGate.Tests;

/// <summary>
/// Client-facing recurring package subscriptions: the gateway fee is part of every renewal charge, a client can
/// list / view / cancel only their own, a cancelled subscription is never billed again, and the HTTP surface is
/// authenticated and client-only. Same fake-Flutterwave convention as <see cref="PackageSubscriptionTests"/>: the
/// charge call is genuinely rejected by the live API with a fake key, and the amount that was attempted is read
/// back from the recorded payment attempt.
/// </summary>
public class PackageSubscriptionClientTests
{
    private static CareProDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<CareProDbContext>()
            .UseMongoDB("mongodb://127.0.0.1:27018", $"carepro_pkgsubclient_tests_{Guid.NewGuid():N}")
            .Options;
        return new TestPackagePaymentDbContext(options);
    }

    private static PackageSubscriptionService CreateService(CareProDbContext db)
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
        var flw = new FlutterwaveService(config, Mock.Of<ILogger<FlutterwaveService>>());
        return new PackageSubscriptionService(db, flw, Mock.Of<IMediator>(), Mock.Of<ILogger<PackageSubscriptionService>>());
    }

    private static Task<PackageSubscription> Create(PackageSubscriptionService svc, string clientId, decimal amount = 65000m,
        string? token = "tok-1", string? requestId = null)
        => svc.CreatePackageSubscriptionAsync(new CreatePackageSubscriptionRequest
        {
            PackageRequestId = requestId ?? ObjectId.GenerateNewId().ToString(),
            ClientId = clientId,
            RecurringAmount = amount,
            Currency = "NGN",
            Email = "client@example.com",
            FlutterwavePaymentToken = token,
            CardLastFour = token == null ? null : "4242",
            CardBrand = token == null ? null : "VISA"
        });

    // ── Gateway fee in the renewal amount ─────────────────────────────────────

    [Theory]
    [InlineData(65000, 910)]        // the staging payment: 1.4% of ₦65,000
    [InlineData(60000, 840)]
    [InlineData(500000, 2000)]      // capped at ₦2,000
    [InlineData(0, 0)]
    public void GatewayFee_IsOnePointFourPercent_CappedAtTwoThousand(int baseAmount, int expectedFee)
    {
        using var db = CreateDb();
        Assert.Equal(expectedFee, CreateService(db).CalculateGatewayFee(baseAmount));
    }

    [Fact]
    public async Task Create_StoresBaseAndFee_AndRecurringAmountIsTheTotal()
    {
        using var db = CreateDb();
        var sub = await Create(CreateService(db), "client-a");

        Assert.Equal(65000m, sub.BaseAmount);
        Assert.Equal(910m, sub.GatewayFee);
        Assert.Equal(65910m, sub.RecurringAmount);
    }

    [Fact]
    public async Task RenewalCharge_AttemptsTheBasePlusFeeTotal_NotTheBaseAlone()
    {
        using var db = CreateDb();
        var svc = CreateService(db);
        var sub = await Create(svc, "client-a");

        await svc.ProcessRecurringPackageChargeAsync(sub.Id.ToString());

        var stored = await db.PackageSubscriptions.FirstAsync(s => s.Id == sub.Id);
        var attempt = Assert.Single(stored.PaymentHistory);
        Assert.Equal(65910m, attempt.Amount); // what was sent to Flutterwave for the renewal
    }

    // ── List / get: ownership ─────────────────────────────────────────────────

    [Fact]
    public async Task List_ReturnsOnlyTheCallersSubscriptions_WithPackageNameAndCardState()
    {
        using var db = CreateDb();
        var svc = CreateService(db);

        var request = new PackageRequest
        {
            Id = ObjectId.GenerateNewId(), ClientId = "client-a", PackageId = "p1",
            PackageCategory = "Adult/Elder Care", PackageTierLabel = "Essential Companion",
            Status = PackageRequestStatuses.Pending, BillingType = PackageRequestBillingTypes.Recurring
        };
        db.PackageRequests.Add(request);
        await db.SaveChangesAsync();

        await Create(svc, "client-a", token: "tok", requestId: request.Id.ToString());
        await Create(svc, "client-a", token: null);          // tokenless: exists but cannot renew
        await Create(svc, "client-b");

        var list = await svc.GetClientSubscriptionsAsync("client-a");

        Assert.Equal(2, list.Count);
        Assert.All(list, s => Assert.NotEqual("client-b", s.PackageRequestId));
        var named = list.Single(s => s.PackageRequestId == request.Id.ToString());
        Assert.Equal("Adult/Elder Care", named.PackageCategory);
        Assert.Equal("Essential Companion", named.PackageTierLabel);
        Assert.True(named.HasSavedCard);
        Assert.Equal("4242", named.CardLastFour);
        Assert.Single(list, s => !s.HasSavedCard);
        Assert.All(list, s => Assert.Null(s.Payments)); // list projection carries no history
    }

    [Fact]
    public async Task Get_AnotherClientsSubscription_ReturnsNull_SameAsMissing()
    {
        using var db = CreateDb();
        var svc = CreateService(db);
        var sub = await Create(svc, "client-a");

        Assert.NotNull(await svc.GetClientSubscriptionAsync("client-a", sub.Id.ToString()));
        Assert.Null(await svc.GetClientSubscriptionAsync("client-b", sub.Id.ToString()));
        Assert.Null(await svc.GetClientSubscriptionAsync("client-a", ObjectId.GenerateNewId().ToString()));
        Assert.Null(await svc.GetClientSubscriptionAsync("client-a", "not-an-id"));
    }

    [Fact]
    public async Task Get_IncludesPaymentHistory_NewestFirst()
    {
        using var db = CreateDb();
        var svc = CreateService(db);
        var sub = await Create(svc, "client-a");
        var stored = await db.PackageSubscriptions.FirstAsync(s => s.Id == sub.Id);
        stored.PaymentHistory.Add(new PackageSubscriptionPaymentRecord { Status = "successful", Amount = 65910m, BillingCycleNumber = 1, AttemptedAt = DateTime.UtcNow.AddDays(-60) });
        stored.PaymentHistory.Add(new PackageSubscriptionPaymentRecord { Status = "successful", Amount = 65910m, BillingCycleNumber = 2, AttemptedAt = DateTime.UtcNow.AddDays(-30) });
        await db.SaveChangesAsync();

        var dto = await svc.GetClientSubscriptionAsync("client-a", sub.Id.ToString());

        Assert.Equal(new[] { 2, 1 }, dto!.Payments!.Select(p => p.BillingCycleNumber).ToArray());
    }

    // ── Cancel ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cancel_StopsAutoRenewal_KeepsPaidPeriod_AndIsNeverBilledAgain()
    {
        using var db = CreateDb();
        var svc = CreateService(db);
        var sub = await Create(svc, "client-a");
        var stored = await db.PackageSubscriptions.FirstAsync(s => s.Id == sub.Id);
        stored.NextChargeDate = DateTime.UtcNow.AddMinutes(-5); // matured: would be billed right now
        await db.SaveChangesAsync();
        Assert.Contains(await svc.GetPackageSubscriptionsDueForBillingAsync(), s => s.Id == sub.Id);

        var result = await svc.CancelClientSubscriptionAsync("client-a", sub.Id.ToString(), "Moving abroad");

        Assert.True(result.IsSuccess);
        Assert.Equal("PendingCancellation", result.Value!.Status);
        Assert.True(result.Value.CancelAtPeriodEnd);
        Assert.False(result.Value.AutoRenew);
        Assert.Null(result.Value.NextChargeDate);

        var after = await db.PackageSubscriptions.FirstAsync(s => s.Id == sub.Id);
        Assert.Equal("Client:client-a", after.CancelledBy);
        Assert.Equal("Moving abroad", after.CancellationReason);
        Assert.Equal(sub.CurrentPeriodEnd, after.CurrentPeriodEnd); // paid period untouched
        Assert.DoesNotContain(await svc.GetPackageSubscriptionsDueForBillingAsync(), s => s.Id == sub.Id);
    }

    [Fact]
    public async Task Cancel_IsIdempotent_AndKeepsTheFirstReason()
    {
        using var db = CreateDb();
        var svc = CreateService(db);
        var sub = await Create(svc, "client-a");

        await svc.CancelClientSubscriptionAsync("client-a", sub.Id.ToString(), "first");
        var again = await svc.CancelClientSubscriptionAsync("client-a", sub.Id.ToString(), "second");

        Assert.True(again.IsSuccess);
        Assert.Equal("first", (await db.PackageSubscriptions.FirstAsync(s => s.Id == sub.Id)).CancellationReason);
    }

    [Fact]
    public async Task Cancel_AnotherClientsSubscription_Fails_AndChangesNothing()
    {
        using var db = CreateDb();
        var svc = CreateService(db);
        var sub = await Create(svc, "client-a");

        var result = await svc.CancelClientSubscriptionAsync("client-b", sub.Id.ToString(), null);

        Assert.False(result.IsSuccess);
        var after = await db.PackageSubscriptions.FirstAsync(s => s.Id == sub.Id);
        Assert.Equal(SubscriptionStatus.Active, after.Status);
        Assert.True(after.AutoRenew);
        Assert.NotNull(after.NextChargeDate);
    }
}

/// <summary>
/// The renewal charge end to end against a local fake of Flutterwave's HTTP API (the service's base URL is
/// configurable): the exact body sent to the tokenized-charge endpoint, the server-side verification of the amount,
/// and what the subscription looks like afterwards — for a success and for a charge that came back at the wrong amount.
/// </summary>
public sealed class FakeFlutterwave : IDisposable
{
    private readonly System.Net.HttpListener _listener = new();
    public string BaseUrl { get; }
    public List<JsonElement> TokenizedChargeBodies { get; } = new();
    /// <summary>Amount the fake reports back when the charge is verified; null = echo what was charged.</summary>
    public decimal? VerifiedAmountOverride { get; set; }
    private decimal _lastCharged;

    public FakeFlutterwave()
    {
        var port = new Random().Next(20000, 40000);
        BaseUrl = $"http://127.0.0.1:{port}";
        _listener.Prefixes.Add(BaseUrl + "/");
        _listener.Start();
        _ = Task.Run(Loop);
    }

    private async Task Loop()
    {
        while (_listener.IsListening)
        {
            System.Net.HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); } catch { return; }
            string body;
            using (var r = new System.IO.StreamReader(ctx.Request.InputStream)) body = await r.ReadToEndAsync();
            string json;
            var path = ctx.Request.Url!.AbsolutePath;
            if (path == "/v3/tokenized-charges")
            {
                var doc = JsonDocument.Parse(body).RootElement.Clone();
                TokenizedChargeBodies.Add(doc);
                _lastCharged = doc.GetProperty("amount").GetDecimal();
                json = $"{{\"status\":\"success\",\"data\":{{\"id\":777001,\"status\":\"successful\",\"amount\":{_lastCharged}}}}}";
            }
            else if (path.StartsWith("/v3/transactions/") && path.EndsWith("/verify"))
            {
                var verified = VerifiedAmountOverride ?? _lastCharged;
                json = $"{{\"status\":\"success\",\"data\":{{\"id\":777001,\"status\":\"successful\",\"tx_ref\":\"x\",\"amount\":{verified},\"currency\":\"NGN\"}}}}";
            }
            else json = "{\"status\":\"error\",\"message\":\"not found\"}";
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            ctx.Response.ContentType = "application/json";
            await ctx.Response.OutputStream.WriteAsync(bytes);
            ctx.Response.Close();
        }
    }

    public void Dispose() { try { _listener.Stop(); _listener.Close(); } catch { } }
}

public class PackageSubscriptionRenewalFlowTests
{
    private static (CareProDbContext db, PackageSubscriptionService svc) Build(FakeFlutterwave fake, IMediator? mediator = null, string? dbName = null)
    {
        var options = new DbContextOptionsBuilder<CareProDbContext>()
            .UseMongoDB("mongodb://127.0.0.1:27018", dbName ?? $"carepro_pkgsub_renewal_{Guid.NewGuid():N}")
            .Options;
        var db = new TestPackagePaymentDbContext(options);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Flutterwave:SecretKey"] = "test-secret", ["Flutterwave:PublicKey"] = "test-public",
                ["Flutterwave:EncryptionKey"] = "test-encryption", ["Flutterwave:WebhookSecretHash"] = "h",
                ["Flutterwave:BaseUrl"] = fake.BaseUrl, ["FrontendUrl"] = "https://example.com"
            })
            .Build();
        var flw = new FlutterwaveService(config, Mock.Of<ILogger<FlutterwaveService>>());
        return (db, new PackageSubscriptionService(db, flw, mediator ?? Mock.Of<IMediator>(), Mock.Of<ILogger<PackageSubscriptionService>>()));
    }

    private static Task<PackageSubscription> Seed(PackageSubscriptionService svc) =>
        svc.CreatePackageSubscriptionAsync(new CreatePackageSubscriptionRequest
        {
            PackageRequestId = ObjectId.GenerateNewId().ToString(), ClientId = "client-a", RecurringAmount = 65000m,
            Email = "client@example.com", FlutterwavePaymentToken = "flw-t1nf-test-token"
        });

    /// <summary>A subscription exactly as the pre-fee-split code stored it: BaseAmount / GatewayFee are not in the
    /// document at all. (The staging subscription created before this change looks like this.)</summary>
    private static ObjectId InsertLegacyDocument(string dbName, string clientId, decimal charge)
    {
        var id = ObjectId.GenerateNewId();
        var now = DateTime.UtcNow;
        var doc = new MongoDB.Bson.BsonDocument
        {
            { "_id", id },
            { "PackageRequestId", ObjectId.GenerateNewId().ToString() },
            { "ClientId", clientId },
            { "RecurringAmount", MongoDB.Bson.BsonDecimal128.Create(charge) },
            { "Currency", "NGN" },
            { "Email", "client@example.com" },
            { "Status", 0 },
            { "CurrentPeriodStart", now.AddDays(-30) },
            { "CurrentPeriodEnd", now },
            { "NextChargeDate", now.AddMinutes(-5) },     // due right now
            { "BillingCyclesCompleted", 0 },
            { "AutoRenew", true },
            { "FlutterwavePaymentToken", "flw-t1nf-legacy-token" },
            { "CardLastFour", "4242" }, { "CardBrand", "VISA" }, { "CardExpiry", "09/30" },
            { "FailedChargeAttempts", 0 }, { "MaxRetryAttempts", 3 },
            { "CancelAtPeriodEnd", false },
            { "PaymentHistory", new MongoDB.Bson.BsonArray() },
            { "CreatedAt", now.AddDays(-30) }, { "UpdatedAt", now.AddDays(-30) }
        };
        new MongoDB.Driver.MongoClient("mongodb://127.0.0.1:27018").GetDatabase(dbName)
            .GetCollection<MongoDB.Bson.BsonDocument>("PackageSubscriptions").InsertOne(doc);
        return id;
    }

    [Fact]
    public async Task LegacyRecord_WithNoSplitFieldsStored_IsStillReadable_ListedBilledAndCancellable_WithNoUpdateNeeded()
    {
        using var fake = new FakeFlutterwave();
        var dbName = $"carepro_pkgsub_legacy_{Guid.NewGuid():N}";
        var (db, svc) = Build(fake, dbName: dbName);
        using var _ = db;
        var legacyId = InsertLegacyDocument(dbName, "client-a", 65000m);

        // Client views: base = the whole stored charge, no fee line.
        var list = await svc.GetClientSubscriptionsAsync("client-a");
        var item = Assert.Single(list);
        Assert.Equal(65000m, item.BaseAmount);
        Assert.Equal(0m, item.GatewayFee);
        Assert.Equal(65000m, item.RecurringAmount);
        Assert.NotNull(await svc.GetClientSubscriptionAsync("client-a", legacyId.ToString()));

        // The billing sweep can load it, and a renewal charges exactly what it always did (no surprise fee).
        Assert.Contains(await svc.GetPackageSubscriptionsDueForBillingAsync(), s => s.Id == legacyId);
        var charge = await svc.ProcessRecurringPackageChargeAsync(legacyId.ToString());
        Assert.True(charge.IsSuccess, string.Join(",", charge.Errors));
        Assert.Equal(65000m, Assert.Single(fake.TokenizedChargeBodies).GetProperty("amount").GetDecimal());

        // And the client can cancel it.
        var cancel = await svc.CancelClientSubscriptionAsync("client-a", legacyId.ToString(), null);
        Assert.True(cancel.IsSuccess);
        Assert.Equal("PendingCancellation", cancel.Value!.Status);
    }

    [Fact]
    public async Task Renewal_ChargesBasePlusFee_VerifiesIt_AndRollsTheSubscriptionForward()
    {
        using var fake = new FakeFlutterwave();
        var (db, svc) = Build(fake);
        using var _ = db;
        var sub = await Seed(svc);

        var result = await svc.ProcessRecurringPackageChargeAsync(sub.Id.ToString());

        Assert.True(result.IsSuccess, string.Join(",", result.Errors));
        var sent = Assert.Single(fake.TokenizedChargeBodies);
        Assert.Equal(65910m, sent.GetProperty("amount").GetDecimal());      // ₦65,000 + ₦910 fee goes to Flutterwave
        Assert.Equal("flw-t1nf-test-token", sent.GetProperty("token").GetString());
        Assert.Equal("client@example.com", sent.GetProperty("email").GetString());

        var stored = await db.PackageSubscriptions.FirstAsync(s => s.Id == sub.Id);
        Assert.Equal(SubscriptionStatus.Active, stored.Status);
        Assert.Equal(1, stored.BillingCyclesCompleted);
        var payment = Assert.Single(stored.PaymentHistory);
        Assert.Equal("successful", payment.Status);
        Assert.Equal(65910m, payment.Amount);
        Assert.True(stored.NextChargeDate > DateTime.UtcNow.AddDays(29));   // next month scheduled
        Assert.Equal(65910m, stored.RecurringAmount);                        // unchanged for the following renewal
    }

    private static (Mock<IMediator> mediator, List<Application.Commands.SendNotificationCommand> sent) CaptureNotifications()
    {
        var sent = new List<Application.Commands.SendNotificationCommand>();
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<Application.Commands.SendNotificationCommand>(), It.IsAny<System.Threading.CancellationToken>()))
            .Callback<IRequest<string>, System.Threading.CancellationToken>((c, _) => sent.Add((Application.Commands.SendNotificationCommand)c))
            .ReturnsAsync("n1");
        return (mediator, sent);
    }

    [Fact]
    public async Task Notifications_PointAtTheSubscription_SoTheClientLinkOpensTheRightPage()
    {
        using var fake = new FakeFlutterwave();
        var (mediator, sent) = CaptureNotifications();
        var (db, svc) = Build(fake, mediator.Object);
        using var _ = db;
        var sub = await Seed(svc);

        await svc.ProcessRecurringPackageChargeAsync(sub.Id.ToString());

        var note = Assert.Single(sent);
        Assert.Equal(Application.DTOs.NotificationTypes.RecurringPaymentSuccessful, note.Type);
        // The client's notification link opens /subscriptions/{relatedEntityId}: it must be the subscription, not the request.
        Assert.Equal(sub.Id.ToString(), note.RelatedEntityId);
        Assert.NotEqual(sub.PackageRequestId, note.RelatedEntityId);
    }

    [Fact]
    public async Task SuspensionNotification_AlsoPointsAtTheSubscription()
    {
        using var fake = new FakeFlutterwave { VerifiedAmountOverride = 1m };
        var (mediator, sent) = CaptureNotifications();
        var (db, svc) = Build(fake, mediator.Object);
        using var _ = db;
        var sub = await Seed(svc);

        await svc.ProcessRecurringPackageChargeAsync(sub.Id.ToString());

        var note = Assert.Single(sent);
        Assert.Equal(Application.DTOs.NotificationTypes.SubscriptionSuspended, note.Type);
        Assert.Equal(sub.Id.ToString(), note.RelatedEntityId);
    }

    [Fact]
    public async Task Renewal_WhereFlutterwaveVerifiesTheBaseOnly_IsRejectedAsAmountMismatch_AndStopsBilling()
    {
        using var fake = new FakeFlutterwave { VerifiedAmountOverride = 65000m };
        var (db, svc) = Build(fake);
        using var _ = db;
        var sub = await Seed(svc);

        var result = await svc.ProcessRecurringPackageChargeAsync(sub.Id.ToString());

        Assert.False(result.IsSuccess);
        var stored = await db.PackageSubscriptions.FirstAsync(s => s.Id == sub.Id);
        Assert.Equal("amount_mismatch", Assert.Single(stored.PaymentHistory).Status);
        Assert.Equal(0, stored.BillingCyclesCompleted);                      // not counted as a paid renewal
        Assert.Equal(SubscriptionStatus.Suspended, stored.Status);           // non-retryable: never auto-retried
    }
}

/// <summary>HTTP layer: real auth pipeline + real controller + real service + MongoDB.</summary>
public class PackageSubscriptionClientHttpTests : IClassFixture<AdminPackagesHostFactory>
{
    private readonly AdminPackagesHostFactory _factory;
    public PackageSubscriptionClientHttpTests(AdminPackagesHostFactory factory) => _factory = factory;

    private System.Net.Http.HttpClient Http(string? role, string? userId = null)
    {
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
        if (role != null) client.DefaultRequestHeaders.Add("X-Test-Role", role);
        if (userId != null) client.DefaultRequestHeaders.Add("X-Test-UserId", userId);
        return client;
    }

    private async Task<string> SeedAsync(string clientId, bool withToken = true)
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IPackageSubscriptionService>();
        var sub = await svc.CreatePackageSubscriptionAsync(new CreatePackageSubscriptionRequest
        {
            PackageRequestId = ObjectId.GenerateNewId().ToString(),
            ClientId = clientId,
            RecurringAmount = 65000m,
            Email = "client@example.com",
            FlutterwavePaymentToken = withToken ? "secret-token-value" : null
        });
        return sub.Id.ToString();
    }

    [Fact]
    public async Task Unauthenticated_401_AndNonClientRoles_403()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Http(null).GetAsync("/api/client/package-subscriptions")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Http("Caregiver").GetAsync("/api/client/package-subscriptions")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Http("Admin:Finance").PostAsJsonAsync("/api/client/package-subscriptions/abc/cancel", new { })).StatusCode);
    }

    [Fact]
    public async Task Client_ListsOwnOnly_SeesFeeInclusiveAmount_AndNeverTheCardToken()
    {
        var mine = await SeedAsync("http-client-1");
        await SeedAsync("http-client-2");

        var res = await Http("Client", "http-client-1").GetAsync("/api/client/package-subscriptions");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var raw = await res.Content.ReadAsStringAsync();
        Assert.DoesNotContain("secret-token-value", raw);
        Assert.DoesNotContain("flutterwavePaymentToken", raw, StringComparison.OrdinalIgnoreCase);

        using var doc = JsonDocument.Parse(raw);
        var items = doc.RootElement.GetProperty("data").EnumerateArray().ToList();
        var item = Assert.Single(items);
        Assert.Equal(mine, item.GetProperty("id").GetString());
        Assert.Equal(65910m, item.GetProperty("recurringAmount").GetDecimal());
        Assert.Equal(910m, item.GetProperty("gatewayFee").GetDecimal());
        Assert.True(item.GetProperty("hasSavedCard").GetBoolean());
    }

    [Fact]
    public async Task Client_CannotReadOrCancelSomeoneElses_404_AndItStaysActive()
    {
        var id = await SeedAsync("http-owner");

        Assert.Equal(HttpStatusCode.NotFound, (await Http("Client", "http-intruder").GetAsync($"/api/client/package-subscriptions/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Http("Client", "http-intruder").PostAsJsonAsync($"/api/client/package-subscriptions/{id}/cancel", new { })).StatusCode);

        var res = await Http("Client", "http-owner").GetAsync($"/api/client/package-subscriptions/{id}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.Equal("Active", doc.RootElement.GetProperty("data").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Client_Cancels_ThenDetailShowsPendingCancellation_AndNoNextCharge()
    {
        var id = await SeedAsync("http-canceller", withToken: false);

        var cancel = await Http("Client", "http-canceller").PostAsJsonAsync($"/api/client/package-subscriptions/{id}/cancel", new { reason = "No longer needed" });
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);

        var res = await Http("Client", "http-canceller").GetAsync($"/api/client/package-subscriptions/{id}");
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal("PendingCancellation", data.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("nextChargeDate").ValueKind);
        Assert.False(data.GetProperty("hasSavedCard").GetBoolean());
    }
}
