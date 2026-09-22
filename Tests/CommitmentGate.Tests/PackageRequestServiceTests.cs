using System;
using System.Linq;
using System.Threading.Tasks;
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
/// Phase 4 — client-facing "list my package requests" (<see cref="IPackageRequestService.GetAllForClientAsync"/>).
/// Service-layer coverage against a real local MongoDB (replica set on 127.0.0.1:27018, same
/// infra as the other suites), proving: newest-first ordering, the same per-request shape and
/// ConfirmedCaregiver visibility rule as the single-request lookup, and that a client's own id
/// is the only thing that scopes the query (no cross-client leakage).
/// </summary>
public class PackageRequestServiceTests
{
    private static CareProDbContext CreateDb(string databaseName)
    {
        var options = new DbContextOptionsBuilder<CareProDbContext>()
            .UseMongoDB("mongodb://127.0.0.1:27018", databaseName)
            .Options;
        return new TestCareProDbContext(options);
    }

    private static string NewDbName() => $"carepro_pkgreq_tests_{Guid.NewGuid():N}";

    private static PackageRequestService CreateService(CareProDbContext db)
        => new(db, Mock.Of<ILogger<PackageRequestService>>());

    private static PackageRequest NewRequest(
        string clientId, string status, DateTime createdAt,
        string category = "Adult/Elder Care", string tier = "Essential",
        string? confirmedCaregiverId = null, DateTime? confirmedAt = null) => new()
    {
        Id = ObjectId.GenerateNewId(),
        ClientId = clientId,
        PackageId = ObjectId.GenerateNewId().ToString(),
        PackageCategory = category,
        PackageTierLabel = tier,
        RequiredCaregiverType = CaregiverType.RegisteredNurse,
        ServiceCategory = category,
        Status = status,
        ConfirmedCaregiverId = confirmedCaregiverId,
        ConfirmedAt = confirmedAt,
        CreatedAt = createdAt,
    };

    [Fact]
    public async Task ReturnsOnlyCallersOwnRequests_NewestFirst_WithCorrectSummaryAndConfirmedCaregiverVisibility()
    {
        var dbName = NewDbName();
        var clientA = ObjectId.GenerateNewId().ToString();
        var clientB = ObjectId.GenerateNewId().ToString();
        var caregiverId = ObjectId.GenerateNewId();
        var now = DateTime.UtcNow;

        using (var db = CreateDb(dbName))
        {
            db.CareGivers.Add(new Caregiver
            {
                Id = caregiverId,
                FirstName = "Amina",
                LastName = "Yusuf",
                Email = "amina@example.com",
                Password = "irrelevant",
                Role = "Caregiver",
                CaregiverType = CaregiverType.RegisteredNurse,
                Specialty = "Geriatrics",
                CreatedAt = now,
            });

            // Client A: pending, assigned, confirmed — created oldest to newest.
            db.PackageRequests.AddRange(
                NewRequest(clientA, PackageRequestStatuses.Pending, now.AddDays(-3), tier: "Oldest"),
                NewRequest(clientA, PackageRequestStatuses.Assigned, now.AddDays(-2), tier: "Middle"),
                NewRequest(clientA, PackageRequestStatuses.Confirmed, now.AddDays(-1), tier: "Newest",
                    confirmedCaregiverId: caregiverId.ToString(), confirmedAt: now.AddHours(-1)));

            // Client B: one request — must never appear in client A's results.
            db.PackageRequests.Add(NewRequest(clientB, PackageRequestStatuses.Pending, now, tier: "NotYours"));

            await db.SaveChangesAsync();
        }

        using (var db = CreateDb(dbName))
        {
            var service = CreateService(db);
            var results = await service.GetAllForClientAsync(clientA);

            // Only client A's 3 requests — client B's is excluded.
            Assert.Equal(3, results.Count);
            Assert.All(results, r => Assert.Equal(clientA, r.ClientId));
            Assert.DoesNotContain(results, r => r.PackageTierLabel == "NotYours");

            // Newest first.
            Assert.Equal(new[] { "Newest", "Middle", "Oldest" }, results.Select(r => r.PackageTierLabel).ToArray());

            // Summary fields present per request.
            var confirmed = results.Single(r => r.Status == PackageRequestStatuses.Confirmed);
            var assigned = results.Single(r => r.Status == PackageRequestStatuses.Assigned);
            var pending = results.Single(r => r.Status == PackageRequestStatuses.Pending);
            Assert.Equal("Adult/Elder Care", confirmed.PackageCategory);
            Assert.NotEqual(default, confirmed.CreatedAt);

            // ConfirmedCaregiver populated ONLY on the confirmed request — same rule as GetForClientAsync.
            Assert.NotNull(confirmed.ConfirmedCaregiver);
            Assert.Equal("Amina Yusuf", confirmed.ConfirmedCaregiver!.Name);
            Assert.Equal(caregiverId.ToString(), confirmed.ConfirmedCaregiver.CaregiverId);
            Assert.Null(assigned.ConfirmedCaregiver);
            Assert.Null(pending.ConfirmedCaregiver);
        }
    }

    [Fact]
    public async Task ClientWithNoRequests_ReturnsEmptyList_NotAnError()
    {
        var dbName = NewDbName();
        using var db = CreateDb(dbName);
        var service = CreateService(db);

        var results = await service.GetAllForClientAsync(ObjectId.GenerateNewId().ToString());

        Assert.Empty(results);
    }

    [Fact]
    public async Task SoftDeletedRequests_AreExcluded()
    {
        var dbName = NewDbName();
        var clientId = ObjectId.GenerateNewId().ToString();
        var now = DateTime.UtcNow;

        using (var db = CreateDb(dbName))
        {
            var deleted = NewRequest(clientId, PackageRequestStatuses.Cancelled, now, tier: "Deleted");
            deleted.DeletedAt = now;
            db.PackageRequests.Add(deleted);
            db.PackageRequests.Add(NewRequest(clientId, PackageRequestStatuses.Pending, now, tier: "Live"));
            await db.SaveChangesAsync();
        }

        using (var db = CreateDb(dbName))
        {
            var service = CreateService(db);
            var results = await service.GetAllForClientAsync(clientId);

            var single = Assert.Single(results);
            Assert.Equal("Live", single.PackageTierLabel);
        }
    }
}
