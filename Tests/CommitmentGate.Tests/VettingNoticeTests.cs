using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Commands;
using Application.DTOs;
using Application.Interfaces.Content;
using Application.Interfaces.Email;
using Domain.Entities;
using Infrastructure.Content.Data;
using Infrastructure.Content.Helpers;
using Infrastructure.Content.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using MongoDB.Bson;
using Xunit;
using AdminVettingNoticeControllerMarker = CarePro_Api.Controllers.Content.AdminVettingNoticeController;

namespace CommitmentGate.Tests;

public class VettingNoticeTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
    private const string Lockout = "2026-10-19T00:00:00Z";
    private const string Clearance = "2026-11-02T00:00:00Z";
    private static readonly AdminIdentity Admin = new() { Id = "adm-9", Email = "ops@carepro.test" };

    private sealed class Rig
    {
        public CareProDbContext Db = null!; public string DbName = "";
        public Mock<IEmailService> Email = new(); public Mock<IMediator> Mediator = new();
        public List<(string to, string subject, string html, string text)> Sent = new();
        public List<SendNotificationCommand> InApp = new();
        public List<TimeSpan> Delays = new();
        public Func<string, bool> FailFor = _ => false;
        public IConfiguration Config = null!;
        public FakeClock Clock = new() { UtcNowValue = Now };

        public VettingNoticeService Service(CareProDbContext? db = null) =>
            new(db ?? Db, Email.Object, Mediator.Object, Config, Mock.Of<ILogger<VettingNoticeService>>(), Clock, ts => { Delays.Add(ts); return Task.CompletedTask; });
    }

    private static Rig Build(string? lockout = Lockout, string? clearance = null, int batch = 20)
    {
        var r = new Rig { DbName = PoliceClearanceTestSupport.NewDbName() };
        r.Db = PoliceClearanceTestSupport.CreateDb(r.DbName);
        r.Config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["VettingLockout:EnforcementDate"] = lockout, ["PoliceClearance:EnforcementDate"] = clearance,
            ["VettingNotice:BatchSize"] = batch.ToString(), ["VettingNotice:BatchDelayMs"] = "250", ["FrontendUrl"] = "http://localhost:5173"
        }).Build();
        r.Email.Setup(e => e.SendVettingNoticeEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns<string, string, string, string>((to, s, h, t) =>
            {
                if (r.FailFor(to)) throw new InvalidOperationException($"smtp refused {to}");
                lock (r.Sent) r.Sent.Add((to, s, h, t)); return Task.CompletedTask;
            });
        r.Mediator.Setup(m => m.Send(It.IsAny<SendNotificationCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<string>, CancellationToken>((c, _) => { lock (r.InApp) r.InApp.Add((SendNotificationCommand)c); }).ReturnsAsync("n");
        return r;
    }

    private static Caregiver Cg(CareProDbContext db, string tag, bool type = true, int guarantors = 2, bool address = true, string? clearance = null,
        bool deleted = false, bool active = true, string? email = "default")
    {
        var cg = new Caregiver
        {
            Id = ObjectId.GenerateNewId(), FirstName = $"NOTICETEST-{tag}", LastName = "X", Email = email == "default" ? $"noticetest-{tag}-{Guid.NewGuid():N}@example.com" : email!,
            Password = "x", Role = "Caregiver", Status = active, IsDeleted = deleted, IsAvailable = true, CreatedAt = DateTime.UtcNow, // pragma: allowlist-secret
            CaregiverType = type ? CaregiverType.RegisteredNurse : null
        };
        db.CareGivers.Add(cg);
        var id = cg.Id.ToString();
        for (var i = 0; i < guarantors; i++) db.Guarantors.Add(new Guarantor { Id = ObjectId.GenerateNewId(), CaregiverId = id, Name = $"G{i}", RelationshipToCaregiver = "U", PhoneNo = "1", Status = GuarantorStatuses.Pending });
        if (address)
        {
            db.CaregiverAddressHistories.Add(new CaregiverAddressHistory { Id = ObjectId.GenerateNewId(), CaregiverId = id, Address = "A", MovedIn = Now.AddYears(-6), MovedOut = Now.AddYears(-3), CreatedAt = Now });
            db.CaregiverAddressHistories.Add(new CaregiverAddressHistory { Id = ObjectId.GenerateNewId(), CaregiverId = id, Address = "B", MovedIn = Now.AddYears(-3), MovedOut = null, ProofStatus = AddressProofStatuses.Approved, CreatedAt = Now });
        }
        if (clearance != null) db.PoliceClearances.Add(new PoliceClearance { Id = ObjectId.GenerateNewId(), CaregiverId = id, Status = clearance, SubmittedAt = Now, Superseded = false });
        return cg;
    }

    // ───────────── recipient selection ─────────────
    [Fact]
    public async Task Selection_PerStep_CompleteDeletedDeactivatedAndNoEmail_Excluded()
    {
        var r = Build();
        var oneStep = Cg(r.Db, "one", type: false);
        var threeSteps = Cg(r.Db, "three", type: false, guarantors: 1, address: false);
        Cg(r.Db, "complete");
        Cg(r.Db, "deleted", type: false, deleted: true);
        Cg(r.Db, "suspended", type: false, active: false);
        Cg(r.Db, "noemail", type: false, email: "");
        Cg(r.Db, "anon", type: false, email: "deleted@carepro.invalid");
        await r.Db.SaveChangesAsync();

        var p = await r.Service().PreviewAsync("initial");
        Console.WriteLine($"[evidence] preview: recipients={p.RecipientCount} byStep={string.Join(",", p.ByStep.Select(k => k.Key + "=" + k.Value))} sample={string.Join(" | ", p.Sample.Select(s => s.FirstName + ":" + string.Join("+", s.MissingSteps)))}");
        Assert.Equal(2, p.RecipientCount);
        Assert.Equal(new[] { "NOTICETEST-one", "NOTICETEST-three" }, p.Sample.Select(s => s.FirstName).OrderBy(x => x == "NOTICETEST-one" ? 0 : 1).ToArray());
        Assert.Equal(new[] { "classification" }, p.Sample.Single(s => s.FirstName == "NOTICETEST-one").MissingSteps);
        Assert.Equal(new[] { "classification", "guarantors", "address-history" }, p.Sample.Single(s => s.FirstName == "NOTICETEST-three").MissingSteps);
    }

    [Theory]
    //  lockout  clearance  expect missing for [no-type/0-guarantors/no-address/no-clearance] caregiver
    [InlineData(true, false, "classification,guarantors,address-history")]
    [InlineData(false, true, "police-clearance")]
    [InlineData(true, true, "classification,guarantors,address-history,police-clearance")]
    public async Task Selection_PerDateCombination(bool lockoutSet, bool clearanceSet, string expected)
    {
        var r = Build(lockoutSet ? Lockout : null, clearanceSet ? Clearance : null);
        Cg(r.Db, "bare", type: false, guarantors: 0, address: false); await r.Db.SaveChangesAsync();
        var p = await r.Service().PreviewAsync("initial");
        Assert.True(p.CanSend);
        Assert.Equal(expected.Split(','), p.Sample.Single().MissingSteps);
    }

    [Fact]
    public async Task ClearanceOnly_CaregiverMissingOtherSteps_IsNotNoticedAboutThem()
    {
        var r = Build(null, Clearance);
        Cg(r.Db, "profileGaps", type: false, guarantors: 0, address: false, clearance: "pending"); await r.Db.SaveChangesAsync();
        Assert.Equal(0, (await r.Service().PreviewAsync("initial")).RecipientCount);   // lockout date unset => the three profile steps don't apply; clearance submitted
    }

    [Theory]
    [InlineData("pending", false)] [InlineData("approved", false)]
    [InlineData("rejected", true)] [InlineData("revoked", true)] [InlineData(null, true)]
    public async Task ClearanceStatus_PendingAndApprovedCountAsSubmitted(string? status, bool noticed)
    {
        var r = Build(null, Clearance);
        Cg(r.Db, "c", clearance: status); await r.Db.SaveChangesAsync();
        Assert.Equal(noticed ? 1 : 0, (await r.Service().PreviewAsync("initial")).RecipientCount);
    }

    [Fact]
    public async Task NeitherDateSet_OrDeadlinePassed_CannotSend()
    {
        var none = Build(null, null);
        Cg(none.Db, "x", type: false); await none.Db.SaveChangesAsync();
        var p = await none.Service().PreviewAsync("initial");
        Assert.False(p.CanSend); Assert.Equal(0, p.RecipientCount);
        await Assert.ThrowsAsync<VettingNoticeConflictException>(() => none.Service().SendAsync("initial", 0, false, Admin));

        var past = Build("2026-10-01T00:00:00Z", null);
        Cg(past.Db, "x", type: false); await past.Db.SaveChangesAsync();
        Assert.False((await past.Service().PreviewAsync("initial")).CanSend);
        await Assert.ThrowsAsync<VettingNoticeConflictException>(() => past.Service().SendAsync("initial", 1, false, Admin));
        Assert.Empty(past.Sent);
        var junk = Build("not a date", "also junk");
        Assert.False((await junk.Service().PreviewAsync("initial")).CanSend);
    }

    [Fact]
    public async Task AddressCoverage_UsesTheServerRule_NotAnApproximation()
    {
        // The earlier mongosh estimate said "complete" for a single record that spans 6 years. The SERVER rule needs at least two
        // records and tolerates a 31-day gap — this is the example caregiver where the two differ.
        var r = Build();
        var single = Cg(r.Db, "singleRecord", address: false);
        r.Db.CaregiverAddressHistories.Add(new CaregiverAddressHistory { Id = ObjectId.GenerateNewId(), CaregiverId = single.Id.ToString(), Address = "Only", MovedIn = Now.AddYears(-6), MovedOut = null, CreatedAt = Now });
        var smallGap = Cg(r.Db, "gap20d", address: false);
        r.Db.CaregiverAddressHistories.Add(new CaregiverAddressHistory { Id = ObjectId.GenerateNewId(), CaregiverId = smallGap.Id.ToString(), Address = "A", MovedIn = Now.AddYears(-6), MovedOut = Now.AddYears(-3), CreatedAt = Now });
        r.Db.CaregiverAddressHistories.Add(new CaregiverAddressHistory { Id = ObjectId.GenerateNewId(), CaregiverId = smallGap.Id.ToString(), Address = "B", MovedIn = Now.AddYears(-3).AddDays(20), MovedOut = null, ProofStatus = AddressProofStatuses.Approved, CreatedAt = Now });
        var bigGap = Cg(r.Db, "gap60d", address: false);
        r.Db.CaregiverAddressHistories.Add(new CaregiverAddressHistory { Id = ObjectId.GenerateNewId(), CaregiverId = bigGap.Id.ToString(), Address = "A", MovedIn = Now.AddYears(-6), MovedOut = Now.AddYears(-3), CreatedAt = Now });
        r.Db.CaregiverAddressHistories.Add(new CaregiverAddressHistory { Id = ObjectId.GenerateNewId(), CaregiverId = bigGap.Id.ToString(), Address = "B", MovedIn = Now.AddYears(-3).AddDays(60), MovedOut = null, ProofStatus = AddressProofStatuses.Approved, CreatedAt = Now });
        await r.Db.SaveChangesAsync();
        var p = await r.Service().PreviewAsync("initial");
        Assert.Equal(new[] { "NOTICETEST-gap60d", "NOTICETEST-singleRecord" }, p.Sample.Select(s => s.FirstName).OrderBy(x => x).ToArray());
        Assert.All(p.Sample, s => Assert.Equal(new[] { "address-history" }, s.MissingSteps));
        Console.WriteLine("[evidence] server rule: single 6-year record => INCOMPLETE (needs >=2 records); 20-day gap => complete; 60-day gap => incomplete");
    }

    // ───────────── confirmation, once-only, retry ─────────────
    [Fact]
    public async Task WrongConfirmationCount_409_NothingSent()
    {
        var r = Build(); Cg(r.Db, "a", type: false); Cg(r.Db, "b", type: false); await r.Db.SaveChangesAsync();
        var ex = await Assert.ThrowsAsync<VettingNoticeConflictException>(() => r.Service().SendAsync("initial", 5, false, Admin));
        Console.WriteLine("[evidence] wrong count -> " + ex.Message);
        Assert.Empty(r.Sent); Assert.Empty(await r.Db.VettingNotices.ToListAsync());
    }

    [Fact]
    public async Task Send_EmailsEachOnce_RecordsRows_InAppToo_AuditEntry_RepeatSendsNothing()
    {
        var r = Build(); var a = Cg(r.Db, "a", type: false); var b = Cg(r.Db, "b", guarantors: 0); Cg(r.Db, "done"); await r.Db.SaveChangesAsync();
        var res = await r.Service().SendAsync("initial", 2, false, Admin);
        Assert.Equal((2, 2, 0, 0), (res.Attempted, res.Sent, res.Failed, res.Skipped));
        Assert.Equal(2, r.Sent.Count);
        Assert.Contains(r.Sent, m => m.subject == "Action needed: complete your CarePro vetting by 19 October 2026" && m.text.Contains("Dear NOTICETEST-a,") && m.text.Contains("Set your caregiver type") && !m.text.Contains("guarantors"));
        Assert.Contains(r.Sent, m => m.text.Contains("Dear NOTICETEST-b,") && m.text.Contains("Add two guarantors") && !m.text.Contains("caregiver type"));
        Assert.All(r.Sent, m => Assert.Contains("http://localhost:5173/app/caregiver/vetting", m.html));
        Assert.Equal(2, r.InApp.Count);
        Assert.All(r.InApp, n => { Assert.Equal("vetting_enforcement_notice", n.Type); Assert.Equal("initial", n.RelatedEntityId); Assert.Equal("system", n.SenderId); });
        Assert.Equal(new[] { a.Id.ToString(), b.Id.ToString() }.OrderBy(x => x), r.InApp.Select(n => n.RecipientId).OrderBy(x => x));

        using var check = PoliceClearanceTestSupport.CreateDb(r.DbName);
        var rows = await check.VettingNotices.ToListAsync();
        Assert.Equal(2, rows.Count); Assert.All(rows, n => { Assert.Equal("sent", n.Result); Assert.NotNull(n.SentAt); Assert.Equal("L202610190000-Cnone", n.DateKey); Assert.Equal($"{n.CaregiverId}|initial|{n.DateKey}", n.Id); });
        var audit = await check.AdminAuditLogs.SingleAsync(x => x.Action == "VettingNoticeSent");
        Assert.Equal("adm-9", audit.AdminId); Assert.DoesNotContain("@", audit.AfterJson); Assert.Contains("\"sent\":2", audit.AfterJson);

        // repeat: nothing left to send
        Assert.Equal(0, (await r.Service().PreviewAsync("initial")).RecipientCount);
        Assert.Equal(2, (await r.Service().PreviewAsync("initial")).AlreadySentCount);
        var again = await r.Service().SendAsync("initial", 0, false, Admin);
        Assert.Equal((0, 0, 2), (again.Attempted, again.Sent, again.Skipped));
        Assert.Equal(2, r.Sent.Count); Assert.Equal(2, r.InApp.Count);
        Console.WriteLine("[evidence] send #1: sent=2; repeat preview: recipients=0 alreadySent=2; send #2: sent=0 — mails total " + r.Sent.Count);
    }

    [Fact]
    public async Task ChangedEnforcementDate_AllowsANewNotice()
    {
        var r = Build(); Cg(r.Db, "a", type: false); await r.Db.SaveChangesAsync();
        await r.Service().SendAsync("initial", 1, false, Admin);
        r.Config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["VettingLockout:EnforcementDate"] = "2026-10-26T00:00:00Z", ["FrontendUrl"] = "http://localhost:5173" }).Build();
        var p = await r.Service().PreviewAsync("initial");
        Assert.Equal(1, p.RecipientCount);                                         // new date snapshot => new notice
        await r.Service().SendAsync("initial", 1, false, Admin);
        Assert.Equal(2, r.Sent.Count);
        Assert.Contains("26 October 2026", r.Sent[1].subject);
        using var check = PoliceClearanceTestSupport.CreateDb(r.DbName);
        Assert.Equal(2, await check.VettingNotices.CountAsync());
    }

    [Fact]
    public async Task ConcurrentSends_NeverDoubleSend()
    {
        for (var round = 0; round < 3; round++)
        {
            var r = Build();
            for (var i = 0; i < 12; i++) Cg(r.Db, $"c{i}", type: false);
            await r.Db.SaveChangesAsync();
            var dbs = Enumerable.Range(0, 4).Select(_ => PoliceClearanceTestSupport.CreateDb(r.DbName)).ToList();
            var results = await Task.WhenAll(dbs.Select(d => Task.Run(async () =>
            {
                try { return await r.Service(d).SendAsync("initial", 12, false, Admin); } catch (VettingNoticeConflictException) { return null; }
            })));
            var perAddress = r.Sent.GroupBy(s => s.to).Select(g => g.Count()).ToList();
            Assert.Equal(12, perAddress.Count);
            Assert.All(perAddress, c => Assert.Equal(1, c));                               // each caregiver emailed exactly once
            Assert.Equal(12, r.InApp.Count);
            using var check = PoliceClearanceTestSupport.CreateDb(r.DbName);
            Assert.Equal(12, await check.VettingNotices.CountAsync());
            if (round == 0) Console.WriteLine($"[evidence] 4 simultaneous sends of 12: emails={r.Sent.Count} unique recipients={perAddress.Count} results={string.Join(",", results.Select(x => x == null ? "409" : $"sent{x.Sent}/skip{x.Skipped}"))}");
        }
    }

    [Fact]
    public async Task OneBadAddress_DoesNotStopTheBatch_FailureRecordedWithoutPersonalData_RetryOnlyOnExplicitRequest()
    {
        var r = Build();
        var bad = Cg(r.Db, "bad", type: false); for (var i = 0; i < 4; i++) Cg(r.Db, $"ok{i}", type: false);
        await r.Db.SaveChangesAsync();
        r.FailFor = to => to == bad.Email;
        var res = await r.Service().SendAsync("initial", 5, false, Admin);
        Assert.Equal((5, 4, 1), (res.Attempted, res.Sent, res.Failed));
        Assert.Equal(4, r.InApp.Count);                                               // no in-app for the failed email
        using var check = PoliceClearanceTestSupport.CreateDb(r.DbName);
        var failedRow = await check.VettingNotices.SingleAsync(n => n.Result == "failed");
        Assert.Equal("InvalidOperationException", failedRow.Error);
        Assert.DoesNotContain("@", failedRow.Error); Assert.DoesNotContain("smtp", failedRow.Error);

        // a plain repeat does NOT retry the failed one; the preview reports it
        var p = await r.Service().PreviewAsync("initial");
        Assert.Equal((0, 4, 1), (p.RecipientCount, p.AlreadySentCount, p.FailedCount));
        Assert.Equal(0, (await r.Service().SendAsync("initial", 0, false, Admin)).Attempted);

        // explicit admin retry
        r.FailFor = _ => false;
        var pr = await r.Service().PreviewAsync("initial", retryFailed: true);
        Assert.Equal(1, pr.RecipientCount);
        var retry = await r.Service().SendAsync("initial", 1, true, Admin);
        Assert.Equal((1, 1, 0), (retry.Attempted, retry.Sent, retry.Failed));
        Assert.Equal(5, r.Sent.Count);
        using var after = PoliceClearanceTestSupport.CreateDb(r.DbName);
        var row = await after.VettingNotices.SingleAsync(n => n.CaregiverId == bad.Id.ToString());
        Assert.Equal(("sent", 2), (row.Result, row.Attempts));
    }

    [Fact]
    public async Task Batches_RespectSizeAndDelay()
    {
        var r = Build(batch: 2);
        for (var i = 0; i < 5; i++) Cg(r.Db, $"b{i}", type: false);
        await r.Db.SaveChangesAsync();
        await r.Service().SendAsync("initial", 5, false, Admin);
        Assert.Equal(2, r.Delays.Count);                                              // between batches 2|2|1
        Assert.All(r.Delays, d => Assert.Equal(TimeSpan.FromMilliseconds(250), d));
    }

    // ───────────── reminders re-evaluate ─────────────
    [Fact]
    public async Task Reminder_ReEvaluates_CompletedCaregiversAreNotReminded_AndEachTypeIsIndependent()
    {
        var r = Build();
        var finishes = Cg(r.Db, "finishes", type: false);
        var stillMissing = Cg(r.Db, "still", guarantors: 0);
        await r.Db.SaveChangesAsync();
        await r.Service().SendAsync("initial", 2, false, Admin);
        Assert.Equal(2, r.Sent.Count);

        // one caregiver completes their only missing step
        var tracked = await r.Db.CareGivers.FirstAsync(c => c.Id == finishes.Id); tracked.CaregiverType = CaregiverType.RegisteredNurse; await r.Db.SaveChangesAsync();
        r.Clock.UtcNowValue = new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc);

        var p = await r.Service().PreviewAsync("reminder_7d");
        Assert.Equal(1, p.RecipientCount);                                            // only the one still missing, and 'initial' rows don't block a reminder
        await Assert.ThrowsAsync<VettingNoticeConflictException>(() => r.Service().SendAsync("reminder_7d", 2, false, Admin));
        await r.Service().SendAsync("reminder_7d", 1, false, Admin);
        Assert.Equal(3, r.Sent.Count);
        Assert.Equal("Reminder: 7 days left to complete your CarePro vetting", r.Sent[2].subject);
        Assert.Contains("Dear NOTICETEST-still,", r.Sent[2].text);
        Assert.Equal(1, (await r.Service().PreviewAsync("reminder_1d")).RecipientCount);
        Assert.Equal(0, (await r.Service().PreviewAsync("reminder_7d")).RecipientCount);   // 7d already sent
    }

    [Fact]
    public async Task InvalidType_400()
    {
        var r = Build();
        await Assert.ThrowsAsync<VettingNoticeValidationException>(() => r.Service().PreviewAsync("weekly"));
        await Assert.ThrowsAsync<VettingNoticeValidationException>(() => r.Service().SendAsync("", 0, false, Admin));
    }

    // ───────────── never automatic ─────────────
    [Fact]
    public void NoHostedServiceOrTimer_ReferencesTheNoticeService()
    {
        var types = typeof(VettingNoticeService).Assembly.GetTypes().Concat(typeof(AdminVettingNoticeControllerMarker).Assembly.GetTypes());
        foreach (var t in types.Where(t => typeof(IHostedService).IsAssignableFrom(t) && !t.IsAbstract))
        {
            var deps = t.GetConstructors().SelectMany(c => c.GetParameters()).Select(p => p.ParameterType).Concat(t.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Select(f => f.FieldType));
            Assert.DoesNotContain(deps, d => d == typeof(IVettingNoticeService) || d == typeof(VettingNoticeService));
        }
        var callers = typeof(AdminVettingNoticeControllerMarker).Assembly.GetTypes().Where(t => t.GetConstructors().Any(c => c.GetParameters().Any(p => p.ParameterType == typeof(IVettingNoticeService)))).Select(t => t.Name).ToList();
        Assert.Equal(new[] { "AdminVettingNoticeController" }, callers);              // the ONLY consumer is the admin controller
    }
}

/// <summary>§2 over HTTP: roles, confirmation, no addresses in the preview, audit — through the real pipeline and a captured fake mailer.</summary>
public class VettingNoticeHttpTests
{
    private const string Route = "/api/admin/vetting-notice";

    private static ClearanceHostFactory Factory(string lockout = "2026-10-19T00:00:00Z", string? clearance = null)
    {
        var f = new ClearanceHostFactory();
        f.ExtraConfig["VettingLockout:EnforcementDate"] = DateTime.UtcNow.AddDays(9).ToString("yyyy-MM-ddTHH:mm:ssZ");
        if (clearance != null) f.ExtraConfig["PoliceClearance:EnforcementDate"] = clearance;
        return f;
    }

    private static async Task SeedThree(ClearanceHostFactory f)
    {
        using var db = f.Db();
        var now = DateTime.UtcNow;
        void Cg(string tag, bool type, int g, bool addr)
        {
            var c = new Caregiver { Id = ObjectId.GenerateNewId(), FirstName = $"NOTICETEST-{tag}", LastName = "H", Email = $"noticetest-{tag}@example.com", Password = "x", Role = "Caregiver", Status = true, IsAvailable = true, CreatedAt = now, CaregiverType = type ? CaregiverType.RegisteredNurse : null }; // pragma: allowlist-secret
            db.CareGivers.Add(c);
            for (var i = 0; i < g; i++) db.Guarantors.Add(new Guarantor { Id = ObjectId.GenerateNewId(), CaregiverId = c.Id.ToString(), Name = "G", RelationshipToCaregiver = "U", PhoneNo = "1", Status = GuarantorStatuses.Pending });
            if (addr) { db.CaregiverAddressHistories.Add(new CaregiverAddressHistory { Id = ObjectId.GenerateNewId(), CaregiverId = c.Id.ToString(), Address = "A", MovedIn = now.AddYears(-6), MovedOut = now.AddYears(-3), CreatedAt = now }); db.CaregiverAddressHistories.Add(new CaregiverAddressHistory { Id = ObjectId.GenerateNewId(), CaregiverId = c.Id.ToString(), Address = "B", MovedIn = now.AddYears(-3), MovedOut = null, ProofStatus = AddressProofStatuses.Approved, CreatedAt = now }); }
        }
        Cg("missing1", false, 2, true); Cg("missing3", false, 0, false); Cg("complete", true, 2, true);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Roles_Preview_Send_Confirmation_Audit()
    {
        using var f = Factory(); await SeedThree(f);
        var sent = new List<(string to, string subject)>();
        f.Email.Setup(e => e.SendVettingNoticeEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string, string, string>((to, s, h, t) => sent.Add((to, s))).Returns(Task.CompletedTask);

        var body = JsonContent.Create(new { type = "initial" });
        foreach (var url in new[] { $"{Route}/preview", $"{Route}/send" })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await f.Client().PostAsync(url, JsonContent.Create(new { type = "initial", confirmRecipientCount = 2 }))).StatusCode);
            foreach (var role in new[] { "Caregiver", "Client", "Admin:Finance", "Admin:MarketingAndSales" })
                Assert.Equal(HttpStatusCode.Forbidden, (await f.Client(role, "u1").PostAsync(url, JsonContent.Create(new { type = "initial", confirmRecipientCount = 2 }))).StatusCode);
        }
        Console.WriteLine("[evidence] preview+send: no token=401; Caregiver/Client/Finance/Marketing=403");
        Assert.Empty(sent);

        var ops = f.Client("Admin:HR", "hr-77", "hr77@carepro.test");
        var pv = await ops.PostAsync($"{Route}/preview", JsonContent.Create(new { type = "initial" }));
        var text = await pv.Content.ReadAsStringAsync();
        Console.WriteLine("[evidence] preview body: " + text);
        Assert.Equal(HttpStatusCode.OK, pv.StatusCode);
        Assert.DoesNotContain("@", text);                                        // no email addresses in the preview
        var j = JsonSerializer.Deserialize<JsonElement>(text);
        Assert.Equal(2, j.GetProperty("recipientCount").GetInt32());
        Assert.Equal(2, j.GetProperty("sample").GetArrayLength());

        Assert.Equal(HttpStatusCode.BadRequest, (await ops.PostAsync($"{Route}/send", JsonContent.Create(new { type = "initial" }))).StatusCode);                       // confirmation required
        var wrong = await ops.PostAsync($"{Route}/send", JsonContent.Create(new { type = "initial", confirmRecipientCount = 99 }));
        Console.WriteLine($"[evidence] send with wrong count -> {(int)wrong.StatusCode} {await wrong.Content.ReadAsStringAsync()}");
        Assert.Equal(HttpStatusCode.Conflict, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ops.PostAsync($"{Route}/preview", JsonContent.Create(new { type = "weekly" }))).StatusCode);
        Assert.Empty(sent);

        var ok = await ops.PostAsync($"{Route}/send", JsonContent.Create(new { type = "initial", confirmRecipientCount = 2, adminId = "FORGED" }));
        Console.WriteLine($"[evidence] send -> {(int)ok.StatusCode} {await ok.Content.ReadAsStringAsync()}");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(new[] { "noticetest-missing1@example.com", "noticetest-missing3@example.com" }, sent.Select(s => s.to).OrderBy(x => x));

        var repeat = await ops.PostAsync($"{Route}/send", JsonContent.Create(new { type = "initial", confirmRecipientCount = 0 }));
        Assert.Equal(0, (await repeat.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sent").GetInt32());
        Assert.Equal(2, sent.Count);

        using var db = f.Db();
        var audit = await db.AdminAuditLogs.Where(a => a.Action == "VettingNoticeSent").OrderBy(a => a.Timestamp).ToListAsync();
        Assert.Equal(2, audit.Count);
        Assert.All(audit, a => { Assert.Equal("hr-77", a.AdminId); Assert.Equal("hr77@carepro.test", a.AdminEmail); });
        Console.WriteLine($"[evidence] audit rows: {string.Join(" | ", audit.Select(a => a.AfterJson))}");
        Assert.True(await db.Notifications.CountAsync(n => n.Type == "vetting_enforcement_notice") == 2);
    }
}
