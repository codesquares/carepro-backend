using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace CommitmentGate.Tests;

/// <summary>Shared helpers for the PoliceClearance suites (real local MongoDB, replica set on 127.0.0.1:27018).</summary>
internal static class PoliceClearanceTestSupport
{
    public static CareProDbContext CreateDb(string databaseName)
        => new TestCareProDbContext(new DbContextOptionsBuilder<CareProDbContext>()
            .UseMongoDB("mongodb://127.0.0.1:27018", databaseName).Options);

    public static string NewDbName() => $"carepro_clearance_{Guid.NewGuid():N}";

    public static Caregiver SeedCaregiver(CareProDbContext db, string first = "CLEARANCETEST", string last = "Subject")
    {
        var caregiver = new Caregiver
        {
            Id = ObjectId.GenerateNewId(), FirstName = first, LastName = last,
            Email = $"clearancetest-{Guid.NewGuid():N}@example.com", Password = "x", // pragma: allowlist-secret
            Role = "Caregiver", Status = true, IsAvailable = true, CreatedAt = DateTime.UtcNow
        };
        db.CareGivers.Add(caregiver);
        db.SaveChanges();
        return caregiver;
    }
}

/// <summary>Item 1: the PoliceClearance document — persistence, tolerance of missing fields, review race guard.</summary>
public class PoliceClearanceEntityTests
{
    [Fact]
    public async Task Clearance_RoundTrips_WithFilesAndChecklist()
    {
        var name = PoliceClearanceTestSupport.NewDbName();
        string id;
        using (var db = PoliceClearanceTestSupport.CreateDb(name))
        {
            var pc = new PoliceClearance
            {
                Id = ObjectId.GenerateNewId(), CaregiverId = "cg1", Status = PoliceClearanceStatuses.Pending,
                SubmittedAt = DateTime.UtcNow, IssueDate = new DateTime(2026, 9, 1), NameOnDocument = "A B", ReferenceNumber = "R1",
                Files = new() { new PoliceClearanceFile { PublicId = "p1", ResourceType = "raw", OriginalFileName = "a.pdf", ContentType = "application/pdf", SizeBytes = 10 } },
                Checklist = new PoliceClearanceChecklist { NameMatchesProfile = true }
            };
            db.PoliceClearances.Add(pc);
            await db.SaveChangesAsync();
            id = pc.Id.ToString();
        }
        using var fresh = PoliceClearanceTestSupport.CreateDb(name);
        var back = await fresh.PoliceClearances.FirstAsync(c => c.CaregiverId == "cg1");
        Assert.Equal(id, back.Id.ToString());
        Assert.Equal("p1", back.Files![0].PublicId);
        Assert.False(back.Checklist!.AllTrue());
    }

    [Fact]
    public async Task Clearance_StoredWithOnlyAFewFields_StillDeserializes()
    {
        // A document written without most fields (no schema is enforced) must not break reads.
        var name = PoliceClearanceTestSupport.NewDbName();
        var client = new MongoClient("mongodb://127.0.0.1:27018");
        var col = client.GetDatabase(name).GetCollection<BsonDocument>("PoliceClearances");
        await col.InsertOneAsync(new BsonDocument { { "CaregiverId", "sparse" } });

        using var db = PoliceClearanceTestSupport.CreateDb(name);
        var read = await db.PoliceClearances.FirstAsync(c => c.CaregiverId == "sparse");
        Assert.Null(read.Status);
        Assert.Null(read.Files);
        Assert.Null(read.Superseded);
        Assert.Null(read.Checklist);
    }

    [Fact]
    public async Task Clearance_TwoReviewersRacing_SecondSaveFails()
    {
        var name = PoliceClearanceTestSupport.NewDbName();
        ObjectId id = ObjectId.GenerateNewId();
        using (var seed = PoliceClearanceTestSupport.CreateDb(name))
        {
            seed.PoliceClearances.Add(new PoliceClearance { Id = id, CaregiverId = "cg", Status = PoliceClearanceStatuses.Pending, SubmittedAt = DateTime.UtcNow });
            await seed.SaveChangesAsync();
        }
        using var a = PoliceClearanceTestSupport.CreateDb(name);
        using var b = PoliceClearanceTestSupport.CreateDb(name);
        var ra = await a.PoliceClearances.FirstAsync(c => c.Id == id);
        var rb = await b.PoliceClearances.FirstAsync(c => c.Id == id);
        ra.Status = PoliceClearanceStatuses.Approved; ra.ReviewedAt = DateTime.UtcNow;
        rb.Status = PoliceClearanceStatuses.Rejected; rb.ReviewedAt = DateTime.UtcNow;
        await a.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => b.SaveChangesAsync());

        using var check = PoliceClearanceTestSupport.CreateDb(name);
        Assert.Equal(PoliceClearanceStatuses.Approved, (await check.PoliceClearances.FirstAsync(c => c.Id == id)).Status);
    }

    [Fact]
    public void Checklist_AllTrue_RequiresEveryItem()
    {
        var full = new PoliceClearanceChecklist { NameMatchesProfile = true, IssuedByNigeriaPoliceForce = true, IssueDateWithinAllowedAge = true, ReferenceNumberCaptured = true, ScanLegibleAndUnaltered = true };
        Assert.True(full.AllTrue());
        full.ScanLegibleAndUnaltered = null;
        Assert.False(full.AllTrue());
        full.ScanLegibleAndUnaltered = true; full.NameMatchesProfile = false;
        Assert.False(full.AllTrue());
    }
}
