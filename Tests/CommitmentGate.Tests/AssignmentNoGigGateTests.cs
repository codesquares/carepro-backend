using Application.DTOs;
using Application.Interfaces.Content;
using Domain.Entities;
using Infrastructure.Content.Data;
using Infrastructure.Content.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using MongoDB.Bson;
using Xunit;

namespace CommitmentGate.Tests;

/// <summary>
/// Package assignment must not require a gig. The REAL CaregiverReadinessService runs here (not a mock),
/// so these prove the gate itself: gig-less and "Published"-only caregivers can be assigned, every other
/// readiness criterion still blocks, and the other readiness callers still see no_active_gig.
/// Real local MongoDB (replica set on 127.0.0.1:27018).
/// </summary>
public class AssignmentNoGigGateTests
{
    private const string Category = "Post Surgery Care";

    private static CareProDbContext CreateDb() =>
        new TestCareProDbContext(new DbContextOptionsBuilder<CareProDbContext>()
            .UseMongoDB("mongodb://127.0.0.1:27018", $"carepro_nogig_assign_{Guid.NewGuid():N}").Options);

    private static CaregiverReadinessService Readiness(CareProDbContext db) =>
        new(db, new EligibilityService(db, Mock.Of<ILogger<EligibilityService>>()), Mock.Of<ILogger<CaregiverReadinessService>>());

    private static AssignmentService Assign(CareProDbContext db) =>
        new(db, Mock.Of<IMediator>(), Mock.Of<Application.Interfaces.Email.IEmailService>(), Readiness(db),
            Mock.Of<IPackageContractService>(), Mock.Of<IOpsAlertService>(), Mock.Of<ILogger<AssignmentService>>());

    /// <summary>A caregiver who satisfies every non-gig readiness criterion, unless overridden.</summary>
    private static Caregiver Caregiver(CareProDbContext db, bool identity = true, int guarantors = 2, bool addressHistory = true)
    {
        var cg = new Caregiver
        {
            Id = ObjectId.GenerateNewId(), FirstName = "Real", LastName = "Nurse", Email = $"n{Guid.NewGuid():N}@x.com",
            Password = "x", Role = "Caregiver", Status = true, IsAvailable = true, IsIdentityVerified = identity, // pragma: allowlist-secret
            CaregiverType = CaregiverType.RegisteredNurse, CreatedAt = DateTime.UtcNow
        };
        db.CareGivers.Add(cg);
        var id = cg.Id.ToString();
        for (int i = 0; i < guarantors; i++)
            db.Guarantors.Add(new Guarantor { Id = ObjectId.GenerateNewId(), CaregiverId = id, Name = $"G{i}", RelationshipToCaregiver = "Uncle", PhoneNo = "0800", Status = GuarantorStatuses.Confirmed });
        if (addressHistory)
        {
            var now = DateTime.UtcNow;
            db.CaregiverAddressHistories.Add(new CaregiverAddressHistory { Id = ObjectId.GenerateNewId(), CaregiverId = id, Address = "A", MovedIn = now.AddYears(-6), MovedOut = now.AddYears(-3), CreatedAt = now });
            db.CaregiverAddressHistories.Add(new CaregiverAddressHistory { Id = ObjectId.GenerateNewId(), CaregiverId = id, Address = "B", MovedIn = now.AddYears(-3), MovedOut = null, CreatedAt = now });
        }
        return cg;
    }

    private static Gig Gig(Caregiver cg, string status, string category) => new()
    {
        Id = ObjectId.GenerateNewId(), CaregiverId = cg.Id.ToString(), Title = "g", Category = category, SubCategory = "s",
        Status = status, Price = 1, CreatedAt = DateTime.UtcNow
    };

    private static async Task<string> Request(CareProDbContext db)
    {
        var client = new Client { Id = ObjectId.GenerateNewId(), FirstName = "C", LastName = "L", Email = $"c{Guid.NewGuid():N}@x.com", Password = "x", Role = "Client", Status = true }; // pragma: allowlist-secret
        db.Clients.Add(client);
        var pr = new PackageRequest
        {
            Id = ObjectId.GenerateNewId(), ClientId = client.Id.ToString(), PackageId = ObjectId.GenerateNewId().ToString(),
            PackageCategory = Category, PackageTierLabel = "Essential Recovery", RequiredCaregiverType = CaregiverType.RegisteredNurse,
            ServiceCategory = Category, Status = PackageRequestStatuses.Pending, BillingType = PackageRequestBillingTypes.OneTime, CreatedAt = DateTime.UtcNow
        };
        db.PackageRequests.Add(pr);
        await db.SaveChangesAsync();
        return pr.Id.ToString();
    }

    [Fact]
    public async Task CaregiverWithZeroGigs_CanBeAssigned()
    {
        using var db = CreateDb();
        var cg = Caregiver(db); var pr = await Request(db); await db.SaveChangesAsync();
        var a = await Assign(db).AssignAsync(pr, cg.Id.ToString(), "admin-1", "ops@x", "staff", null);
        Assert.Equal(AssignmentStatuses.PendingAcceptance, a.Status);
    }

    [Fact]
    public async Task CaregiverWithOnlyAPublishedGigInTheOldCategoryVocabulary_CanBeAssigned()
    {
        using var db = CreateDb();
        var cg = Caregiver(db);
        db.Gigs.Add(Gig(cg, "Published", "Adult Care")); // what a real caregiver-published gig looks like
        var pr = await Request(db); await db.SaveChangesAsync();
        var a = await Assign(db).AssignAsync(pr, cg.Id.ToString(), "admin-1", "ops@x", "staff", null);
        Assert.Equal(AssignmentStatuses.PendingAcceptance, a.Status);
    }

    [Fact]
    public async Task OtherReadinessCriteria_StillBlock_AndNoActiveGigIsNeverTheReason()
    {
        using var db = CreateDb();
        var unverified = Caregiver(db, identity: false);
        var noGuarantors = Caregiver(db, guarantors: 0);
        var noAddress = Caregiver(db, addressHistory: false);
        var pr = await Request(db); await db.SaveChangesAsync();

        var e1 = await Assert.ThrowsAsync<CaregiverNotReadyException>(() => Assign(db).AssignAsync(pr, unverified.Id.ToString(), "a", "e", "staff", null));
        Assert.Contains(CaregiverReadinessReasons.NotIdentityVerified, e1.Reasons);
        var e2 = await Assert.ThrowsAsync<CaregiverNotReadyException>(() => Assign(db).AssignAsync(pr, noGuarantors.Id.ToString(), "a", "e", "staff", null));
        Assert.Contains(CaregiverReadinessReasons.GuarantorsIncomplete, e2.Reasons);
        var e3 = await Assert.ThrowsAsync<CaregiverNotReadyException>(() => Assign(db).AssignAsync(pr, noAddress.Id.ToString(), "a", "e", "staff", null));
        Assert.Contains(CaregiverReadinessReasons.AddressHistoryIncomplete, e3.Reasons);
        foreach (var e in new[] { e1, e2, e3 }) Assert.DoesNotContain(CaregiverReadinessReasons.NoActiveGig, e.Reasons);
    }

    [Fact]
    public async Task OtherReadinessCallers_AreUnchanged_StillRequireAnActiveGig()
    {
        using var db = CreateDb();
        var noGigs = Caregiver(db);
        var publishedOnly = Caregiver(db);
        db.Gigs.Add(Gig(publishedOnly, "Published", Category));
        var activeMatching = Caregiver(db);
        db.Gigs.Add(Gig(activeMatching, "Active", Category));
        await db.SaveChangesAsync();
        var svc = Readiness(db);

        // GetReadinessAsync with no override (Brevo sync, vetting endpoint, post-payment check): unchanged
        Assert.Contains(CaregiverReadinessReasons.NoActiveGig, (await svc.GetReadinessAsync(noGigs.Id.ToString(), Category)).IneligibilityReasons);
        Assert.Contains(CaregiverReadinessReasons.NoActiveGig, (await svc.GetReadinessAsync(publishedOnly.Id.ToString(), Category)).IneligibilityReasons);
        Assert.True((await svc.GetReadinessAsync(activeMatching.Id.ToString(), Category)).IsReady);

        // Bulk (matching service): unchanged
        var bulk = await svc.GetReadinessBulkAsync(new[] { noGigs.Id.ToString(), publishedOnly.Id.ToString(), activeMatching.Id.ToString() }, Category);
        Assert.False(bulk[noGigs.Id.ToString()].HasActiveGig);
        Assert.False(bulk[publishedOnly.Id.ToString()].HasActiveGig);
        Assert.True(bulk[activeMatching.Id.ToString()].HasActiveGig);
    }

    [Fact]
    public async Task AssignAsync_DoesNotTouchGigs()
    {
        using var db = CreateDb();
        var cg = Caregiver(db); var pr = await Request(db); await db.SaveChangesAsync();
        await Assign(db).AssignAsync(pr, cg.Id.ToString(), "admin-1", "ops@x", "staff", null);
        Assert.Equal(0, await db.Gigs.CountAsync()); // nothing created/required/modified along the way
    }
}
