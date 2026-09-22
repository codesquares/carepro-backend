using System;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Xunit;

namespace CommitmentGate.Tests;

/// <summary>
/// Real end-to-end HTTP coverage for Option A's two new surfaces:
///   1. POST /api/admin/package-payments/initiate — OperationsPolicy gate, exercised
///      through the real authorization pipeline (same infra as AdminPackagesHttpAuthTests).
///   2. POST /api/client/package-requests — confirms the self-service creation endpoint
///      removed as part of this change is genuinely gone (404 from routing, not just
///      "no test happens to call it"), while GET on the same route (list) still works.
/// Reuses <see cref="AdminPackagesHostFactory"/> and <see cref="HeaderRoleAuthHandler"/>.
/// </summary>
public class AdminPackagePaymentsHttpTests : IClassFixture<AdminPackagesHostFactory>
{
    private readonly AdminPackagesHostFactory _factory;

    public AdminPackagePaymentsHttpTests(AdminPackagesHostFactory factory) => _factory = factory;

    private System.Net.Http.HttpClient Client(string? role)
    {
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
        if (role != null) client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private static object SampleInitiateRequest() => new
    {
        clientId = "000000000000000000000000",
        packageId = "000000000000000000000000",
        extraDays = 0
    };

    [Fact]
    public async Task Initiate_Unauthenticated_IsRejected_401()
    {
        var res = await Client(null).PostAsJsonAsync("/api/admin/package-payments/initiate", SampleInitiateRequest());
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Initiate_NonAdminCaller_IsRejected_403()
    {
        var res = await Client("Caregiver").PostAsJsonAsync("/api/admin/package-payments/initiate", SampleInitiateRequest());
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Initiate_AdminInWrongDepartment_IsRejected_403()
    {
        var res = await Client("Admin:MarketingAndSales").PostAsJsonAsync("/api/admin/package-payments/initiate", SampleInitiateRequest());
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Initiate_OperationsAdmin_ReachesRealPipeline_PackageNotFound()
    {
        // Proves the full DI wiring (controller → IPackagePaymentService → PackageService/
        // Clients/PendingPackagePayments) is actually connected end to end through the real
        // ASP.NET Core pipeline: an allowed admin gets past the auth gate and reaches the
        // service's own "Package not found" guard clause, not a wiring/500 error.
        var res = await Client("Admin:HR").PostAsJsonAsync("/api/admin/package-payments/initiate", SampleInitiateRequest());
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("Package not found", body);
    }

    [Fact]
    public async Task Initiate_SuperAdmin_IsAllowedThroughAuthGate()
    {
        var res = await Client("SuperAdmin").PostAsJsonAsync("/api/admin/package-payments/initiate", SampleInitiateRequest());
        // Not 401/403 — reaches the controller action.
        Assert.NotEqual(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, res.StatusCode);
    }

    // ─────────────────── The removed self-service PackageRequest creation endpoint ───────────────────

    [Fact]
    public async Task ClientCreatePackageRequest_Endpoint_NoLongerExists_405()
    {
        // This used to be POST /api/client/package-requests. Removed as part of Option A —
        // the only way a PackageRequest gets created now is the admin payment-link +
        // webhook flow. The route itself still resolves (GetAll's [HttpGet] lives at the
        // same bare route), but with the [HttpPost] action gone, ASP.NET Core's real routing
        // correctly rejects POST as 405 Method Not Allowed — proving there is no longer any
        // action on this route that accepts a POST, not merely "nothing calls it anymore".
        var res = await Client("Client").PostAsJsonAsync("/api/client/package-requests", new { packageId = "000000000000000000000000" });
        Assert.Equal(HttpStatusCode.MethodNotAllowed, res.StatusCode);
    }

    [Fact]
    public async Task ClientListPackageRequests_StillWorks_AfterCreateRemoval()
    {
        // GET on the same route (list) must be untouched by removing the POST action.
        var res = await Client("Client").GetAsync("/api/client/package-requests");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }
}
