using System;
using System.Collections.Generic;
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
/// Real end-to-end HTTP coverage for the client-facing package catalog
/// (<c>GET /api/client/packages</c>): the real auth pipeline decides 401 vs 200
/// (a header selects the principal), and the happy path runs the real controller +
/// PackageService + MongoDB. Reuses <see cref="HeaderRoleAuthHandler"/> and the
/// test-host wiring from <see cref="AdminPackagesHostFactory"/>.
/// </summary>
public class ClientPackagesHttpTests : IClassFixture<AdminPackagesHostFactory>
{
    private readonly AdminPackagesHostFactory _factory;

    public ClientPackagesHttpTests(AdminPackagesHostFactory factory) => _factory = factory;

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

    private async Task<(string activeId, string inactiveId)> SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CareProDbContext>();

        var now = DateTime.UtcNow;
        var active = new Package
        {
            Id = ObjectId.GenerateNewId(),
            Category = PackageCategories.PostPartumCare,
            TierLabel = "Premium",
            RequiredCaregiverType = CaregiverType.RegisteredNurse,
            RequiredSpecialty = "Midwifery",
            BasePrice = 350000m,
            AdditionalDayPrice = 20000m,
            PayCalculationType = Domain.Entities.PayCalculationType.Fixed,
            FixedCaregiverPay = 180000m,
            Description = "Daily post-partum recovery support and newborn care.",
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var cheaperActive = new Package
        {
            Id = ObjectId.GenerateNewId(),
            Category = PackageCategories.PostPartumCare,
            TierLabel = "Essential",
            RequiredCaregiverType = CaregiverType.AuxiliaryNurse,
            BasePrice = 150000m,
            PayCalculationType = Domain.Entities.PayCalculationType.Hourly,
            Description = "Core post-partum support.",
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var inactive = new Package
        {
            Id = ObjectId.GenerateNewId(),
            Category = PackageCategories.AdultElderCare,
            TierLabel = "Retired tier",
            RequiredCaregiverType = CaregiverType.CHEW,
            BasePrice = 99000m,
            PayCalculationType = Domain.Entities.PayCalculationType.Hourly,
            Description = "No longer offered.",
            IsActive = false,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Packages.AddRange(active, cheaperActive, inactive);
        await db.SaveChangesAsync();
        return (active.Id.ToString(), inactive.Id.ToString());
    }

    [Fact]
    public async Task Unauthenticated_IsRejected_401()
    {
        var res = await Client(null).GetAsync("/api/client/packages");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedClient_GetsActivePackages_InactiveExcluded_NoInternalsLeaked()
    {
        var (activeId, inactiveId) = await SeedAsync();

        var res = await Client("Client").GetAsync("/api/client/packages");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var raw = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("success").GetBoolean());
        var data = root.GetProperty("data").EnumerateArray().ToList();

        var ids = data.Select(e => e.GetProperty("id").GetString()).ToHashSet();
        Assert.Contains(activeId, ids);
        Assert.DoesNotContain(inactiveId, ids);            // inactive package excluded
        Assert.All(data, e => Assert.False(
            e.TryGetProperty("isActive", out _)));         // IsActive not projected

        // The real, current fields a client needs are present…
        var premium = data.First(e => e.GetProperty("id").GetString() == activeId);
        Assert.Equal("Post-Partum Care", premium.GetProperty("category").GetString());
        Assert.Equal("Premium", premium.GetProperty("tierLabel").GetString());
        Assert.Equal("RegisteredNurse", premium.GetProperty("requiredCaregiverType").GetString());
        Assert.Equal("Midwifery", premium.GetProperty("requiredSpecialty").GetString());
        Assert.Equal(350000m, premium.GetProperty("basePrice").GetDecimal());
        Assert.Equal(20000m, premium.GetProperty("additionalDayPrice").GetDecimal());

        // …and NOTHING payroll/operations-internal is leaked.
        foreach (var e in data)
        {
            Assert.False(e.TryGetProperty("payCalculationType", out _));
            Assert.False(e.TryGetProperty("fixedCaregiverPay", out _));
            Assert.False(e.TryGetProperty("createdAt", out _));
            Assert.False(e.TryGetProperty("updatedAt", out _));
        }

        // Ordered by category, then price ascending.
        var postPartum = data
            .Where(e => e.GetProperty("category").GetString() == "Post-Partum Care")
            .Select(e => e.GetProperty("basePrice").GetDecimal())
            .ToList();
        Assert.Equal(postPartum.OrderBy(x => x).ToList(), postPartum);
    }

    [Fact]
    public async Task AnyAuthenticatedUser_CanRead_NoSpecialRoleNeeded()
    {
        await SeedAsync();

        // A caregiver principal — not a client, not an admin — still gets 200.
        var res = await Client("Caregiver").GetAsync("/api/client/packages");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }
}
