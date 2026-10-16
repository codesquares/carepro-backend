using System.Reflection;
using Application.Interfaces.Content;
using Application.Interfaces.Email;
using Domain.Entities;
using Infrastructure.Content.Data;
using Infrastructure.Content.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using MongoDB.Bson;
using Xunit;

namespace CommitmentGate.Tests;

/// <summary>Item 8: account hard delete removes police clearance records AND their private files; failures are loud and retried.</summary>
public class PoliceClearanceHardDeleteTests
{
    private static readonly MethodInfo Process = typeof(UserHardDeleteProcessor)
        .GetMethod("ProcessSingleCaregiverAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static async Task Run(UserHardDeleteProcessor p, CareProDbContext db, FakeClearanceFileStore store, Caregiver cg)
    {
        try { await (Task)Process.Invoke(p, new object[] { db, Mock.Of<IEmailService>(), store, cg })!; }
        catch (TargetInvocationException ex) { throw ex.InnerException!; }
    }

    private static async Task<PoliceClearance> Seed(CareProDbContext db, FakeClearanceFileStore store, Caregiver cg, int files)
    {
        var list = new List<PoliceClearanceFile>();
        for (var i = 0; i < files; i++)
        {
            var s = await store.UploadAsync(new byte[] { 1, 2, 3 }, ".jpg");
            list.Add(new PoliceClearanceFile { PublicId = s.PublicId, ResourceType = s.ResourceType, OriginalFileName = $"f{i}.jpg", ContentType = "image/jpeg", SizeBytes = 3 });
        }
        var rec = new PoliceClearance { Id = ObjectId.GenerateNewId(), CaregiverId = cg.Id.ToString(), Status = PoliceClearanceStatuses.Approved, Files = list, NameOnDocument = "CLEARANCETEST", SubmittedAt = DateTime.UtcNow };
        db.PoliceClearances.Add(rec);
        await db.SaveChangesAsync();
        return rec;
    }

    private static (UserHardDeleteProcessor proc, Mock<ILogger<UserHardDeleteProcessor>> log) Processor()
    {
        var log = new Mock<ILogger<UserHardDeleteProcessor>>();
        return (new UserHardDeleteProcessor(Mock.Of<IServiceScopeFactory>(), log.Object), log);
    }

    [Fact]
    public async Task HardDelete_RemovesRecordsAndEveryFile_AllHistory_LeavesOthersAlone()
    {
        var name = PoliceClearanceTestSupport.NewDbName();
        using var db = PoliceClearanceTestSupport.CreateDb(name);
        var store = new FakeClearanceFileStore();
        var gone = PoliceClearanceTestSupport.SeedCaregiver(db, "Leaving");
        var stays = PoliceClearanceTestSupport.SeedCaregiver(db, "Staying");
        var r1 = await Seed(db, store, gone, 2);
        var r2 = await Seed(db, store, gone, 1);          // an older, superseded submission is covered too
        r2.Superseded = true; await db.SaveChangesAsync();
        var keep = await Seed(db, store, stays, 1);
        var goneFiles = r1.Files!.Concat(r2.Files!).Select(f => f.PublicId!).ToList();
        Assert.Equal(4, store.Files.Count);

        var (proc, _) = Processor();
        await Run(proc, db, store, gone);

        using var fresh = PoliceClearanceTestSupport.CreateDb(name);
        Assert.Empty(await fresh.PoliceClearances.Where(c => c.CaregiverId == gone.Id.ToString()).ToListAsync());
        Assert.Single(await fresh.PoliceClearances.Where(c => c.CaregiverId == stays.Id.ToString()).ToListAsync());
        Assert.Equal(goneFiles.OrderBy(x => x), store.Deleted.OrderBy(x => x));          // every file of every submission deleted from storage
        Assert.Single(store.Files);                                                        // the other caregiver's file is untouched
        Console.WriteLine($"[evidence] hard delete: {goneFiles.Count} storage files deleted, clearance records for the account = 0; other caregiver's record+file intact");
        Assert.Equal("[DELETED]", (await fresh.CareGivers.FirstAsync(c => c.Id == gone.Id)).FirstName);   // normal anonymisation still ran
    }

    [Fact]
    public async Task FileDeletionFailure_IsLoggedLoudly_ThrowsBeforeAnythingIsRemoved_AndRetrySucceeds()
    {
        var name = PoliceClearanceTestSupport.NewDbName();
        using var db = PoliceClearanceTestSupport.CreateDb(name);
        var store = new FakeClearanceFileStore { FailDeletes = true };
        var cg = PoliceClearanceTestSupport.SeedCaregiver(db, "Stuck");
        var rec = await Seed(db, store, cg, 2);
        var (proc, log) = Processor();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(proc, db, store, cg));

        log.Verify(l => l.Log(LogLevel.Error, It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("could not be deleted from storage") && v.ToString()!.Contains(rec.Files![0].PublicId!)),
            It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);

        using var mid = PoliceClearanceTestSupport.CreateDb(name);
        Assert.Single(await mid.PoliceClearances.Where(c => c.CaregiverId == cg.Id.ToString()).ToListAsync());      // record (and its file ids) kept for the retry
        Assert.NotEqual("[DELETED]", (await mid.CareGivers.FirstAsync(c => c.Id == cg.Id)).FirstName);              // account not half-anonymised
        Console.WriteLine("[evidence] storage delete failed -> ERROR logged naming the public ids, processing threw, record + account untouched (retried next cycle)");

        store.FailDeletes = false;                                                                                   // storage recovers
        using var retryDb = PoliceClearanceTestSupport.CreateDb(name);
        var again = await retryDb.CareGivers.FirstAsync(c => c.Id == cg.Id);
        await Run(proc, retryDb, store, again);
        using var end = PoliceClearanceTestSupport.CreateDb(name);
        Assert.Empty(await end.PoliceClearances.Where(c => c.CaregiverId == cg.Id.ToString()).ToListAsync());
        Assert.Equal(2, store.Deleted.Count);
    }

    [Fact]
    public async Task AlreadyGoneFiles_CountAsDeleted_SoRetriesDoNotGetStuck()
    {
        var name = PoliceClearanceTestSupport.NewDbName();
        using var db = PoliceClearanceTestSupport.CreateDb(name);
        var store = new FakeClearanceFileStore();
        var cg = PoliceClearanceTestSupport.SeedCaregiver(db, "Retry");
        var rec = await Seed(db, store, cg, 1);
        store.Files.Clear();                                                      // file already removed on an earlier attempt
        var (proc, _) = Processor();
        await Run(proc, db, store, cg);
        using var fresh = PoliceClearanceTestSupport.CreateDb(name);
        Assert.Empty(await fresh.PoliceClearances.Where(c => c.CaregiverId == cg.Id.ToString()).ToListAsync());
    }

    [Fact]
    public async Task CaregiverWithNoClearances_StillProcessesNormally()
    {
        var name = PoliceClearanceTestSupport.NewDbName();
        using var db = PoliceClearanceTestSupport.CreateDb(name);
        var cg = PoliceClearanceTestSupport.SeedCaregiver(db, "Plain");
        var (proc, _) = Processor();
        await Run(proc, db, new FakeClearanceFileStore(), cg);
        using var fresh = PoliceClearanceTestSupport.CreateDb(name);
        Assert.Equal("[DELETED]", (await fresh.CareGivers.FirstAsync(c => c.Id == cg.Id)).FirstName);
    }

    [Fact]
    public async Task StaffViewAuditRows_DoNotContainTheOriginalFileName()
    {
        // The audit entry for a staff file view records only the file INDEX, so audit rows (which are retained) never carry a
        // filename that might contain a person's name.
        var src = File.ReadAllText(FindUp("Infrastructure/Content/Services/PoliceClearanceAdminService.cs"));
        Assert.Contains("new { fileIndex }", src);
        Assert.DoesNotContain("fileName = record.Files", src);
        await Task.CompletedTask;
    }

    private static string FindUp(string relative)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, relative))) return Path.Combine(d.FullName, relative);
        throw new FileNotFoundException(relative);
    }
}
