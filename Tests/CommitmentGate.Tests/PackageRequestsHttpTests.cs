using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using Xunit;

namespace CommitmentGate.Tests;

/// <summary>
/// Real end-to-end HTTP coverage for <c>GET /api/client/package-requests</c> (list). Reuses
/// <see cref="HeaderRoleAuthHandler"/> and the test-host wiring from <see cref="AdminPackagesHostFactory"/>
/// — the same infra as <see cref="ClientPackagesHttpTests"/>.
///
/// The test principal's id is fixed ("test-admin-id" — see <see cref="HeaderRoleAuthHandler"/>), so
/// cross-client isolation itself (does the query ever leak another client's rows) is proven at the
/// service layer in <see cref="PackageRequestServiceTests"/> with two real, distinct client ids. What
/// this suite proves is the thing that's specific to the HTTP layer: the route has no id/clientId
/// parameter for a caller to substitute (<c>[HttpGet]</c> on the bare <c>api/client/package-requests</c>
/// route, id always resolved from the JWT), and the role gate + response shape are correct end to end.
/// </summary>
public class PackageRequestsHttpTests : IClassFixture<AdminPackagesHostFactory>
{
    private readonly AdminPackagesHostFactory _factory;
    private const string TestPrincipalClientId = "test-admin-id"; // matches HeaderRoleAuthHandler's NameIdentifier claim

    public PackageRequestsHttpTests(AdminPackagesHostFactory factory) => _factory = factory;

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

    [Fact]
    public async Task Unauthenticated_IsRejected_401()
    {
        var res = await Client(null).GetAsync("/api/client/package-requests");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task NonClientCaller_IsRejected_403()
    {
        var res = await Client("Caregiver").GetAsync("/api/client/package-requests");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Client_GetsOwnRequests_NewestFirst_WithConfirmedCaregiverOnlyOnConfirmed()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CareProDbContext>();

        var caregiverId = ObjectId.GenerateNewId();
        var now = DateTime.UtcNow;
        db.CareGivers.Add(new Caregiver
        {
            Id = caregiverId,
            FirstName = "Tolu",
            LastName = "Bello",
            Email = "tolu@example.com",
            Password = "irrelevant",
            Role = "Caregiver",
            CaregiverType = CaregiverType.AuxiliaryNurse,
            CreatedAt = now,
        });

        db.PackageRequests.AddRange(
            new PackageRequest
            {
                Id = ObjectId.GenerateNewId(),
                ClientId = TestPrincipalClientId,
                PackageId = ObjectId.GenerateNewId().ToString(),
                PackageCategory = "Post-Partum Care",
                PackageTierLabel = "Older",
                RequiredCaregiverType = CaregiverType.AuxiliaryNurse,
                ServiceCategory = "Post-Partum Care",
                Status = PackageRequestStatuses.Pending,
                CreatedAt = now.AddDays(-2),
            },
            new PackageRequest
            {
                Id = ObjectId.GenerateNewId(),
                ClientId = TestPrincipalClientId,
                PackageId = ObjectId.GenerateNewId().ToString(),
                PackageCategory = "Post-Partum Care",
                PackageTierLabel = "Confirmed",
                RequiredCaregiverType = CaregiverType.AuxiliaryNurse,
                ServiceCategory = "Post-Partum Care",
                Status = PackageRequestStatuses.Confirmed,
                ConfirmedCaregiverId = caregiverId.ToString(),
                ConfirmedAt = now.AddHours(-1),
                CreatedAt = now.AddDays(-1),
            },
            // Belongs to a different client — must never come back for the test principal.
            new PackageRequest
            {
                Id = ObjectId.GenerateNewId(),
                ClientId = ObjectId.GenerateNewId().ToString(),
                PackageId = ObjectId.GenerateNewId().ToString(),
                PackageCategory = "Live-in Package",
                PackageTierLabel = "SomeoneElses",
                RequiredCaregiverType = CaregiverType.RegisteredNurse,
                ServiceCategory = "Live-in Package",
                Status = PackageRequestStatuses.Pending,
                CreatedAt = now,
            });
        await db.SaveChangesAsync();

        var res = await Client("Client").GetAsync("/api/client/package-requests");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var raw = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(raw);
        var data = doc.RootElement.GetProperty("data").EnumerateArray().ToList();

        Assert.Equal(2, data.Count); // only the test principal's own two requests
        Assert.DoesNotContain(data, e => e.GetProperty("packageTierLabel").GetString() == "SomeoneElses");

        // Newest first.
        Assert.Equal("Confirmed", data[0].GetProperty("packageTierLabel").GetString());
        Assert.Equal("Older", data[1].GetProperty("packageTierLabel").GetString());

        // ConfirmedCaregiver populated only on the confirmed one.
        Assert.True(data[0].GetProperty("confirmedCaregiver").ValueKind == JsonValueKind.Object);
        Assert.Equal("Tolu Bello", data[0].GetProperty("confirmedCaregiver").GetProperty("name").GetString());
        Assert.True(data[1].GetProperty("confirmedCaregiver").ValueKind == JsonValueKind.Null);
    }
}
