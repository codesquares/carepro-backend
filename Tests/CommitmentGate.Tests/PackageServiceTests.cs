using Application.DTOs;
using Domain.Entities;
using Infrastructure.Content.Data;
using Infrastructure.Content.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using MongoDB.Bson;
using Xunit;

namespace CommitmentGate.Tests;

/// <summary>
/// Phase 3 — Package entity + admin CRUD. Service-layer coverage against a real
/// local MongoDB (replica set on 127.0.0.1:27018, same infra as the other suites).
/// HTTP-level auth gating is covered separately in AdminPackagesHttpAuthTests.
/// </summary>
public class PackageServiceTests
{
    private static CareProDbContext CreateDb(string databaseName)
    {
        var options = new DbContextOptionsBuilder<CareProDbContext>()
            .UseMongoDB("mongodb://127.0.0.1:27018", databaseName)
            .Options;
        return new TestCareProDbContext(options);
    }

    private static string NewDbName() => $"carepro_package_tests_{Guid.NewGuid():N}";

    private static PackageService CreateService(CareProDbContext db)
        => new(db, Mock.Of<ILogger<PackageService>>());

    private static AddPackageRequest Req(
        string category, string tier, string type, string? specialty = null,
        decimal basePrice = 100000, decimal? additionalDay = null,
        string payType = "Hourly", decimal? fixedPay = null) => new()
    {
        Category = category,
        TierLabel = tier,
        RequiredCaregiverType = type,
        RequiredSpecialty = specialty,
        BasePrice = basePrice,
        AdditionalDayPrice = additionalDay,
        PayCalculationType = payType,
        FixedCaregiverPay = fixedPay,
        Description = $"{category} - {tier}",
        IsActive = true
    };

    // ─────────────────────────── CRUD ───────────────────────────

    [Fact]
    public async Task Create_Read_Update_Delete_RoundTrips()
    {
        var dbName = NewDbName();
        string id;

        // CREATE
        using (var db = CreateDb(dbName))
        {
            var created = await CreateService(db).CreatePackageAsync(
                Req(PackageCategories.AdultElderCare, "Essential", "AuxiliaryNurse", basePrice: 150000, additionalDay: 12000));
            id = created.Id;
            Assert.Equal("Adult/Elder Care", created.Category);
            Assert.Equal("Essential", created.TierLabel);
            Assert.Equal("AuxiliaryNurse", created.RequiredCaregiverType);
            Assert.Null(created.RequiredSpecialty);
            Assert.Equal(150000m, created.BasePrice);
            Assert.Equal(12000m, created.AdditionalDayPrice);
            Assert.True(created.IsActive);
        }

        // READ (fresh context)
        using (var db = CreateDb(dbName))
        {
            var fetched = await CreateService(db).GetPackageByIdAsync(id);
            Assert.NotNull(fetched);
            Assert.Equal("Essential", fetched!.TierLabel);
            Assert.Equal(150000m, fetched.BasePrice);
        }

        // UPDATE (partial — price + deactivate only)
        using (var db = CreateDb(dbName))
        {
            var ok = await CreateService(db).UpdatePackageAsync(new UpdatePackageRequest
            {
                Id = id,
                BasePrice = 175000,
                IsActive = false
            });
            Assert.True(ok);
        }
        using (var db = CreateDb(dbName))
        {
            var fetched = await CreateService(db).GetPackageByIdAsync(id);
            Assert.Equal(175000m, fetched!.BasePrice);
            Assert.False(fetched.IsActive);
            Assert.Equal("Essential", fetched.TierLabel); // untouched fields preserved
            Assert.Equal("AuxiliaryNurse", fetched.RequiredCaregiverType);
        }

        // DELETE
        using (var db = CreateDb(dbName))
        {
            Assert.True(await CreateService(db).DeletePackageAsync(id));
        }
        using (var db = CreateDb(dbName))
        {
            Assert.Null(await CreateService(db).GetPackageByIdAsync(id));
        }
    }

    [Fact]
    public async Task Update_UnknownId_ThrowsKeyNotFound()
    {
        using var db = CreateDb(NewDbName());
        await Assert.ThrowsAsync<KeyNotFoundException>(() => CreateService(db).UpdatePackageAsync(
            new UpdatePackageRequest { Id = ObjectId.GenerateNewId().ToString(), BasePrice = 1 }));
    }

    [Fact]
    public async Task Delete_UnknownId_ThrowsKeyNotFound()
    {
        using var db = CreateDb(NewDbName());
        await Assert.ThrowsAsync<KeyNotFoundException>(() => CreateService(db).DeletePackageAsync(
            ObjectId.GenerateNewId().ToString()));
    }

    [Fact]
    public async Task Create_InvalidCategory_Throws()
    {
        using var db = CreateDb(NewDbName());
        await Assert.ThrowsAsync<ArgumentException>(() => CreateService(db).CreatePackageAsync(
            Req("Pet Care", "Essential", "AuxiliaryNurse")));
    }

    [Fact]
    public async Task Create_InvalidCaregiverType_Throws()
    {
        using var db = CreateDb(NewDbName());
        await Assert.ThrowsAsync<ArgumentException>(() => CreateService(db).CreatePackageAsync(
            Req(PackageCategories.AdultElderCare, "Essential", "Doctor")));
    }

    [Fact]
    public async Task Update_BlankSpecialty_ClearsIt()
    {
        var dbName = NewDbName();
        var created = await CreateService(CreateDb(dbName)).CreatePackageAsync(
            Req(PackageCategories.PostPartumCare, "Premium", "RegisteredNurse", specialty: "Midwifery"));

        await CreateService(CreateDb(dbName)).UpdatePackageAsync(new UpdatePackageRequest
        {
            Id = created.Id,
            RequiredSpecialty = "   "
        });

        var fetched = await CreateService(CreateDb(dbName)).GetPackageByIdAsync(created.Id);
        Assert.Null(fetched!.RequiredSpecialty);
    }

    [Fact]
    public async Task ToggleActiveStatus_Flips()
    {
        var dbName = NewDbName();
        var created = await CreateService(CreateDb(dbName)).CreatePackageAsync(
            Req(PackageCategories.LiveInPackage, "Standard", "CHEW"));

        await CreateService(CreateDb(dbName)).ToggleActiveStatusAsync(created.Id, false);
        Assert.False((await CreateService(CreateDb(dbName)).GetPackageByIdAsync(created.Id))!.IsActive);

        await CreateService(CreateDb(dbName)).ToggleActiveStatusAsync(created.Id, true);
        Assert.True((await CreateService(CreateDb(dbName)).GetPackageByIdAsync(created.Id))!.IsActive);
    }

    [Fact]
    public async Task GetActivePackagesForClient_ReturnsOnlyActive_ProjectsNoInternals_OrdersByCategoryThenPrice()
    {
        var dbName = NewDbName();

        using (var db = CreateDb(dbName))
        {
            var svc = CreateService(db);
            await svc.CreatePackageAsync(Req(PackageCategories.PostPartumCare, "Premium", "RegisteredNurse", "Midwifery", basePrice: 350000));
            await svc.CreatePackageAsync(Req(PackageCategories.PostPartumCare, "Essential", "AuxiliaryNurse", basePrice: 150000));
            await svc.CreatePackageAsync(Req(PackageCategories.AdultElderCare, "Standard", "CHEW", basePrice: 120000));
            var retired = await svc.CreatePackageAsync(Req(PackageCategories.AdultElderCare, "Retired", "CHEW", basePrice: 90000));
            await svc.ToggleActiveStatusAsync(retired.Id, false);
        }

        using (var db = CreateDb(dbName))
        {
            var result = await CreateService(db).GetActivePackagesForClientAsync();

            // Inactive package excluded.
            Assert.Equal(3, result.Count);
            Assert.DoesNotContain(result, p => p.TierLabel == "Retired");

            // Ordered: category asc, then price asc.
            Assert.Equal(
                new[] { "Adult/Elder Care", "Post-Partum Care", "Post-Partum Care" },
                result.Select(p => p.Category).ToArray());
            var postPartumPrices = result.Where(p => p.Category == "Post-Partum Care").Select(p => p.BasePrice).ToList();
            Assert.Equal(postPartumPrices.OrderBy(x => x).ToList(), postPartumPrices);

            // Client projection carries the browse fields…
            var premium = result.Single(p => p.TierLabel == "Premium");
            Assert.Equal("RegisteredNurse", premium.RequiredCaregiverType);
            Assert.Equal("Midwifery", premium.RequiredSpecialty);
            Assert.Equal(350000m, premium.BasePrice);

            // …and the type is ClientPackageDTO (compile-time proof there is no
            // PayCalculationType / FixedCaregiverPay / IsActive to leak).
            Assert.IsType<ClientPackageDTO>(premium);
        }
    }

    // ─────────── All ~11 real package variants are representable ───────────

    // PayCalculationType per Phase 9.2's confirmed business data: all 3 Live-in
    // tiers are Fixed, the other 8 variants are Hourly.
    public static IEnumerable<object?[]> AllElevenVariants() => new List<object?[]>
    {
        // Adult/Elder Care
        new object?[] { "Adult/Elder Care", "Essential", "AuxiliaryNurse",  null, "Hourly", null },
        new object?[] { "Adult/Elder Care", "Standard",  "CHEW",            null, "Hourly", null },
        new object?[] { "Adult/Elder Care", "Premium",   "RegisteredNurse", null, "Hourly", null },
        // Post-Partum Care — only two tiers, both Registered Nurse + Midwifery
        new object?[] { "Post-Partum Care", "Essential", "RegisteredNurse", "Midwifery", "Hourly", null },
        new object?[] { "Post-Partum Care", "Premium",   "RegisteredNurse", "Midwifery", "Hourly", null },
        // Post Surgery Care — all three tiers Registered Nurse (breaks a tier→type formula)
        new object?[] { "Post Surgery Care", "Essential", "RegisteredNurse", null, "Hourly", null },
        new object?[] { "Post Surgery Care", "Standard",  "RegisteredNurse", null, "Hourly", null },
        new object?[] { "Post Surgery Care", "Premium",   "RegisteredNurse", null, "Hourly", null },
        // Live-in Package — Fixed pay across all three tiers
        new object?[] { "Live-in Package", "Essential", "AuxiliaryNurse",  null, "Fixed", 200000m },
        new object?[] { "Live-in Package", "Standard",  "CHEW",            null, "Fixed", 280000m },
        new object?[] { "Live-in Package", "Premium",   "RegisteredNurse", null, "Fixed", 400000m },
    };

    [Theory]
    [MemberData(nameof(AllElevenVariants))]
    public async Task EveryRealVariant_RoundTripsWithCorrectTypeSpecialtyAndPayCalculation(
        string category, string tier, string type, string? specialty, string payType, decimal? fixedPay)
    {
        var dbName = NewDbName();
        string id;
        using (var db = CreateDb(dbName))
        {
            var created = await CreateService(db).CreatePackageAsync(Req(category, tier, type, specialty, payType: payType, fixedPay: fixedPay));
            id = created.Id;
        }
        using (var db = CreateDb(dbName))
        {
            var fetched = await CreateService(db).GetPackageByIdAsync(id);
            Assert.NotNull(fetched);
            Assert.Equal(category, fetched!.Category);
            Assert.Equal(tier, fetched.TierLabel);
            Assert.Equal(type, fetched.RequiredCaregiverType);
            Assert.Equal(specialty, fetched.RequiredSpecialty);
            Assert.Equal(payType, fetched.PayCalculationType);
            Assert.Equal(fixedPay, fetched.FixedCaregiverPay);

            var raw = await db.Packages.FirstAsync(p => p.Id == ObjectId.Parse(id));
            Assert.Equal(Enum.Parse<CaregiverType>(type), raw.RequiredCaregiverType);
            Assert.Equal(Enum.Parse<PayCalculationType>(payType), raw.PayCalculationType);
        }
    }

    [Fact]
    public async Task PostSurgeryCare_ThreeDistinctTiers_AllRegisteredNurse_CoexistInList()
    {
        var dbName = NewDbName();
        using (var db = CreateDb(dbName))
        {
            var svc = CreateService(db);
            await svc.CreatePackageAsync(Req("Post Surgery Care", "Essential", "RegisteredNurse"));
            await svc.CreatePackageAsync(Req("Post Surgery Care", "Standard", "RegisteredNurse"));
            await svc.CreatePackageAsync(Req("Post Surgery Care", "Premium", "RegisteredNurse"));
        }
        using (var db = CreateDb(dbName))
        {
            var all = await CreateService(db).GetAllPackagesAsync();
            var postSurgery = all.Where(p => p.Category == "Post Surgery Care").ToList();
            Assert.Equal(3, postSurgery.Count);
            Assert.All(postSurgery, p => Assert.Equal("RegisteredNurse", p.RequiredCaregiverType));
            Assert.Equal(new[] { "Essential", "Premium", "Standard" },
                postSurgery.Select(p => p.TierLabel).OrderBy(x => x).ToArray());
        }
    }

    // ─────────── 9.2 PayCalculationType / FixedCaregiverPay invariants ───────────

    [Fact]
    public async Task Create_InvalidPayCalculationType_Throws()
    {
        using var db = CreateDb(NewDbName());
        await Assert.ThrowsAsync<ArgumentException>(() => CreateService(db).CreatePackageAsync(
            Req(PackageCategories.LiveInPackage, "Essential", "AuxiliaryNurse", payType: "Salaried")));
    }

    [Fact]
    public async Task Create_Fixed_WithoutFixedCaregiverPay_Throws()
    {
        using var db = CreateDb(NewDbName());
        await Assert.ThrowsAsync<ArgumentException>(() => CreateService(db).CreatePackageAsync(
            Req(PackageCategories.LiveInPackage, "Essential", "AuxiliaryNurse", payType: "Fixed", fixedPay: null)));
    }

    [Fact]
    public async Task Create_Fixed_WithZeroFixedCaregiverPay_Throws()
    {
        using var db = CreateDb(NewDbName());
        await Assert.ThrowsAsync<ArgumentException>(() => CreateService(db).CreatePackageAsync(
            Req(PackageCategories.LiveInPackage, "Essential", "AuxiliaryNurse", payType: "Fixed", fixedPay: 0)));
    }

    [Fact]
    public async Task Create_Hourly_WithFixedCaregiverPay_Throws()
    {
        using var db = CreateDb(NewDbName());
        await Assert.ThrowsAsync<ArgumentException>(() => CreateService(db).CreatePackageAsync(
            Req(PackageCategories.AdultElderCare, "Essential", "AuxiliaryNurse", payType: "Hourly", fixedPay: 100000)));
    }

    [Fact]
    public async Task Update_SwitchHourlyToFixed_RequiresFixedCaregiverPay()
    {
        var dbName = NewDbName();
        var created = await CreateService(CreateDb(dbName)).CreatePackageAsync(
            Req(PackageCategories.AdultElderCare, "Essential", "AuxiliaryNurse", payType: "Hourly"));

        await Assert.ThrowsAsync<ArgumentException>(() => CreateService(CreateDb(dbName)).UpdatePackageAsync(
            new UpdatePackageRequest { Id = created.Id, PayCalculationType = "Fixed" }));

        // Supplying the amount alongside the switch succeeds.
        var ok = await CreateService(CreateDb(dbName)).UpdatePackageAsync(
            new UpdatePackageRequest { Id = created.Id, PayCalculationType = "Fixed", FixedCaregiverPay = 250000 });
        Assert.True(ok);

        var fetched = await CreateService(CreateDb(dbName)).GetPackageByIdAsync(created.Id);
        Assert.Equal("Fixed", fetched!.PayCalculationType);
        Assert.Equal(250000m, fetched.FixedCaregiverPay);
    }

    [Fact]
    public async Task Update_SwitchFixedToHourly_ClearsFixedCaregiverPayAutomatically()
    {
        var dbName = NewDbName();
        var created = await CreateService(CreateDb(dbName)).CreatePackageAsync(
            Req(PackageCategories.LiveInPackage, "Essential", "AuxiliaryNurse", payType: "Fixed", fixedPay: 200000));

        // Caller doesn't even need to null it out explicitly — switching to Hourly clears it.
        await CreateService(CreateDb(dbName)).UpdatePackageAsync(
            new UpdatePackageRequest { Id = created.Id, PayCalculationType = "Hourly" });

        var fetched = await CreateService(CreateDb(dbName)).GetPackageByIdAsync(created.Id);
        Assert.Equal("Hourly", fetched!.PayCalculationType);
        Assert.Null(fetched.FixedCaregiverPay);
    }

    [Fact]
    public async Task Update_FixedCaregiverPayOnly_LeavesExistingHourlyTypeButRejectsIt()
    {
        var dbName = NewDbName();
        var created = await CreateService(CreateDb(dbName)).CreatePackageAsync(
            Req(PackageCategories.AdultElderCare, "Essential", "AuxiliaryNurse", payType: "Hourly"));

        // Package stays Hourly; supplying a FixedCaregiverPay without switching type violates the invariant.
        await Assert.ThrowsAsync<ArgumentException>(() => CreateService(CreateDb(dbName)).UpdatePackageAsync(
            new UpdatePackageRequest { Id = created.Id, FixedCaregiverPay = 100000 }));
    }

    [Fact]
    public async Task LegacyPackage_PredatingPhase9_HasNullPayCalculationType()
    {
        // Simulates a real package document created before Phase 9.2 shipped — proves
        // the nullable field doesn't break deserialization of pre-existing records.
        var dbName = NewDbName();
        using (var db = CreateDb(dbName))
        {
            db.Packages.Add(new Package
            {
                Id = ObjectId.GenerateNewId(),
                Category = PackageCategories.PostSurgeryCare,
                TierLabel = "Essential",
                RequiredCaregiverType = CaregiverType.RegisteredNurse,
                BasePrice = 100000,
                Description = "legacy",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
                // PayCalculationType / FixedCaregiverPay intentionally left unset.
            });
            await db.SaveChangesAsync();
        }
        using (var db = CreateDb(dbName))
        {
            var all = await CreateService(db).GetAllPackagesAsync();
            var legacy = Assert.Single(all);
            Assert.Null(legacy.PayCalculationType);
            Assert.Null(legacy.FixedCaregiverPay);
        }
    }
}
