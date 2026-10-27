using System.Text.Json;
using Application.DTOs;
using CarePro_Api.Controllers.Content;
using Domain.Entities;
using Infrastructure.Content.Data;
using Infrastructure.Content.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using MongoDB.Bson;
using Xunit;

namespace CommitmentGate.Tests;

/// <summary>
/// The anonymous package summary must never carry a price or payroll field, and the existing
/// authenticated client catalog must be untouched. Real local MongoDB (replica set on 127.0.0.1:27018).
/// </summary>
public class PublicPackageSummaryTests
{
    private static CareProDbContext CreateDb() =>
        new TestCareProDbContext(new DbContextOptionsBuilder<CareProDbContext>()
            .UseMongoDB("mongodb://127.0.0.1:27018", $"carepro_pubpkg_{Guid.NewGuid():N}").Options);

    private static Package Pkg(string category, string tier, decimal price, bool active = true) => new()
    {
        Id = ObjectId.GenerateNewId(), Category = category, TierLabel = tier,
        RequiredCaregiverType = CaregiverType.RegisteredNurse, BasePrice = price, AdditionalDayPrice = 12345m,
        PayCalculationType = PayCalculationType.Fixed, FixedCaregiverPay = 77777m,
        Description = $"{tier} description", IsActive = active, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task PublicSummary_ReturnsOnlyActivePackages_WithNoPriceOrPayrollFieldsAnywhere()
    {
        using var db = CreateDb();
        db.Packages.AddRange(
            Pkg("Post Surgery Care", "Essential Recovery", 80000m),
            Pkg("Post Surgery Care", "Hidden Draft", 99999m, active: false),
            Pkg("Adult/Elder Care", "Essential Companion", 50000m));
        await db.SaveChangesAsync();
        var svc = new PackageService(db, Mock.Of<ILogger<PackageService>>());

        var result = await svc.GetActivePackagesPublicAsync();

        Assert.Equal(new[] { "Essential Companion", "Essential Recovery" }, result.Select(r => r.TierLabel).ToArray());
        Assert.DoesNotContain(result, r => r.TierLabel == "Hidden Draft");
        Assert.Equal("RegisteredNurse", result[0].RequiredCaregiverType);

        // What actually goes over the wire: exactly four properties, and none of the seeded money values.
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var doc = JsonDocument.Parse(json);
        var props = doc.RootElement[0].EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "category", "description", "requiredCaregiverType", "tierLabel" }, props);
        foreach (var leaked in new[] { "80000", "50000", "12345", "77777", "basePrice", "fixedCaregiverPay", "additionalDayPrice" })
            Assert.DoesNotContain(leaked, json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PublicController_IsAnonymous_AndClientController_RemainsAuthorized()
    {
        Assert.NotNull(Attribute.GetCustomAttribute(typeof(PublicPackagesController), typeof(AllowAnonymousAttribute)));
        Assert.NotNull(Attribute.GetCustomAttribute(typeof(ClientPackagesController), typeof(AuthorizeAttribute)));
        Assert.Null(Attribute.GetCustomAttribute(typeof(ClientPackagesController), typeof(AllowAnonymousAttribute)));
    }

    [Fact]
    public async Task ClientCatalog_IsUnchanged_StillCarriesPrices()
    {
        using var db = CreateDb();
        db.Packages.Add(Pkg("Post Surgery Care", "Essential Recovery", 80000m));
        await db.SaveChangesAsync();
        var svc = new PackageService(db, Mock.Of<ILogger<PackageService>>());

        var client = Assert.Single(await svc.GetActivePackagesForClientAsync());

        Assert.Equal(80000m, client.BasePrice);
        Assert.Equal(12345m, client.AdditionalDayPrice);
        var names = typeof(ClientPackageDTO).GetProperties().Select(p => p.Name).ToHashSet();
        Assert.DoesNotContain("FixedCaregiverPay", names);
        Assert.Equal(4, typeof(PublicPackageSummaryDTO).GetProperties().Length);
    }
}
