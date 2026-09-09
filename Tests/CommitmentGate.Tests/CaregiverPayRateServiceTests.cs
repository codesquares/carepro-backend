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
/// Phase 9.3 — CaregiverPayRate entity + admin CRUD. Service-layer coverage against a
/// real local MongoDB (replica set on 127.0.0.1:27018, same infra as the other suites).
/// </summary>
public class CaregiverPayRateServiceTests
{
    private static CareProDbContext CreateDb(string databaseName)
    {
        var options = new DbContextOptionsBuilder<CareProDbContext>()
            .UseMongoDB("mongodb://127.0.0.1:27018", databaseName)
            .Options;
        return new TestCareProDbContext(options);
    }

    private static string NewDbName() => $"carepro_payrate_tests_{Guid.NewGuid():N}";

    private static CaregiverPayRateService CreateService(CareProDbContext db)
        => new(db, Mock.Of<ILogger<CaregiverPayRateService>>());

    private static AddCaregiverPayRateRequest Req(
        string type = "RegisteredNurse", string tier = "Senior", decimal rate = 2500, bool isActive = true) => new()
    {
        CaregiverType = type,
        ExperienceTier = tier,
        HourlyRate = rate,
        IsActive = isActive
    };

    [Fact]
    public async Task Create_Read_Update_Delete_RoundTrips()
    {
        var dbName = NewDbName();
        string id;

        using (var db = CreateDb(dbName))
        {
            var created = await CreateService(db).CreatePayRateAsync(Req("RegisteredNurse", "Senior", 2500));
            id = created.Id;
            Assert.Equal("RegisteredNurse", created.CaregiverType);
            Assert.Equal("Senior", created.ExperienceTier);
            Assert.Equal(2500m, created.HourlyRate);
            Assert.True(created.IsActive);
        }

        using (var db = CreateDb(dbName))
        {
            var fetched = await CreateService(db).GetPayRateByIdAsync(id);
            Assert.NotNull(fetched);
            Assert.Equal(2500m, fetched!.HourlyRate);
        }

        using (var db = CreateDb(dbName))
        {
            var ok = await CreateService(db).UpdatePayRateAsync(new UpdateCaregiverPayRateRequest
            {
                Id = id,
                HourlyRate = 2800
            });
            Assert.True(ok);
        }
        using (var db = CreateDb(dbName))
        {
            var fetched = await CreateService(db).GetPayRateByIdAsync(id);
            Assert.Equal(2800m, fetched!.HourlyRate);
            Assert.Equal("RegisteredNurse", fetched.CaregiverType); // untouched
        }

        using (var db = CreateDb(dbName))
        {
            Assert.True(await CreateService(db).DeletePayRateAsync(id));
        }
        using (var db = CreateDb(dbName))
        {
            Assert.Null(await CreateService(db).GetPayRateByIdAsync(id));
        }
    }

    [Fact]
    public async Task Update_UnknownId_ThrowsKeyNotFound()
    {
        using var db = CreateDb(NewDbName());
        await Assert.ThrowsAsync<KeyNotFoundException>(() => CreateService(db).UpdatePayRateAsync(
            new UpdateCaregiverPayRateRequest { Id = ObjectId.GenerateNewId().ToString(), HourlyRate = 1 }));
    }

    [Fact]
    public async Task Delete_UnknownId_ThrowsKeyNotFound()
    {
        using var db = CreateDb(NewDbName());
        await Assert.ThrowsAsync<KeyNotFoundException>(() => CreateService(db).DeletePayRateAsync(
            ObjectId.GenerateNewId().ToString()));
    }

    [Fact]
    public async Task Create_InvalidCaregiverType_Throws()
    {
        using var db = CreateDb(NewDbName());
        await Assert.ThrowsAsync<ArgumentException>(() => CreateService(db).CreatePayRateAsync(
            Req(type: "Doctor")));
    }

    [Fact]
    public async Task Create_InvalidExperienceTier_Throws()
    {
        using var db = CreateDb(NewDbName());
        await Assert.ThrowsAsync<ArgumentException>(() => CreateService(db).CreatePayRateAsync(
            Req(tier: "Expert")));
    }

    [Fact]
    public async Task Create_NegativeHourlyRate_Throws()
    {
        using var db = CreateDb(NewDbName());
        await Assert.ThrowsAsync<ArgumentException>(() => CreateService(db).CreatePayRateAsync(
            Req(rate: -100)));
    }

    [Fact]
    public async Task ToggleActiveStatus_Flips()
    {
        var dbName = NewDbName();
        var created = await CreateService(CreateDb(dbName)).CreatePayRateAsync(Req("CHEW", "Junior", 1200));

        await CreateService(CreateDb(dbName)).ToggleActiveStatusAsync(created.Id, false);
        Assert.False((await CreateService(CreateDb(dbName)).GetPayRateByIdAsync(created.Id))!.IsActive);

        await CreateService(CreateDb(dbName)).ToggleActiveStatusAsync(created.Id, true);
        Assert.True((await CreateService(CreateDb(dbName)).GetPayRateByIdAsync(created.Id))!.IsActive);
    }

    // ─────────── Uniqueness among active rates for the same (type, tier) pair ───────────

    [Fact]
    public async Task Create_DuplicateActivePair_Throws()
    {
        var dbName = NewDbName();
        await CreateService(CreateDb(dbName)).CreatePayRateAsync(Req("AuxiliaryNurse", "Mid", 1500));

        await Assert.ThrowsAsync<ArgumentException>(() => CreateService(CreateDb(dbName)).CreatePayRateAsync(
            Req("AuxiliaryNurse", "Mid", 1600)));
    }

    [Fact]
    public async Task Create_DuplicatePair_AllowedWhenExistingIsInactive()
    {
        var dbName = NewDbName();
        await CreateService(CreateDb(dbName)).CreatePayRateAsync(Req("AuxiliaryNurse", "Mid", 1500, isActive: false));

        var created = await CreateService(CreateDb(dbName)).CreatePayRateAsync(
            Req("AuxiliaryNurse", "Mid", 1600, isActive: true));
        Assert.Equal(1600m, created.HourlyRate);
    }

    [Fact]
    public async Task Update_ActivatingIntoExistingActivePair_Throws()
    {
        var dbName = NewDbName();
        await CreateService(CreateDb(dbName)).CreatePayRateAsync(Req("CHEW", "Senior", 2000));
        var second = await CreateService(CreateDb(dbName)).CreatePayRateAsync(
            Req("CHEW", "Senior", 2100, isActive: false));

        await Assert.ThrowsAsync<ArgumentException>(() => CreateService(CreateDb(dbName)).UpdatePayRateAsync(
            new UpdateCaregiverPayRateRequest { Id = second.Id, IsActive = true }));
    }

    [Fact]
    public async Task Update_ChangingPairIntoExistingActivePair_Throws()
    {
        var dbName = NewDbName();
        await CreateService(CreateDb(dbName)).CreatePayRateAsync(Req("RegisteredNurse", "Junior", 1800));
        var other = await CreateService(CreateDb(dbName)).CreatePayRateAsync(Req("RegisteredNurse", "Mid", 2000));

        await Assert.ThrowsAsync<ArgumentException>(() => CreateService(CreateDb(dbName)).UpdatePayRateAsync(
            new UpdateCaregiverPayRateRequest { Id = other.Id, ExperienceTier = "Junior" }));
    }

    [Fact]
    public async Task Update_SamePair_DoesNotConflictWithItself()
    {
        var dbName = NewDbName();
        var created = await CreateService(CreateDb(dbName)).CreatePayRateAsync(Req("RegisteredNurse", "Senior", 2500));

        var ok = await CreateService(CreateDb(dbName)).UpdatePayRateAsync(
            new UpdateCaregiverPayRateRequest { Id = created.Id, HourlyRate = 2600 });
        Assert.True(ok);
    }

    // ─────────── All 9 (type × tier) combinations are representable ───────────

    public static IEnumerable<object[]> AllCombinations()
    {
        foreach (var type in new[] { "AuxiliaryNurse", "CHEW", "RegisteredNurse" })
            foreach (var tier in new[] { "Junior", "Mid", "Senior" })
                yield return new object[] { type, tier };
    }

    [Theory]
    [MemberData(nameof(AllCombinations))]
    public async Task EveryTypeTierCombination_RoundTrips(string type, string tier)
    {
        var dbName = NewDbName();
        string id;
        using (var db = CreateDb(dbName))
        {
            var created = await CreateService(db).CreatePayRateAsync(Req(type, tier, 1900));
            id = created.Id;
        }
        using (var db = CreateDb(dbName))
        {
            var fetched = await CreateService(db).GetPayRateByIdAsync(id);
            Assert.NotNull(fetched);
            Assert.Equal(type, fetched!.CaregiverType);
            Assert.Equal(tier, fetched.ExperienceTier);

            var raw = await db.CaregiverPayRates.FirstAsync(r => r.Id == ObjectId.Parse(id));
            Assert.Equal(Enum.Parse<CaregiverType>(type), raw.CaregiverType);
            Assert.Equal(Enum.Parse<ExperienceTier>(tier), raw.ExperienceTier);
        }
    }
}
