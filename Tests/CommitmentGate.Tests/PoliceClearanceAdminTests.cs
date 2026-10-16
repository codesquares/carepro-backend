using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Moq;
using MongoDB.Bson;
using Xunit;

namespace CommitmentGate.Tests;

/// <summary>Item 4 (+ the staff-view parts of item 3): admin review API — roles, checklist, reasons, JWT reviewer, races, audit.</summary>
public class PoliceClearanceAdminHttpTests : IClassFixture<ClearanceHostFactory>
{
    private readonly ClearanceHostFactory _f;
    public PoliceClearanceAdminHttpTests(ClearanceHostFactory f) => _f = f;
    private const string Admin = "/api/admin/police-clearances";
    private static readonly object FullChecklist = new { nameMatchesProfile = true, issuedByNigeriaPoliceForce = true, issueDateWithinAllowedAge = true, referenceNumberCaptured = true, scanLegibleAndUnaltered = true };

    private async Task<(Caregiver cg, string id)> SeedPending(string first = "CLEARANCETEST", int files = 1, string name = "CLEARANCETEST Subject")
    {
        using var db = _f.Db();
        var cg = PoliceClearanceTestSupport.SeedCaregiver(db, first, "Subject");
        var list = new List<PoliceClearanceFile>();
        for (var i = 0; i < files; i++)
        {
            var bytes = files == 1 ? ClearanceHostFactory.PdfBytes($"file{i}") : ClearanceHostFactory.JpgBytes().Concat(new byte[] { (byte)i }).ToArray();
            var s = await _f.Store.UploadAsync(bytes, files == 1 ? ".pdf" : ".jpg");
            list.Add(new PoliceClearanceFile { PublicId = s.PublicId, ResourceType = "raw", OriginalFileName = $"f{i}", ContentType = files == 1 ? "application/pdf" : "image/jpeg", SizeBytes = bytes.Length });
        }
        var rec = new PoliceClearance { Id = ObjectId.GenerateNewId(), CaregiverId = cg.Id.ToString(), Status = PoliceClearanceStatuses.Pending, SubmittedAt = DateTime.UtcNow, IssueDate = DateTime.UtcNow.Date, NameOnDocument = name, ReferenceNumber = "NPF-9", Files = list, Superseded = false };
        db.PoliceClearances.Add(rec);
        await db.SaveChangesAsync();
        return (cg, rec.Id.ToString());
    }

    private HttpClient Ops(string id = "ops-admin-1", string email = "ops1@carepro.test") => _f.Client("Admin:HR", id, email);

    // ───────────── roles on every route ─────────────
    [Fact]
    public async Task EveryRoute_NoToken401_Caregiver403_OtherDepartmentsAndSuperAdmin()
    {
        var (_, id) = await SeedPending();
        var routes = new (string m, string url, object? body)[]
        {
            ("GET", Admin, null), ("GET", $"{Admin}/pending-count", null), ("GET", $"{Admin}/{id}", null), ("GET", $"{Admin}/{id}/files/0", null),
            ("POST", $"{Admin}/{id}/approve", new { checklist = FullChecklist }), ("POST", $"{Admin}/{id}/reject", new { reason = "not good" }), ("POST", $"{Admin}/{id}/revoke", new { reason = "not good" })
        };
        var table = new List<string>();
        foreach (var (m, url, body) in routes)
        {
            HttpRequestMessage Req() { var r = new HttpRequestMessage(new HttpMethod(m), url); if (body != null) r.Content = JsonContent.Create(body); return r; }
            Assert.Equal(HttpStatusCode.Unauthorized, (await _f.Client().SendAsync(Req())).StatusCode);
            foreach (var role in new[] { "Caregiver", "Client", "Admin:Finance", "Admin:MarketingAndSales", "Admin:Engineering" })
            {
                var res = await _f.Client(role, "someone").SendAsync(Req());
                Assert.True(res.StatusCode == HttpStatusCode.Forbidden, $"{role} {m} {url} -> {(int)res.StatusCode}");
            }
            table.Add($"{m} {url.Replace(id, "{id}")}: none=401 Caregiver/Client/Finance/Marketing/Engineering=403");
        }
        foreach (var t in table) Console.WriteLine("[evidence] " + t);
        // Operations departments and SuperAdmin are allowed (read routes).
        foreach (var role in new[] { "Admin:HR", "Admin:ComplianceAndLegal", "Admin:CareLeads", "SuperAdmin" })
            Assert.Equal(HttpStatusCode.OK, (await _f.Client(role, "a1").GetAsync(Admin)).StatusCode);
    }

    // ───────────── checklist / reason rules ─────────────
    [Fact]
    public async Task Approve_RequiresEveryChecklistItem_400_ListsMissing()
    {
        var (_, id) = await SeedPending();
        var partial = new { checklist = new { nameMatchesProfile = true, issuedByNigeriaPoliceForce = true, issueDateWithinAllowedAge = true, referenceNumberCaptured = true, scanLegibleAndUnaltered = false } };
        var res = await Ops().PostAsJsonAsync($"{Admin}/{id}/approve", partial);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("scan legible and unaltered", body);
        Console.WriteLine($"[evidence] approve with 4/5 checklist -> {(int)res.StatusCode} {body}");
        Assert.Equal(HttpStatusCode.BadRequest, (await Ops().PostAsJsonAsync($"{Admin}/{id}/approve", new { })).StatusCode);                 // no checklist at all
        Assert.Equal(HttpStatusCode.BadRequest, (await Ops().PostAsJsonAsync($"{Admin}/{id}/approve", new { checklist = new { nameMatchesProfile = true } })).StatusCode);
        using var db = _f.Db();
        Assert.Equal(PoliceClearanceStatuses.Pending, (await db.PoliceClearances.FirstAsync(c => c.Id == ObjectId.Parse(id))).Status);   // untouched
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  no ")]
    [InlineData("1234")]
    public async Task Reject_And_Revoke_RequireAReason_400(string? reason)
    {
        var (_, id) = await SeedPending();
        var res = await Ops().PostAsJsonAsync($"{Admin}/{id}/reject", new { reason });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        if (reason == null) Console.WriteLine($"[evidence] reject without reason -> {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        Assert.Equal(HttpStatusCode.BadRequest, (await Ops().PostAsJsonAsync($"{Admin}/{id}/revoke", new { reason })).StatusCode);
    }

    [Fact]
    public async Task Approve_Persists_ChecklistReviewerFromJwt_NotFromBody_AndNotifiesCaregiver()
    {
        var (cg, id) = await SeedPending();
        // A forged adminId in the body must be ignored: the DTO has no such field and the JWT wins.
        var res = await Ops("jwt-admin-77", "jwt77@carepro.test").PostAsJsonAsync($"{Admin}/{id}/approve",
            new { checklist = FullChecklist, notes = "Looks genuine", adminId = "FORGED-ADMIN", reviewedByAdminId = "FORGED-ADMIN", adminEmail = "forged@x.com" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var db = _f.Db();
        var rec = await db.PoliceClearances.FirstAsync(c => c.Id == ObjectId.Parse(id));
        Console.WriteLine($"[evidence] DB after approve: Status={rec.Status} ReviewedByAdminId={rec.ReviewedByAdminId} Email={rec.ReviewedByAdminEmail} ReviewedAt={rec.ReviewedAt:O} Checklist.AllTrue={rec.Checklist!.AllTrue()} Notes='{rec.ReviewNotes}'");
        Assert.Equal(PoliceClearanceStatuses.Approved, rec.Status);
        Assert.Equal("jwt-admin-77", rec.ReviewedByAdminId);
        Assert.Equal("jwt77@carepro.test", rec.ReviewedByAdminEmail);
        Assert.NotNull(rec.ReviewedAt);
        Assert.True(rec.Checklist!.AllTrue());
        Assert.Equal("Looks genuine", rec.ReviewNotes);
        var audit = await db.AdminAuditLogs.FirstAsync(a => a.TargetEntityId == id && a.Action == "PoliceClearanceApproved");
        Assert.Equal("jwt-admin-77", audit.AdminId);
        _f.Email.Verify(e => e.SendGenericNotificationEmailAsync(cg.Email, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), false, null, null), Times.AtLeastOnce);
        Assert.True(await db.Notifications.AnyAsync(n => n.RecipientId == cg.Id.ToString() && n.Type == "police_clearance_approved"));
    }

    [Fact]
    public async Task Reject_PersistsReason_AndCaregiverSeesIt_InStatusAndNotification()
    {
        var (cg, id) = await SeedPending();
        var res = await Ops("rej-admin", "rej@carepro.test").PostAsJsonAsync($"{Admin}/{id}/reject", new { reason = "The photo is too blurry to read the reference number." });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var db = _f.Db();
        var rec = await db.PoliceClearances.FirstAsync(c => c.Id == ObjectId.Parse(id));
        Assert.Equal(PoliceClearanceStatuses.Rejected, rec.Status);
        Assert.Equal("rej-admin", rec.ReviewedByAdminId);
        Assert.Equal("The photo is too blurry to read the reference number.", rec.RejectionReason);
        var n = await db.Notifications.FirstAsync(x => x.RecipientId == cg.Id.ToString() && x.Type == "police_clearance_rejected");
        Assert.Contains("too blurry", n.Content);
        var view = await (await _f.Client("Caregiver", cg.Id.ToString()).GetAsync("/api/caregiver/vetting/police-clearance")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("rejected", view.GetProperty("status").GetString());
        Assert.Equal("The photo is too blurry to read the reference number.", view.GetProperty("rejectionReason").GetString());
        Assert.True(view.GetProperty("canResubmit").GetBoolean());
        Console.WriteLine($"[evidence] caregiver GET after reject: status=rejected, rejectionReason present, canResubmit=true");
    }

    [Fact]
    public async Task Revoke_OnlyFromApproved_RecordsWho_AndCaregiverSeesReason()
    {
        var (cg, id) = await SeedPending();
        Assert.Equal(HttpStatusCode.Conflict, (await Ops().PostAsJsonAsync($"{Admin}/{id}/revoke", new { reason = "valid reason here" })).StatusCode);   // pending can't be revoked
        Assert.Equal(HttpStatusCode.OK, (await Ops().PostAsJsonAsync($"{Admin}/{id}/approve", new { checklist = FullChecklist })).StatusCode);
        var res = await Ops("rev-admin", "rev@carepro.test").PostAsJsonAsync($"{Admin}/{id}/revoke", new { reason = "Reference number failed re-verification." });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var db = _f.Db();
        var rec = await db.PoliceClearances.FirstAsync(c => c.Id == ObjectId.Parse(id));
        Assert.Equal(PoliceClearanceStatuses.Revoked, rec.Status);
        Assert.Equal("rev-admin", rec.RevokedByAdminId);
        Assert.NotNull(rec.RevokedAt);
        Assert.Equal("Reference number failed re-verification.", rec.RevokedReason);
        var view = await (await _f.Client("Caregiver", cg.Id.ToString()).GetAsync("/api/caregiver/vetting/police-clearance")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("revoked", view.GetProperty("status").GetString());
        Assert.Equal("Reference number failed re-verification.", view.GetProperty("revokedReason").GetString());
        Assert.True(await db.Notifications.AnyAsync(n => n.RecipientId == cg.Id.ToString() && n.Type == "police_clearance_revoked" && n.Content.Contains("failed re-verification")));
    }

    // ───────────── latest-only / idempotency / races ─────────────
    [Fact]
    public async Task SecondDecision_IsSafe409_NeverOverwrites()
    {
        var (_, id) = await SeedPending();
        Assert.Equal(HttpStatusCode.OK, (await Ops().PostAsJsonAsync($"{Admin}/{id}/approve", new { checklist = FullChecklist })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Ops().PostAsJsonAsync($"{Admin}/{id}/approve", new { checklist = FullChecklist })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Ops().PostAsJsonAsync($"{Admin}/{id}/reject", new { reason = "changed my mind" })).StatusCode);
        using var db = _f.Db();
        Assert.Equal(PoliceClearanceStatuses.Approved, (await db.PoliceClearances.FirstAsync(c => c.Id == ObjectId.Parse(id))).Status);
    }

    [Fact]
    public async Task SupersededRecord_CannotBeReviewed_409()
    {
        var (cg, oldId) = await SeedPending();
        using (var db = _f.Db())
        {
            var old = await db.PoliceClearances.FirstAsync(c => c.Id == ObjectId.Parse(oldId));
            old.Superseded = true;
            db.PoliceClearances.Add(new PoliceClearance { Id = ObjectId.GenerateNewId(), CaregiverId = cg.Id.ToString(), Status = PoliceClearanceStatuses.Pending, SubmittedAt = DateTime.UtcNow.AddMinutes(1), Superseded = false });
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await Ops().PostAsJsonAsync($"{Admin}/{oldId}/approve", new { checklist = FullChecklist })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Ops().PostAsJsonAsync($"{Admin}/{oldId}/reject", new { reason = "not the latest" })).StatusCode);
    }

    [Fact]
    public async Task ConcurrentApproveApprove_AndApproveReject_ExactlyOneWins()
    {
        for (var round = 0; round < 3; round++)
        {
            var (_, a) = await SeedPending();
            var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => Task.Run(async () =>
                (await Ops($"adm{i}").PostAsJsonAsync($"{Admin}/{a}/approve", new { checklist = FullChecklist })).StatusCode)));
            Assert.Equal(1, results.Count(r => r == HttpStatusCode.OK));
            Assert.Equal(5, results.Count(r => r == HttpStatusCode.Conflict));

            var (_, b) = await SeedPending();
            var mixed = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => Task.Run(async () =>
                i % 2 == 0 ? (await Ops($"adm{i}").PostAsJsonAsync($"{Admin}/{b}/approve", new { checklist = FullChecklist })).StatusCode
                           : (await Ops($"adm{i}").PostAsJsonAsync($"{Admin}/{b}/reject", new { reason = "rejecting concurrently" })).StatusCode)));
            Assert.Equal(1, mixed.Count(r => r == HttpStatusCode.OK));
            Assert.Equal(5, mixed.Count(r => r == HttpStatusCode.Conflict));
            using var db = _f.Db();
            var final = await db.PoliceClearances.FirstAsync(c => c.Id == ObjectId.Parse(b));
            Assert.True(final.Status is PoliceClearanceStatuses.Approved or PoliceClearanceStatuses.Rejected);
            // exactly one decision's data present — not a blend of both
            Assert.Equal(final.Status == PoliceClearanceStatuses.Rejected, final.RejectionReason != null);
            Assert.Equal(final.Status == PoliceClearanceStatuses.Approved, final.Checklist != null);
            if (round == 0) Console.WriteLine($"[evidence] 6 parallel approve/reject on one record -> {string.Join(",", mixed.Select(m => (int)m))} ; final={final.Status}");
        }
    }

    [Fact]
    public async Task UnknownOrMalformedId_404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Ops().GetAsync($"{Admin}/{ObjectId.GenerateNewId()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Ops().GetAsync($"{Admin}/not-an-id")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Ops().PostAsJsonAsync($"{Admin}/not-an-id/approve", new { checklist = FullChecklist })).StatusCode);
    }

    // ───────────── list / detail ─────────────
    [Fact]
    public async Task List_FiltersStatus_SearchesName_OldestFirst_HidesSuperseded_AndCounts()
    {
        var (a, aid) = await SeedPending("Zed-CLEARANCETEST-LIST");
        await Task.Delay(15);
        var (b, bid) = await SeedPending("Amy-CLEARANCETEST-LIST");
        await Ops().PostAsJsonAsync($"{Admin}/{bid}/reject", new { reason = "rejected for the list test" });
        using (var db = _f.Db())   // a superseded older pending for caregiver A must not appear
        {
            db.PoliceClearances.Add(new PoliceClearance { Id = ObjectId.GenerateNewId(), CaregiverId = a.Id.ToString(), Status = PoliceClearanceStatuses.Pending, SubmittedAt = DateTime.UtcNow.AddDays(-9), Superseded = true });
            await db.SaveChangesAsync();
        }
        var all = await (await Ops().GetAsync($"{Admin}?search=CLEARANCETEST-LIST")).Content.ReadFromJsonAsync<JsonElement>();
        var ids = all.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetString()).ToList();
        Assert.Equal(new[] { aid, bid }, ids);                               // oldest first, one row per caregiver
        var pending = await (await Ops().GetAsync($"{Admin}?status=pending&search=CLEARANCETEST-LIST")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(new[] { aid }, pending.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetString()).ToArray());
        var byName = await (await Ops().GetAsync($"{Admin}?search=amy-clearancetest")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, byName.GetProperty("items").GetArrayLength());
        Assert.Equal(HttpStatusCode.BadRequest, (await Ops().GetAsync($"{Admin}?status=bogus")).StatusCode);
        var count = await (await Ops().GetAsync($"{Admin}/pending-count")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(count.GetProperty("count").GetInt32() >= 1);
    }

    [Fact]
    public async Task Detail_HasFilesChecklistHistoryNameHint_AndNoStorageIds()
    {
        var (cg, id) = await SeedPending("Ngozi", name: "Ngozi Subject");
        var res = await Ops().GetAsync($"{Admin}/{id}");
        var text = await res.Content.ReadAsStringAsync();
        var j = JsonSerializer.Deserialize<JsonElement>(text);
        Assert.Equal("likely_match", j.GetProperty("nameMatchHint").GetString());
        Assert.True(j.GetProperty("canApprove").GetBoolean());
        Assert.True(j.GetProperty("isLatest").GetBoolean());
        Assert.Equal(1, j.GetProperty("files").GetArrayLength());
        Assert.DoesNotContain("fake/", text);
        Assert.DoesNotContain("cloudinary", text, StringComparison.OrdinalIgnoreCase);

        var (_, other) = await SeedPending("Chidi", name: "Totally Different");
        var j2 = await (await Ops().GetAsync($"{Admin}/{other}")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("check_manually", j2.GetProperty("nameMatchHint").GetString());
    }

    // ───────────── staff file view (item 3) ─────────────
    [Fact]
    public async Task StaffFileView_AllFilesOpenable_EachViewAudited_OutsidersRefused()
    {
        var (cg, id) = await SeedPending(files: 3);
        var ops = Ops("viewer-admin-5", "viewer5@carepro.test");
        for (var i = 0; i < 3; i++)
        {
            var r = await ops.GetAsync($"{Admin}/{id}/files/{i}");
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.Equal("image/jpeg", r.Content.Headers.ContentType!.MediaType);
            Assert.Equal((byte)i, (await r.Content.ReadAsByteArrayAsync()).Last());           // really file i, not just page 1
        }
        Assert.Equal(HttpStatusCode.NotFound, (await ops.GetAsync($"{Admin}/{id}/files/3")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ops.GetAsync($"{Admin}/{id}/files/-1")).StatusCode);

        using var db = _f.Db();
        var logs = await db.AdminAuditLogs.Where(a => a.TargetEntityId == id && a.Action == "PoliceClearanceFileViewed").ToListAsync();
        Console.WriteLine($"[evidence] audit rows for 3 views: {string.Join(" | ", logs.OrderBy(l => l.Timestamp).Select(l => $"admin={l.AdminId} email={l.AdminEmail} target={l.TargetEntityType}:{l.TargetEntityId[..6]}… owner={l.TargetUserId![..6]}… after={l.AfterJson} at={l.Timestamp:O}"))}");
        Assert.Equal(3, logs.Count);                                                          // the two 404s wrote nothing
        Assert.All(logs, l => { Assert.Equal("viewer-admin-5", l.AdminId); Assert.Equal(cg.Id.ToString(), l.TargetUserId); });
        Assert.Equal(new[] { 0, 1, 2 }, logs.Select(l => JsonDocument.Parse(l.AfterJson!).RootElement.GetProperty("fileIndex").GetInt32()).OrderBy(x => x).ToArray());

        // refused callers write no audit row and receive no bytes
        var before = await db.AdminAuditLogs.CountAsync(a => a.Action == "PoliceClearanceFileViewed");
        foreach (var role in new[] { "Admin:Finance", "Caregiver" })
        {
            var r = await _f.Client(role, cg.Id.ToString()).GetAsync($"{Admin}/{id}/files/0");
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
            Assert.Empty(await r.Content.ReadAsByteArrayAsync() is { Length: > 100 } b ? b : Array.Empty<byte>());
        }
        Assert.Equal(before, await db.AdminAuditLogs.CountAsync(a => a.Action == "PoliceClearanceFileViewed"));
    }

    [Fact]
    public async Task OwnerCanView_OperationsAdminCanView_FinanceAndOtherCaregiverGet403()
    {
        var (cg, id) = await SeedPending();
        var other = PoliceClearanceTestSupport.SeedCaregiver(_f.Db(), "Intruder-CLEARANCETEST");
        var owner = await _f.Client("Caregiver", cg.Id.ToString()).GetAsync($"/api/caregiver/vetting/police-clearance/{id}/files/0");
        var staff = await Ops().GetAsync($"{Admin}/{id}/files/0");
        var finance = await _f.Client("Admin:Finance", "fin-1").GetAsync($"{Admin}/{id}/files/0");
        var financeViaCaregiverRoute = await _f.Client("Admin:Finance", "fin-1").GetAsync($"/api/caregiver/vetting/police-clearance/{id}/files/0");
        var intruder = await _f.Client("Caregiver", other.Id.ToString()).GetAsync($"/api/caregiver/vetting/police-clearance/{id}/files/0");
        var intruderViaAdmin = await _f.Client("Caregiver", other.Id.ToString()).GetAsync($"{Admin}/{id}/files/0");
        Console.WriteLine($"[evidence] file {id[..6]}…/0: owner={(int)owner.StatusCode} ops-admin={(int)staff.StatusCode} finance-admin={(int)finance.StatusCode} finance-on-caregiver-route={(int)financeViaCaregiverRoute.StatusCode} other-caregiver={(int)intruder.StatusCode} other-caregiver-on-admin-route={(int)intruderViaAdmin.StatusCode}");
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden),
            (owner.StatusCode, staff.StatusCode, finance.StatusCode, financeViaCaregiverRoute.StatusCode, intruder.StatusCode, intruderViaAdmin.StatusCode));
    }

    [Fact]
    public async Task StaffView_WritesAuditEvenIfStorageReadFails()
    {
        var (_, id) = await SeedPending();
        using (var db = _f.Db())
        {
            var rec = await db.PoliceClearances.FirstAsync(c => c.Id == ObjectId.Parse(id));
            rec.Files![0].PublicId = "fake/does-not-exist.pdf";
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.NotFound, (await Ops("auditor-1").GetAsync($"{Admin}/{id}/files/0")).StatusCode);
        using var check = _f.Db();
        Assert.True(await check.AdminAuditLogs.AnyAsync(a => a.TargetEntityId == id && a.AdminId == "auditor-1"));   // audit precedes the read
    }
}
