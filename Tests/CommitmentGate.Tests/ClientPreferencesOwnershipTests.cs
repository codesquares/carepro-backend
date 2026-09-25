using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Application.DTOs;
using Application.Interfaces.Authentication;
using Application.Interfaces.Content;
using CarePro_Api;
using CarePro_Api.Controllers.Content;
using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.EntityFrameworkCore.Extensions;
using Moq;
using Xunit;

namespace CommitmentGate.Tests;

/// <summary>
/// ClientPreferences routes used to trust a client id supplied in the body / route / query (and PUT-by-record-id had no
/// ownership check at all), so any signed-in client could read or overwrite another client's preferences and read or
/// flip their marketing-email consent. Identity must come from the JWT.
/// </summary>
public class ClientPreferencesOwnershipTests
{
    private const string Me = "client-me";
    private const string Victim = "client-victim";

    private static ClaimsPrincipal Client(string id) =>
        new(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, id),
            new Claim(ClaimTypes.Role, "Client"),
        }, "TestAuth", ClaimTypes.Name, ClaimTypes.Role));

    private static (ClientPreferencesController c, Mock<IClientPreferenceService> svc) Build(string callerId)
    {
        var svc = new Mock<IClientPreferenceService>(MockBehavior.Strict); // any unexpected service call = failure
        var c = new ClientPreferencesController(
            svc.Object,
            Mock.Of<IClientService>(),
            Mock.Of<ITokenHandler>(),
            dbContext: null!, // only the anonymous unsubscribe routes touch it
            Mock.Of<ILogger<ClientPreferencesController>>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = Client(callerId) } }
        };
        return (c, svc);
    }

    // ---------- POST /ClientPreferences ----------

    [Fact]
    public async Task Post_WithAnotherClientsId_IsForbidden_AndNothingIsWritten()
    {
        var (c, svc) = Build(Me);

        var result = await c.AddClientPreferenceAsync(new AddClientPreferenceRequest { ClientId = Victim, Data = new() { "x:1" } });

        Assert.IsType<ForbidResult>(result);
        svc.Verify(s => s.CreateClientPreferenceAsync(It.IsAny<AddClientPreferenceRequest>()), Times.Never);
    }

    [Theory]
    [InlineData(Me)]
    [InlineData("")]
    [InlineData(null)]
    public async Task Post_ForSelf_OrBlankId_Works_AndServiceReceivesTheTokenIdentity(string? bodyId)
    {
        var (c, svc) = Build(Me);
        svc.Setup(s => s.CreateClientPreferenceAsync(It.IsAny<AddClientPreferenceRequest>())).ReturnsAsync("rec-1");

        var result = await c.AddClientPreferenceAsync(new AddClientPreferenceRequest { ClientId = bodyId, Data = new() { "x:1" } });

        Assert.IsType<OkObjectResult>(result);
        svc.Verify(s => s.CreateClientPreferenceAsync(It.Is<AddClientPreferenceRequest>(r => r.ClientId == Me)), Times.Once);
    }

    [Fact]
    public async Task Post_WithoutData_IsRejected_400_InsteadOfSilentlyWipingTheRecord()
    {
        var (c, svc) = Build(Me);

        var result = await c.AddClientPreferenceAsync(new AddClientPreferenceRequest { ClientId = Me, Data = null });

        Assert.IsType<BadRequestObjectResult>(result);
        svc.Verify(s => s.CreateClientPreferenceAsync(It.IsAny<AddClientPreferenceRequest>()), Times.Never);
    }

    // ---------- GET /ClientPreferences/clientId ----------

    [Fact]
    public async Task Get_AnotherClientsPreferences_IsForbidden()
    {
        var (c, svc) = Build(Me);

        var result = await c.GetCaregiverVerificationAsync(Victim);

        Assert.IsType<ForbidResult>(result);
        svc.Verify(s => s.GetClientPreferenceAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Get_OwnPreferences_Works()
    {
        var (c, svc) = Build(Me);
        svc.Setup(s => s.GetClientPreferenceAsync(Me)).ReturnsAsync(new ClientPreferenceDTO { ClientId = Me });

        Assert.IsType<OkObjectResult>(await c.GetCaregiverVerificationAsync(Me));
    }

    // ---------- PUT /ClientPreferences/preferenceId (by RECORD id) ----------

    [Fact]
    public async Task Put_OnAnotherClientsRecord_IsForbidden_AndNothingIsWritten()
    {
        var (c, svc) = Build(Me);
        svc.Setup(s => s.GetPreferenceOwnerIdAsync("rec-victim")).ReturnsAsync(Victim);

        var result = await c.UpdateVerificationAsync("rec-victim", new UpdateClientPreferenceRequest { Data = new() { "pwned:1" } });

        Assert.IsType<ForbidResult>(result.Result);
        svc.Verify(s => s.UpdateClientPreferenceAsync(It.IsAny<string>(), It.IsAny<UpdateClientPreferenceRequest>()), Times.Never);
    }

    [Fact]
    public async Task Put_OnUnknownRecord_Is404()
    {
        var (c, svc) = Build(Me);
        svc.Setup(s => s.GetPreferenceOwnerIdAsync("nope")).ReturnsAsync((string?)null);

        Assert.IsType<NotFoundObjectResult>((await c.UpdateVerificationAsync("nope", new UpdateClientPreferenceRequest { Data = new() })).Result);
    }

    [Fact]
    public async Task Put_OnOwnRecord_Works()
    {
        var (c, svc) = Build(Me);
        svc.Setup(s => s.GetPreferenceOwnerIdAsync("rec-me")).ReturnsAsync(Me);
        svc.Setup(s => s.UpdateClientPreferenceAsync("rec-me", It.IsAny<UpdateClientPreferenceRequest>())).ReturnsAsync("ok");

        Assert.IsType<OkObjectResult>((await c.UpdateVerificationAsync("rec-me", new UpdateClientPreferenceRequest { Data = new() { "a:1" } })).Result);
    }

    [Fact]
    public async Task Put_WithoutData_IsRejected_AndDoesNotWipe()
    {
        var (c, svc) = Build(Me);
        svc.Setup(s => s.GetPreferenceOwnerIdAsync("rec-me")).ReturnsAsync(Me);

        Assert.IsType<BadRequestObjectResult>((await c.UpdateVerificationAsync("rec-me", new UpdateClientPreferenceRequest { Data = null })).Result);
        svc.Verify(s => s.UpdateClientPreferenceAsync(It.IsAny<string>(), It.IsAny<UpdateClientPreferenceRequest>()), Times.Never);
    }

    // ---------- notification-preferences/{clientId}: marketing-email consent ----------

    [Fact]
    public async Task NotificationPrefs_Get_ForAnotherClient_IsForbidden()
    {
        var (c, svc) = Build(Me);

        Assert.IsType<ForbidResult>(await c.GetNotificationPreferencesAsync(Victim));
        svc.Verify(s => s.GetNotificationPreferencesAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task NotificationPrefs_Put_ForAnotherClient_IsForbidden_SoConsentCannotBeFlipped()
    {
        var (c, svc) = Build(Me);

        var result = await c.UpdateNotificationPreferencesAsync(Victim, new UpdateNotificationPreferencesRequest());

        Assert.IsType<ForbidResult>(result);
        svc.Verify(s => s.UpdateNotificationPreferencesAsync(It.IsAny<string>(), It.IsAny<UpdateNotificationPreferencesRequest>()), Times.Never);
    }

    [Fact]
    public async Task NotificationPrefs_ForSelf_StillWork()
    {
        var (c, svc) = Build(Me);
        svc.Setup(s => s.GetNotificationPreferencesAsync(Me)).ReturnsAsync(new NotificationPreferencesDTO());
        svc.Setup(s => s.UpdateNotificationPreferencesAsync(Me, It.IsAny<UpdateNotificationPreferencesRequest>())).ReturnsAsync(new NotificationPreferencesDTO());

        Assert.IsType<OkObjectResult>(await c.GetNotificationPreferencesAsync(Me));
        Assert.IsType<OkObjectResult>(await c.UpdateNotificationPreferencesAsync(Me, new UpdateNotificationPreferencesRequest()));
    }

    // ---------- the anonymous unsubscribe routes must stay anonymous ----------

    [Collection("EnvironmentVariableIsolation")]
    public class Anonymous : IClassFixture<CaregiverIdentityExposureHostFactory>
    {
        private readonly HttpClient _client;
        public Anonymous(CaregiverIdentityExposureHostFactory f) =>
            _client = f.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

        [Theory]
        [InlineData("GET", "/api/ClientPreferences/clientId?clientId=x")]
        [InlineData("POST", "/api/ClientPreferences")]
        [InlineData("PUT", "/api/ClientPreferences/preferenceId?preferenceId=x")]
        [InlineData("GET", "/api/ClientPreferences/notification-preferences/x")]
        [InlineData("PUT", "/api/ClientPreferences/notification-preferences/x")]
        public async Task PreferenceRoutes_Anonymous_Is401(string method, string path)
        {
            using var req = new HttpRequestMessage(new HttpMethod(method), path);
            using var res = await _client.SendAsync(req);
            Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        }

        [Fact]
        public async Task Unsubscribe_StillAnonymous_ReachesTheActionAndRejectsAMissingToken_400()
        {
            using var res = await _client.GetAsync("/api/ClientPreferences/unsubscribe");
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode); // not 401: still [AllowAnonymous]
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Real host + real MongoDB (replica set on 127.0.0.1:27018, like the other *HttpTests): two seeded clients, requests
// arrive with a real JWT-shaped principal chosen by header, and the DB is inspected afterwards.
// ─────────────────────────────────────────────────────────────────────────────
internal sealed class HeaderIdentityAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "HeaderIdentityTestAuth";
    public HeaderIdentityAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> o, ILoggerFactory l, UrlEncoder e) : base(o, l, e) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-UserId", out var id) || string.IsNullOrWhiteSpace(id))
            return Task.FromResult(AuthenticateResult.NoResult());
        var role = Request.Headers.TryGetValue("X-Test-Role", out var r) ? r.ToString() : "Client";
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim("userId", id.ToString()), new Claim(ClaimTypes.Role, role) };
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName, ClaimTypes.Name, ClaimTypes.Role)), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

public sealed class ClientPreferencesHostFactory : WebApplicationFactory<Program>
{
    public string DbName { get; } = $"carepro_clientprefs_http_{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(o =>
            {
                o.DefaultAuthenticateScheme = HeaderIdentityAuthHandler.SchemeName;
                o.DefaultChallengeScheme = HeaderIdentityAuthHandler.SchemeName;
            }).AddScheme<AuthenticationSchemeOptions, HeaderIdentityAuthHandler>(HeaderIdentityAuthHandler.SchemeName, _ => { });

            services.RemoveAll<DbContextOptions<CareProDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<CareProDbContext>();
            services.AddDbContext<CareProDbContext>(o => o.UseMongoDB("mongodb://127.0.0.1:27018", DbName));

            foreach (var h in services.Where(d => d.ServiceType == typeof(IHostedService)).ToList()) services.Remove(h);
        });
    }
}

public class ClientPreferencesRealStackTests : IClassFixture<ClientPreferencesHostFactory>
{
    private readonly ClientPreferencesHostFactory _factory;
    public ClientPreferencesRealStackTests(ClientPreferencesHostFactory f) => _factory = f;

    private HttpClient As(string userId, string role = "Client")
    {
        var c = _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        c.DefaultRequestHeaders.Add("X-Test-UserId", userId);
        c.DefaultRequestHeaders.Add("X-Test-Role", role);
        return c;
    }

    private async Task<(string aId, string bId, string aRec, string bRec)> SeedTwoClientsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CareProDbContext>();
        string Add(string name, string marker)
        {
            var cid = ObjectId.GenerateNewId();
            db.Clients.Add(new Client { Id = cid, FirstName = name, LastName = "Test", Email = $"{name}-{Guid.NewGuid():N}@example.com", Password = "x", Role = "Client", IsDeleted = false, Status = true, CreatedAt = DateTime.UtcNow }); // pragma: allowlist-secret
            var rec = new ClientPreference
            {
                Id = ObjectId.GenerateNewId(), ClientId = cid.ToString(), Data = new() { marker },
                NotificationPreferences = new NotificationPreferences { EmailNotifications = true, MarketingEmails = true, Promotions = true },
                CreatedAt = DateTime.UtcNow,
            };
            db.ClientPreferences.Add(rec);
            return $"{cid}|{rec.Id}";
        }
        var a = Add("Alice", "alice-secret-marker").Split('|');
        var b = Add("Bob", "bob-secret-marker").Split('|');
        await db.SaveChangesAsync();
        return (a[0], b[0], a[1], b[1]);
    }

    private async Task<ClientPreference> Rec(string clientId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CareProDbContext>();
        return await db.ClientPreferences.AsNoTracking().FirstAsync(p => p.ClientId == clientId);
    }

    [Fact]
    public async Task Alice_CannotReadOrWriteBobsPreferences_OnAnyRoute_AndBobsRecordIsUntouched()
    {
        var (alice, bob, aliceRec, bobRec) = await SeedTwoClientsAsync();
        var a = As(alice);

        // READ Bob's preferences
        var get = await a.GetAsync($"/api/ClientPreferences/clientId?clientId={bob}");
        Assert.Equal(HttpStatusCode.Forbidden, get.StatusCode);
        Assert.DoesNotContain("bob-secret-marker", await get.Content.ReadAsStringAsync());

        // WRITE Bob's preferences via the POST body id
        var post = await a.PostAsJsonAsync("/api/ClientPreferences", new { clientId = bob, data = new[] { "pwned:1" } });
        Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);

        // WRITE Bob's preferences via PUT by record id
        var put = await a.PutAsJsonAsync($"/api/ClientPreferences/preferenceId?preferenceId={bobRec}", new { data = new[] { "pwned:1" } });
        Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);

        // READ / FLIP Bob's marketing-email consent
        Assert.Equal(HttpStatusCode.Forbidden, (await a.GetAsync($"/api/ClientPreferences/notification-preferences/{bob}")).StatusCode);
        var flip = await a.PutAsJsonAsync($"/api/ClientPreferences/notification-preferences/{bob}", new { marketingEmails = false, promotions = false, emailNotifications = false });
        Assert.Equal(HttpStatusCode.Forbidden, flip.StatusCode);

        // Bob's data is exactly what it was
        var after = await Rec(bob);
        Assert.Equal(new[] { "bob-secret-marker" }, after.Data);
        Assert.True(after.NotificationPreferences!.MarketingEmails);
        Assert.True(after.NotificationPreferences.Promotions);
        Assert.True(after.NotificationPreferences.EmailNotifications);
    }

    [Fact]
    public async Task Alice_CanReadAndWriteHerOwn_OnEveryRoute()
    {
        var (alice, _, aliceRec, _) = await SeedTwoClientsAsync();
        var a = As(alice);

        var get = await a.GetAsync($"/api/ClientPreferences/clientId?clientId={alice}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Contains("alice-secret-marker", await get.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, (await a.PostAsJsonAsync("/api/ClientPreferences", new { clientId = alice, data = new[] { "post-own:1" } })).StatusCode);
        Assert.Equal(new[] { "post-own:1" }, (await Rec(alice)).Data);

        // a body with no clientId still works (identity comes from the token)
        Assert.Equal(HttpStatusCode.OK, (await a.PostAsJsonAsync("/api/ClientPreferences", new { data = new[] { "blank-id:1" } })).StatusCode);
        Assert.Equal(new[] { "blank-id:1" }, (await Rec(alice)).Data);

        Assert.Equal(HttpStatusCode.OK, (await a.PutAsJsonAsync($"/api/ClientPreferences/preferenceId?preferenceId={aliceRec}", new { data = new[] { "put-own:1" } })).StatusCode);
        Assert.Equal(new[] { "put-own:1" }, (await Rec(alice)).Data);

        Assert.Equal(HttpStatusCode.OK, (await a.GetAsync($"/api/ClientPreferences/notification-preferences/{alice}")).StatusCode);
        var flip = await a.PutAsJsonAsync($"/api/ClientPreferences/notification-preferences/{alice}", new { marketingEmails = false });
        Assert.Equal(HttpStatusCode.OK, flip.StatusCode);
    }

    [Fact]
    public async Task ADataLessBody_IsRejected_AndDoesNotWipeTheRecord()
    {
        var (alice, _, aliceRec, _) = await SeedTwoClientsAsync();
        var a = As(alice);

        // the shape PaymentSuccess.jsx's legacy savePendingTasks sends: no `data`
        var post = await a.PostAsJsonAsync("/api/ClientPreferences", new { clientId = alice, orderId = "o1", tasks = new[] { "t" } });
        Assert.Equal(HttpStatusCode.BadRequest, post.StatusCode);
        Assert.Equal(new[] { "alice-secret-marker" }, (await Rec(alice)).Data);
    }

    [Fact]
    public async Task ACaregiver_CannotUseTheseRoutes_AtAll()
    {
        var (alice, _, _, _) = await SeedTwoClientsAsync();
        var res = await As("some-caregiver", "Caregiver").GetAsync($"/api/ClientPreferences/clientId?clientId={alice}");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }
}
