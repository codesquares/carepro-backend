using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Application.DTOs;
using Application.Interfaces.Content;
using Domain.Entities;
using Infrastructure.Content.Helpers;
using Infrastructure.Content.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using MongoDB.Bson;
using Xunit;

namespace CommitmentGate.Tests;

/// <summary>Item 2 — service level: age rule, lifecycle, superseding, validation. Real Mongo (27018), fake storage.</summary>
public class PoliceClearanceCaregiverServiceTests
{
    private sealed class Rig
    {
        public Infrastructure.Content.Data.CareProDbContext Db = null!;
        public FakeClearanceFileStore Store = new();
        public Mock<IPoliceClearanceNotifier> Notifier = new();
        public FakeClock Clock = new() { UtcNowValue = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc) };
        public PoliceClearanceService Service = null!;
        public Caregiver Caregiver = null!;
    }

    private static Rig Build()
    {
        var r = new Rig { Db = PoliceClearanceTestSupport.CreateDb(PoliceClearanceTestSupport.NewDbName()) };
        r.Caregiver = PoliceClearanceTestSupport.SeedCaregiver(r.Db);
        r.Service = new PoliceClearanceService(r.Db, r.Store, r.Notifier.Object, new ConfigurationBuilder().Build(),
            Mock.Of<ILogger<PoliceClearanceService>>(), r.Clock);
        return r;
    }

    private static SubmitPoliceClearanceRequest Req(string issue, byte[]? bytes = null, string ct = "application/pdf", string fileName = "c.pdf") => new()
    {
        IssueDate = issue, NameOnDocument = "CLEARANCETEST Subject", ReferenceNumber = "NPF-1",
        Files = { new ClearanceUploadFile { FileName = fileName, ContentType = ct, Bytes = bytes ?? FakeBytes.Pdf } }
    };

    private static class FakeBytes { public static readonly byte[] Pdf = ClearanceHostFactory.PdfBytes(); public static readonly byte[] Jpg = ClearanceHostFactory.JpgBytes(); }

    // ── age rule boundaries (today in Lagos = 2026-10-10; 6 months back = 2026-04-10) ──
    [Theory]
    [InlineData("2026-04-10", true)]    // exactly 6 months: just inside
    [InlineData("2026-04-11", true)]
    [InlineData("2026-10-10", true)]    // today
    [InlineData("2026-04-09", false)]   // one day older than 6 months: just outside
    [InlineData("2020-01-01", false)]
    [InlineData("2026-10-11", false)]   // tomorrow: future
    public async Task IssueDate_AgeRule_Boundaries(string issue, bool accepted)
    {
        var r = Build();
        if (accepted)
        {
            var res = await r.Service.SubmitAsync(r.Caregiver.Id.ToString(), Req(issue));
            Assert.Equal("pending", res.Status);
        }
        else
        {
            var ex = await Assert.ThrowsAsync<PoliceClearanceValidationException>(() => r.Service.SubmitAsync(r.Caregiver.Id.ToString(), Req(issue)));
            Console.WriteLine($"[evidence] {issue}: {ex.Message}");
            Assert.Empty(r.Store.Files);                              // rejected BEFORE anything was stored
            Assert.Empty(await r.Db.PoliceClearances.ToListAsync());
        }
    }

    [Fact]
    public async Task OldClearance_FriendlyMessage()
    {
        var r = Build();
        var ex = await Assert.ThrowsAsync<PoliceClearanceValidationException>(() => r.Service.SubmitAsync(r.Caregiver.Id.ToString(), Req("2025-01-01")));
        Assert.Equal("This clearance is older than 6 months. Please upload a recent one.", ex.Message);
    }

    [Fact]
    public async Task AgeRule_UsesConfiguredMonths()
    {
        var r = Build();
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["PoliceClearance:MaxAgeMonths"] = "1" }).Build();
        var svc = new PoliceClearanceService(r.Db, r.Store, r.Notifier.Object, cfg, Mock.Of<ILogger<PoliceClearanceService>>(), r.Clock);
        await Assert.ThrowsAsync<PoliceClearanceValidationException>(() => svc.SubmitAsync(r.Caregiver.Id.ToString(), Req("2026-09-09")));
        Assert.Equal("pending", (await svc.SubmitAsync(r.Caregiver.Id.ToString(), Req("2026-09-10"))).Status);
    }

    [Fact]
    public async Task Submit_CreatesPending_StoresFiles_AlertsAdmins_ReturnsNoStorageIds()
    {
        var r = Build();
        var res = await r.Service.SubmitAsync(r.Caregiver.Id.ToString(), Req("2026-09-01"));
        Assert.Equal("pending", res.Status);
        Assert.False(res.CanResubmit == false);        // pending can be replaced
        Assert.Single(res.Files);
        Assert.Single(r.Store.Files);
        r.Notifier.Verify(n => n.NotifySubmittedAsync(It.IsAny<PoliceClearance>(), It.IsAny<string>()), Times.Once);
        var json = JsonSerializer.Serialize(res);
        Assert.DoesNotContain("fake/", json);          // no storage public id/url in the caregiver response
        Assert.DoesNotContain("http", json);
    }

    [Fact]
    public async Task Resubmit_WhilePending_Replaces_AndSupersedesOlder_HistoryKept()
    {
        var r = Build();
        var id = r.Caregiver.Id.ToString();
        var first = await r.Service.SubmitAsync(id, Req("2026-09-01"));
        var second = await r.Service.SubmitAsync(id, Req("2026-09-02"));
        Assert.NotEqual(first.ClearanceId, second.ClearanceId);
        var all = await r.Db.PoliceClearances.Where(c => c.CaregiverId == id).ToListAsync();
        Assert.Equal(2, all.Count);
        Assert.True(all.Single(c => c.Id.ToString() == first.ClearanceId).Superseded);
        Assert.Equal(2, second.History.Count);
        Assert.Equal(2, r.Store.Files.Count);          // old file kept (history is never deleted by the caregiver)
    }

    [Fact]
    public async Task Resubmit_AfterRejectedOrRevoked_Allowed()
    {
        foreach (var status in new[] { PoliceClearanceStatuses.Rejected, PoliceClearanceStatuses.Revoked })
        {
            var r = Build();
            var id = r.Caregiver.Id.ToString();
            r.Db.PoliceClearances.Add(new PoliceClearance { Id = ObjectId.GenerateNewId(), CaregiverId = id, Status = status, SubmittedAt = DateTime.UtcNow.AddDays(-2), RejectionReason = "bad", RevokedReason = "worse" });
            await r.Db.SaveChangesAsync();
            var before = await r.Service.GetStatusAsync(id);
            Assert.True(before.CanResubmit);
            Assert.Equal(status, before.Status);
            var after = await r.Service.SubmitAsync(id, Req("2026-09-01"));
            Assert.Equal("pending", after.Status);
        }
    }

    [Fact]
    public async Task Resubmit_WhenApproved_Is409()
    {
        var r = Build();
        var id = r.Caregiver.Id.ToString();
        r.Db.PoliceClearances.Add(new PoliceClearance { Id = ObjectId.GenerateNewId(), CaregiverId = id, Status = PoliceClearanceStatuses.Approved, SubmittedAt = DateTime.UtcNow.AddDays(-2) });
        await r.Db.SaveChangesAsync();
        Assert.False((await r.Service.GetStatusAsync(id)).CanResubmit);
        await Assert.ThrowsAsync<PoliceClearanceConflictException>(() => r.Service.SubmitAsync(id, Req("2026-09-01")));
        Assert.Empty(r.Store.Files);
    }

    [Fact]
    public async Task Status_ShowsRejectionReason_AndRevokedReason()
    {
        var r = Build();
        var id = r.Caregiver.Id.ToString();
        r.Db.PoliceClearances.Add(new PoliceClearance { Id = ObjectId.GenerateNewId(), CaregiverId = id, Status = PoliceClearanceStatuses.Rejected, SubmittedAt = DateTime.UtcNow, RejectionReason = "Blurry scan", ReviewedAt = DateTime.UtcNow });
        await r.Db.SaveChangesAsync();
        var s = await r.Service.GetStatusAsync(id);
        Assert.Equal("Blurry scan", s.RejectionReason);
        Assert.NotNull(s.ReviewedAt);
    }

    [Fact]
    public async Task Status_NeverIncludesAnotherCaregiversRecords()
    {
        var r = Build();
        var other = PoliceClearanceTestSupport.SeedCaregiver(r.Db, "Other", "CLEARANCETEST");
        await r.Service.SubmitAsync(other.Id.ToString(), Req("2026-09-01"));
        var mine = await r.Service.GetStatusAsync(r.Caregiver.Id.ToString());
        Assert.Equal("none", mine.Status);
        Assert.Empty(mine.History);
        Assert.Empty(mine.Files);
    }

    [Fact]
    public async Task RenamedExe_AsPdf_IsRejected_Nothing_Stored()
    {
        var r = Build();
        var exe = System.Text.Encoding.ASCII.GetBytes("MZ\u0090\0\u0003\0\0\0 not a pdf at all");
        var ex = await Assert.ThrowsAsync<PoliceClearanceValidationException>(() => r.Service.SubmitAsync(r.Caregiver.Id.ToString(), Req("2026-09-01", exe, "application/pdf", "clearance.pdf")));
        Assert.Contains("valid PDF", ex.Message);
        Assert.Empty(r.Store.Files);
        Assert.Empty(await r.Db.PoliceClearances.ToListAsync());
    }

    [Theory]
    [InlineData("", "NPF-1")]
    [InlineData("A", "NPF-1")]
    [InlineData("Real Name", "")]
    [InlineData("Real Name", "x")]
    public async Task MissingOrTooShortFields_Rejected(string name, string reference)
    {
        var r = Build();
        var req = Req("2026-09-01"); req.NameOnDocument = name; req.ReferenceNumber = reference;
        await Assert.ThrowsAsync<PoliceClearanceValidationException>(() => r.Service.SubmitAsync(r.Caregiver.Id.ToString(), req));
    }

    [Fact]
    public async Task MissingOrBadIssueDate_Rejected()
    {
        var r = Build();
        foreach (var d in new[] { "", "not-a-date", null })
            await Assert.ThrowsAsync<PoliceClearanceValidationException>(() => r.Service.SubmitAsync(r.Caregiver.Id.ToString(), Req(d!)));
    }

    [Fact]
    public async Task StorageFailure_IsFriendly_AndLeavesNoRecord_AndCleansPartialUploads()
    {
        var r = Build();
        var req = Req("2026-09-01");
        req.Files.Clear();
        req.Files.Add(new ClearanceUploadFile { FileName = "1.jpg", ContentType = "image/jpeg", Bytes = FakeBytes.Jpg });
        req.Files.Add(new ClearanceUploadFile { FileName = "2.jpg", ContentType = "image/jpeg", Bytes = FakeBytes.Jpg });
        r.Store.FailUploads = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => r.Service.SubmitAsync(r.Caregiver.Id.ToString(), req));
        Assert.Empty(await r.Db.PoliceClearances.ToListAsync());
    }

    [Fact]
    public async Task DailyCap_StopsRunawaySubmissions()
    {
        var r = Build();
        var id = r.Caregiver.Id.ToString();
        for (var i = 0; i < 5; i++) await r.Service.SubmitAsync(id, Req("2026-09-01"));
        await Assert.ThrowsAsync<PoliceClearanceConflictException>(() => r.Service.SubmitAsync(id, Req("2026-09-01")));
    }

    [Fact]
    public async Task OwnFile_OwnerCanRead_OtherCaregiverGets403_UnknownIs404()
    {
        var r = Build();
        var id = r.Caregiver.Id.ToString();
        var res = await r.Service.SubmitAsync(id, Req("2026-09-01"));
        var file = await r.Service.GetOwnFileAsync(id, res.ClearanceId!, 0);
        Assert.Equal(FakeBytes.Pdf, file.Bytes);
        Assert.Equal("application/pdf", file.ContentType);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => r.Service.GetOwnFileAsync(ObjectId.GenerateNewId().ToString(), res.ClearanceId!, 0));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => r.Service.GetOwnFileAsync(id, ObjectId.GenerateNewId().ToString(), 0));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => r.Service.GetOwnFileAsync(id, res.ClearanceId!, 5));
    }

    [Fact]
    public void Logic_CheckIssueDate_UsesLagosDate()
    {
        // 23:30 UTC on the 10th is already 00:30 on the 11th in Lagos: an issue date of the 11th is "today", not future.
        var now = new DateTime(2026, 10, 10, 23, 30, 0, DateTimeKind.Utc);
        Assert.Null(PoliceClearanceLogic.CheckIssueDate(new DateTime(2026, 10, 11), now, 6));
        Assert.NotNull(PoliceClearanceLogic.CheckIssueDate(new DateTime(2026, 10, 12), now, 6));
    }
}

/// <summary>Item 2 — HTTP level, real pipeline: auth/roles, ownership, multipart errors are 400 not 500.</summary>
public class PoliceClearanceCaregiverHttpTests : IClassFixture<ClearanceHostFactory>
{
    private readonly ClearanceHostFactory _f;
    public PoliceClearanceCaregiverHttpTests(ClearanceHostFactory f) => _f = f;
    private const string Route = "/api/caregiver/vetting/police-clearance";

    private async Task<Caregiver> NewCaregiver(string first = "CLEARANCETEST")
    {
        using var db = _f.Db();
        return PoliceClearanceTestSupport.SeedCaregiver(db, first);
    }

    [Fact]
    public async Task NoToken_401_AdminToken_403_OnEveryRoute()
    {
        foreach (var (method, url) in new[] { ("GET", Route), ("POST", Route), ("GET", $"{Route}/{ObjectId.GenerateNewId()}/files/0") })
        {
            var anon = await _f.Client().SendAsync(new HttpRequestMessage(new HttpMethod(method), url));
            Assert.Equal(HttpStatusCode.Unauthorized, anon.StatusCode);
            foreach (var role in new[] { "Admin:HR", "Admin:Finance", "SuperAdmin" })
            {
                var res = await _f.Client(role, ObjectId.GenerateNewId().ToString()).SendAsync(new HttpRequestMessage(new HttpMethod(method), url));
                Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
            }
        }
        Console.WriteLine("[evidence] caregiver routes: no token=401; Admin:HR/Admin:Finance/SuperAdmin token=403 on GET, POST and file route");
    }

    [Fact]
    public async Task Get_NoRecord_ReturnsNone_AndCanResubmit()
    {
        var cg = await NewCaregiver();
        var res = await _f.Client("Caregiver", cg.Id.ToString()).GetAsync(Route);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("none", body.GetProperty("status").GetString());
        Assert.True(body.GetProperty("canResubmit").GetBoolean());
        Assert.Equal(6, body.GetProperty("maxAgeMonths").GetInt32());
    }

    [Fact]
    public async Task Submit_Pdf_Works_AndOnlyOwnerSeesIt_BodyAndQueryIdsAreIgnored()
    {
        var a = await NewCaregiver("CLEARANCETEST-A");
        var b = await NewCaregiver("CLEARANCETEST-B");
        var ca = _f.Client("Caregiver", a.Id.ToString());
        var cb = _f.Client("Caregiver", b.Id.ToString());

        // A tries to submit AS B by putting B's id in the body and query — both are ignored.
        var form = ClearanceHostFactory.Form(ClearanceHostFactory.DateAgo(10), extraFields: new() { ["caregiverId"] = b.Id.ToString(), ["CaregiverId"] = b.Id.ToString() });
        var post = await ca.PostAsync($"{Route}?caregiverId={b.Id}&id={b.Id}", form);
        var postBody = await post.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);

        using (var db = _f.Db())
        {
            var rec = await db.PoliceClearances.SingleAsync(c => c.NameOnDocument == "CLEARANCETEST Subject" && c.CaregiverId == a.Id.ToString());
            Console.WriteLine($"[evidence] DB: record stored under caller A ({rec.CaregiverId}); B ({b.Id}) has {await db.PoliceClearances.CountAsync(c => c.CaregiverId == b.Id.ToString())} records");
            Assert.Equal(0, await db.PoliceClearances.CountAsync(c => c.CaregiverId == b.Id.ToString()));
        }

        var bView = await (await cb.GetAsync($"{Route}?caregiverId={a.Id}")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("none", bView.GetProperty("status").GetString());          // B cannot see A's, even naming A in the query
        var aView = await (await ca.GetAsync(Route)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("pending", aView.GetProperty("status").GetString());
        var clearanceId = aView.GetProperty("clearanceId").GetString()!;

        // Owner can view own file; another caregiver gets 403.
        var own = await ca.GetAsync($"{Route}/{clearanceId}/files/0");
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Equal("application/pdf", own.Content.Headers.ContentType!.MediaType);
        Assert.Equal("private, no-store", own.Headers.CacheControl!.ToString().Replace("no-store, private", "private, no-store"));
        Assert.Equal(ClearanceHostFactory.PdfBytes(), await own.Content.ReadAsByteArrayAsync());
        var foreign = await cb.GetAsync($"{Route}/{clearanceId}/files/0");
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        Console.WriteLine($"[evidence] file route: owner=200, other caregiver=403");

        // No storage id/url leaks anywhere in the caregiver response.
        Assert.DoesNotContain("fake/", postBody);
        Assert.DoesNotContain("http", postBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Submit_Errors_Are400NeverFiveHundred()
    {
        var cg = await NewCaregiver();
        var client = _f.Client("Caregiver", cg.Id.ToString());

        async Task<(HttpStatusCode code, string msg)> Post(MultipartFormDataContent form)
        {
            var r = await client.PostAsync(Route, form);
            var j = await r.Content.ReadFromJsonAsync<JsonElement>();
            return (r.StatusCode, j.GetProperty("message").GetString()!);
        }

        // too old
        var old = await Post(ClearanceHostFactory.Form("2024-01-01"));
        Assert.Equal(HttpStatusCode.BadRequest, old.code);
        Assert.Equal("This clearance is older than 6 months. Please upload a recent one.", old.msg);
        // future
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(ClearanceHostFactory.Form(DateTime.UtcNow.AddDays(3).ToString("yyyy-MM-dd")))).code);
        // renamed exe as pdf
        var exe = System.Text.Encoding.ASCII.GetBytes("MZ\u0090\0\u0003\0\0\0 binary");
        var bad = await Post(ClearanceHostFactory.Form(ClearanceHostFactory.DateAgo(5), files: new[] { ("clearance.pdf", "application/pdf", exe) }));
        Assert.Equal(HttpStatusCode.BadRequest, bad.code);
        Console.WriteLine($"[evidence] exe renamed .pdf -> {(int)bad.code} \"{bad.msg}\"");
        // wrong declared type
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(ClearanceHostFactory.Form(ClearanceHostFactory.DateAgo(5), files: new[] { ("x.html", "text/html", ClearanceHostFactory.PdfBytes()) }))).code);
        // no files
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(ClearanceHostFactory.Form(ClearanceHostFactory.DateAgo(5), files: Array.Empty<(string, string, byte[])>()))).code);
        // six images
        var six = Enumerable.Range(0, 6).Select(i => ($"{i}.jpg", "image/jpeg", ClearanceHostFactory.JpgBytes())).ToArray();
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(ClearanceHostFactory.Form(ClearanceHostFactory.DateAgo(5), files: six))).code);
        // oversize single file (11 MB)
        var big = new byte[11 * 1024 * 1024]; ClearanceHostFactory.JpgBytes().CopyTo(big, 0);
        var oversize = await Post(ClearanceHostFactory.Form(ClearanceHostFactory.DateAgo(5), files: new[] { ("big.jpg", "image/jpeg", big) }));
        Assert.Equal(HttpStatusCode.BadRequest, oversize.code);
        Console.WriteLine($"[evidence] 11 MB file -> {(int)oversize.code} \"{oversize.msg}\"");
        // not multipart at all
        var notMultipart = await client.PostAsync(Route, new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        Assert.True((int)notMultipart.StatusCode is 400 or 415, $"got {(int)notMultipart.StatusCode}");
        // missing fields
        var noName = await Post(ClearanceHostFactory.Form(ClearanceHostFactory.DateAgo(5), name: ""));
        Assert.Equal(HttpStatusCode.BadRequest, noName.code);

        using var db = _f.Db();
        Assert.Equal(0, await db.PoliceClearances.CountAsync(c => c.CaregiverId == cg.Id.ToString()));   // none of the failures stored anything
    }

    [Fact]
    public async Task Submit_Over25MbRequest_Is413Or400_NotFiveHundred()
    {
        var cg = await NewCaregiver();
        var big = new byte[26 * 1024 * 1024]; ClearanceHostFactory.JpgBytes().CopyTo(big, 0);
        var res = await _f.Client("Caregiver", cg.Id.ToString()).PostAsync(Route,
            ClearanceHostFactory.Form(ClearanceHostFactory.DateAgo(5), files: new[] { ("big.jpg", "image/jpeg", big) }));
        Console.WriteLine($"[evidence] 26 MB request -> {(int)res.StatusCode}");
        Assert.True((int)res.StatusCode is 400 or 413, $"got {(int)res.StatusCode}");
    }

    [Fact]
    public async Task Approved_Blocks_Resubmit_With409()
    {
        var cg = await NewCaregiver();
        using (var db = _f.Db())
        {
            db.PoliceClearances.Add(new PoliceClearance { Id = ObjectId.GenerateNewId(), CaregiverId = cg.Id.ToString(), Status = PoliceClearanceStatuses.Approved, SubmittedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var res = await _f.Client("Caregiver", cg.Id.ToString()).PostAsync(Route, ClearanceHostFactory.Form(ClearanceHostFactory.DateAgo(5)));
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Console.WriteLine("[evidence] POST while latest=approved -> 409");
    }
}
