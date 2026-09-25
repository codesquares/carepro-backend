using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using CarePro_Api;
using Infrastructure.Content.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.EntityFrameworkCore.Extensions;
using Xunit;

namespace CommitmentGate.Tests;

/// <summary>
/// Real end-to-end HTTP coverage for the Phase 3 admin Package endpoints:
/// the OperationsPolicy gate is exercised by the actual authorization pipeline
/// (a header on the request selects which principal the test auth handler emits),
/// and the happy path runs the real controller + PackageService + MongoDB.
/// </summary>
public sealed class AdminPackagesHostFactory : WebApplicationFactory<Program>
{
    public string DbName { get; } = $"carepro_package_http_{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = HeaderRoleAuthHandler.Scheme;
                options.DefaultChallengeScheme = HeaderRoleAuthHandler.Scheme;
            }).AddScheme<AuthenticationSchemeOptions, HeaderRoleAuthHandler>(HeaderRoleAuthHandler.Scheme, _ => { });

            // Point the DbContext at the test MongoDB (replica set on 27018).
            services.RemoveAll<DbContextOptions<CareProDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<CareProDbContext>();
            services.AddDbContext<CareProDbContext>(o =>
                o.UseMongoDB("mongodb://127.0.0.1:27018", DbName));

            var hosted = services.Where(d => d.ServiceType == typeof(IHostedService)).ToList();
            foreach (var h in hosted) services.Remove(h);
        });
    }
}

/// <summary>
/// Emits a principal based on the <c>X-Test-Role</c> header:
///   (absent)              → 401 (unauthenticated)
///   "Caregiver"           → authenticated non-admin → OperationsPolicy 403
///   "Admin:MarketingAndSales" → admin in the wrong department → 403
///   "Admin:HR"            → admin in an operations department → allowed
///   "SuperAdmin"          → allowed
/// </summary>
internal sealed class HeaderRoleAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string Scheme = "HeaderRoleTestAuth";

    public HeaderRoleAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-Role", out var raw) || string.IsNullOrWhiteSpace(raw))
            return Task.FromResult(AuthenticateResult.NoResult());

        var value = raw.ToString();
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "test-admin-id"),
            new(ClaimTypes.Email, "ops@carepro.test"),
            new("userId", "test-admin-id"),
        };

        if (value.StartsWith("Admin:", StringComparison.Ordinal))
        {
            claims.Add(new Claim(ClaimTypes.Role, "Admin"));
            claims.Add(new Claim("department", value["Admin:".Length..]));
        }
        else
        {
            claims.Add(new Claim(ClaimTypes.Role, value));
        }

        var identity = new ClaimsIdentity(claims, Scheme, ClaimTypes.Name, ClaimTypes.Role);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

public class AdminPackagesHttpAuthTests : IClassFixture<AdminPackagesHostFactory>
{
    private readonly AdminPackagesHostFactory _factory;

    public AdminPackagesHttpAuthTests(AdminPackagesHostFactory factory) => _factory = factory;

    private HttpClient Client(string? role)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
        if (role != null) client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private static object SamplePackage(string category = "Post Surgery Care", string tier = "Standard",
        string type = "RegisteredNurse", string? specialty = null) => new
    {
        category,
        tierLabel = tier,
        requiredCaregiverType = type,
        requiredSpecialty = specialty,
        basePrice = 250000,
        additionalDayPrice = (decimal?)null,
        payCalculationType = "Hourly",
        fixedCaregiverPay = (decimal?)null,
        description = "created via HTTP test",
        isActive = true
    };

    // ─────────────────────── OperationsPolicy gate ───────────────────────

    [Fact]
    public async Task Unauthenticated_IsRejected_401()
    {
        var res = await Client(null).PostAsJsonAsync("/api/admin/Packages", SamplePackage());
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task NonAdminCaller_IsRejected_403()
    {
        var res = await Client("Caregiver").PostAsJsonAsync("/api/admin/Packages", SamplePackage());
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task AdminInWrongDepartment_IsRejected_403()
    {
        var res = await Client("Admin:MarketingAndSales").GetAsync("/api/admin/Packages");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task OperationsAdmin_IsAllowed()
    {
        var res = await Client("Admin:HR").GetAsync("/api/admin/Packages");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    // ─────────────────── Full CRUD through the endpoints ───────────────────

    [Fact]
    public async Task SuperAdmin_CanCreateReadUpdateDelete_ThroughEndpoints()
    {
        var client = Client("SuperAdmin");

        // CREATE
        var createRes = await client.PostAsJsonAsync("/api/admin/Packages",
            SamplePackage("Post-Partum Care", "Premium", "RegisteredNurse", "Midwifery"));
        Assert.True(createRes.StatusCode == HttpStatusCode.OK,
            $"CREATE failed: {createRes.StatusCode} — {await createRes.Content.ReadAsStringAsync()}");
        var created = await createRes.Content.ReadFromJsonAsync<CreateEnvelope>();
        Assert.True(created!.Success);
        var id = created.Data!.Id;
        Assert.Equal("Post-Partum Care", created.Data.Category);
        Assert.Equal("RegisteredNurse", created.Data.RequiredCaregiverType);
        Assert.Equal("Midwifery", created.Data.RequiredSpecialty);

        // READ (detail)
        var getRes = await client.GetAsync($"/api/admin/Packages/{id}");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var fetched = await getRes.Content.ReadFromJsonAsync<CreateEnvelope>();
        Assert.Equal("Premium", fetched!.Data!.TierLabel);

        // READ (list)
        var listRes = await client.GetAsync("/api/admin/Packages");
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);

        // UPDATE
        var updRes = await client.PutAsJsonAsync($"/api/admin/Packages/{id}", new { basePrice = 300000, isActive = false });
        Assert.Equal(HttpStatusCode.OK, updRes.StatusCode);
        var afterUpdate = await (await client.GetAsync($"/api/admin/Packages/{id}"))
            .Content.ReadFromJsonAsync<CreateEnvelope>();
        Assert.Equal(300000m, afterUpdate!.Data!.BasePrice);
        Assert.False(afterUpdate.Data.IsActive);
        Assert.Equal("RegisteredNurse", afterUpdate.Data.RequiredCaregiverType); // untouched

        // DELETE
        var delRes = await client.DeleteAsync($"/api/admin/Packages/{id}");
        Assert.Equal(HttpStatusCode.OK, delRes.StatusCode);
        var goneRes = await client.GetAsync($"/api/admin/Packages/{id}");
        Assert.Equal(HttpStatusCode.NotFound, goneRes.StatusCode);
    }

    [Fact]
    public async Task Create_InvalidCategory_ThroughEndpoint_Returns400()
    {
        var res = await Client("SuperAdmin").PostAsJsonAsync("/api/admin/Packages",
            SamplePackage(category: "Pet Sitting"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    private sealed class CreateEnvelope
    {
        public bool Success { get; set; }
        public PackageBody? Data { get; set; }
    }

    private sealed class PackageBody
    {
        public string Id { get; set; } = "";
        public string Category { get; set; } = "";
        public string TierLabel { get; set; } = "";
        public string RequiredCaregiverType { get; set; } = "";
        public string? RequiredSpecialty { get; set; }
        public decimal BasePrice { get; set; }
        public bool IsActive { get; set; }
    }
}
