using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Application.DTOs;
using Application.Interfaces.Content;
using Domain.Entities;
using Infrastructure.Content.Data;
using Infrastructure.Content.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using MongoDB.Bson;
using Xunit;

namespace CommitmentGate.Tests;

/// <summary>Item 6: readiness (single + bulk), enforcement date, assign gate, accept re-check, revocation alert.</summary>
public class PoliceClearanceReadinessTests
{
    private const string Category = "Post Surgery Care";
    private static readonly DateTime EnforceAt = new(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc);

    private static IConfiguration Cfg(string? enforcement) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["PoliceClearance:EnforcementDate"] = enforcement }).Build();

    private static CaregiverReadinessService Readiness(CareProDbContext db, string? enforcement, FakeClock clock) =>
        new(db, new EligibilityService(db, Mock.Of<ILogger<EligibilityService>>()), Mock.Of<ILogger<CaregiverReadinessService>>(), Cfg(enforcement), clock);

    private static Caregiver ReadyCaregiver(CareProDbContext db)
    {
        var cg = new Caregiver
        {
            Id = ObjectId.GenerateNewId(), FirstName = "CLEARANCETEST", LastName = "Ready", Email = $"r{Guid.NewGuid():N}@x.com",
            Password = "x", Role = "Caregiver", Status = true, IsAvailable = true, IsIdentityVerified = true, // pragma: allowlist-secret
            CaregiverType = CaregiverType.RegisteredNurse, CreatedAt = DateTime.UtcNow
        };
        db.CareGivers.Add(cg);
        var id = cg.Id.ToString();
        for (int i = 0; i < 2; i++)
            db.Guarantors.Add(new Guarantor { Id = ObjectId.GenerateNewId(), CaregiverId = id, Name = $"G{i}", RelationshipToCaregiver = "Uncle", PhoneNo = "0800", Status = GuarantorStatuses.Confirmed });
        var now = DateTime.UtcNow;
        db.CaregiverAddressHistories.Add(new CaregiverAddressHistory { Id = ObjectId.GenerateNewId(), CaregiverId = id, Address = "A", MovedIn = now.AddYears(-6), MovedOut = now.AddYears(-3), CreatedAt = now });
        db.CaregiverAddressHistories.Add(new CaregiverAddressHistory { Id = ObjectId.GenerateNewId(), CaregiverId = id, Address = "B", MovedIn = now.AddYears(-3), MovedOut = null, ProofStatus = AddressProofStatuses.Approved, CreatedAt = now });
        return cg;
    }

    private static void Clearance(CareProDbContext db, Caregiver cg, string? status, bool superseded = false, DateTime? at = null)
    {
        if (status == null) return;
        db.PoliceClearances.Add(new PoliceClearance { Id = ObjectId.GenerateNewId(), CaregiverId = cg.Id.ToString(), Status = status, SubmittedAt = at ?? DateTime.UtcNow, Superseded = superseded });
    }

    // ───────── readiness: every state, before/after enforcement ─────────
    [Theory]
    //        status      enforcement-set  now-after?  expectReady  expectBlocking          expectInfo               expectApproved
    [InlineData(null,       true,  false, true,  null,                      "police_clearance_missing", false)]  // none, before: informational only
    [InlineData("pending",  true,  false, true,  null,                      "police_clearance_pending", false)]
    [InlineData("rejected", true,  false, true,  null,                      "police_clearance_missing", false)]
    [InlineData("revoked",  true,  false, true,  null,                      "police_clearance_missing", false)]
    [InlineData("approved", true,  false, true,  null,                      null,                       true)]
    [InlineData(null,       true,  true,  false, "police_clearance_missing", null,                      false)]  // after enforcement: blocking
    [InlineData("pending",  true,  true,  false, "police_clearance_pending", null,                      false)]
    [InlineData("rejected", true,  true,  false, "police_clearance_missing", null,                      false)]
    [InlineData("revoked",  true,  true,  false, "police_clearance_missing", null,                      false)]
    [InlineData("approved", true,  true,  true,  null,                      null,                       true)]
    [InlineData(null,       false, true,  true,  null,                      "police_clearance_missing", false)]  // enforcement UNSET: never blocks, even "now" far in future
    [InlineData("pending",  false, true,  true,  null,                      "police_clearance_pending", false)]
    public async Task Single_And_Bulk_AgreeForEveryState(string? status, bool dateSet, bool afterDate, bool expectReady, string? expectBlocking, string? expectInfo, bool expectApproved)
    {
        using var db = PoliceClearanceTestSupport.CreateDb(PoliceClearanceTestSupport.NewDbName());
        var cg = ReadyCaregiver(db);
        Clearance(db, cg, status);
        await db.SaveChangesAsync();
        var clock = new FakeClock { UtcNowValue = afterDate ? EnforceAt.AddSeconds(1) : EnforceAt.AddDays(-30) };
        if (!dateSet) clock.UtcNowValue = new DateTime(2040, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var svc = Readiness(db, dateSet ? "2026-12-01T00:00:00Z" : null, clock);

        var single = await svc.GetReadinessAsync(cg.Id.ToString(), Category, knownHasActiveGig: true);
        var bulk = (await svc.GetReadinessBulkAsync(new[] { cg.Id.ToString() }, Category))[cg.Id.ToString()];

        foreach (var r in new[] { single, bulk })
        {
            Assert.Equal(expectReady, r.IsReady);
            Assert.Equal(expectBlocking == null ? Array.Empty<string>() : new[] { expectBlocking }, r.IneligibilityReasons.Where(x => x.StartsWith("police_clearance")).ToArray());
            Assert.Equal(expectInfo == null ? Array.Empty<string>() : new[] { expectInfo }, r.InformationalReasons.ToArray());
            Assert.Equal(expectApproved, r.PoliceClearanceApproved);
            Assert.Equal(afterDate && dateSet, r.PoliceClearanceEnforced);
        }
        // the bulk method matches the single method field for field
        Assert.Equal(single.IsReady, bulk.IsReady);
        Assert.Equal(single.IneligibilityReasons, bulk.IneligibilityReasons);
        Assert.Equal(single.InformationalReasons, bulk.InformationalReasons);
        Assert.Equal(single.PoliceClearanceStatus, bulk.PoliceClearanceStatus);
    }

    [Fact]
    public async Task EnforcementDate_Boundary_And_UnparseableFailsOpen()
    {
        using var db = PoliceClearanceTestSupport.CreateDb(PoliceClearanceTestSupport.NewDbName());
        var cg = ReadyCaregiver(db); await db.SaveChangesAsync();
        var clock = new FakeClock { UtcNowValue = EnforceAt.AddTicks(-1) };
        var before = await Readiness(db, "2026-12-01T00:00:00Z", clock).GetReadinessAsync(cg.Id.ToString(), Category, true);
        Assert.True(before.IsReady);
        clock.UtcNowValue = EnforceAt;                                      // at the date: enforced
        var at = await Readiness(db, "2026-12-01T00:00:00Z", clock).GetReadinessAsync(cg.Id.ToString(), Category, true);
        Assert.False(at.IsReady);
        foreach (var junk in new[] { "garbage", "31/31/2026 99:99", "", "   " })
            Assert.True((await Readiness(db, junk, clock).GetReadinessAsync(cg.Id.ToString(), Category, true)).IsReady);   // fail open
        Assert.True((await new CaregiverReadinessService(db, new EligibilityService(db, Mock.Of<ILogger<EligibilityService>>()), Mock.Of<ILogger<CaregiverReadinessService>>()).GetReadinessAsync(cg.Id.ToString(), Category, true)).IsReady); // legacy ctor: no config = not enforced
    }

    [Fact]
    public async Task LatestNonSuperseded_Wins_OlderApprovedSupersededDoesNotCount()
    {
        using var db = PoliceClearanceTestSupport.CreateDb(PoliceClearanceTestSupport.NewDbName());
        var cg = ReadyCaregiver(db);
        Clearance(db, cg, PoliceClearanceStatuses.Approved, superseded: true, at: DateTime.UtcNow.AddDays(-5));
        Clearance(db, cg, PoliceClearanceStatuses.Pending, at: DateTime.UtcNow);
        await db.SaveChangesAsync();
        var svc = Readiness(db, "2020-01-01", new FakeClock());
        var r = await svc.GetReadinessAsync(cg.Id.ToString(), Category, true);
        Assert.Equal("pending", r.PoliceClearanceStatus);
        Assert.Contains("police_clearance_pending", r.IneligibilityReasons);
    }

    [Fact]
    public async Task Bulk_BatchesManyCaregivers_EachGetsOwnStatus()
    {
        using var db = PoliceClearanceTestSupport.CreateDb(PoliceClearanceTestSupport.NewDbName());
        var states = new[] { null, "pending", "approved", "rejected", "revoked" };
        var cgs = states.Select(s => { var c = ReadyCaregiver(db); Clearance(db, c, s); return c; }).ToList();
        await db.SaveChangesAsync();
        var svc = Readiness(db, "2020-01-01", new FakeClock());
        var bulk = await svc.GetReadinessBulkAsync(cgs.Select(c => c.Id.ToString()), Category);
        Assert.Equal(new[] { "none", "pending", "approved", "rejected", "revoked" }, cgs.Select(c => bulk[c.Id.ToString()].PoliceClearanceStatus).ToArray());
        Assert.Equal(new[] { false, false, true, false, false }, cgs.Select(c => bulk[c.Id.ToString()].IsReady).ToArray());
    }

    // ───────── assignment gate ─────────
    private static AssignmentService Assign(CareProDbContext db, string? enforcement, FakeClock clock, IOpsAlertService? ops = null) =>
        new(db, Mock.Of<IMediator>(), Mock.Of<Application.Interfaces.Email.IEmailService>(), Readiness(db, enforcement, clock),
            Mock.Of<IPackageContractService>(), ops ?? Mock.Of<IOpsAlertService>(), Mock.Of<ILogger<AssignmentService>>(), Cfg(enforcement), clock);

    private static async Task<string> NewRequest(CareProDbContext db)
    {
        var client = new Client { Id = ObjectId.GenerateNewId(), FirstName = "C", LastName = "L", Email = $"c{Guid.NewGuid():N}@x.com", Password = "x", Role = "Client", Status = true }; // pragma: allowlist-secret
        db.Clients.Add(client);
        var pr = new PackageRequest
        {
            Id = ObjectId.GenerateNewId(), ClientId = client.Id.ToString(), PackageId = ObjectId.GenerateNewId().ToString(), PackageCategory = Category,
            PackageTierLabel = "Essential Recovery", RequiredCaregiverType = CaregiverType.RegisteredNurse, ServiceCategory = Category,
            Status = PackageRequestStatuses.Pending, BillingType = PackageRequestBillingTypes.OneTime, CreatedAt = DateTime.UtcNow
        };
        db.PackageRequests.Add(pr);
        await db.SaveChangesAsync();
        return pr.Id.ToString();
    }

    [Fact]
    public async Task AssignGate_BeforeEnforcement_AllowsAnyone_AfterEnforcement_NeedsApproved()
    {
        using var db = PoliceClearanceTestSupport.CreateDb(PoliceClearanceTestSupport.NewDbName());
        var none = ReadyCaregiver(db); var pending = ReadyCaregiver(db); var approved = ReadyCaregiver(db);
        Clearance(db, pending, PoliceClearanceStatuses.Pending); Clearance(db, approved, PoliceClearanceStatuses.Approved);
        await db.SaveChangesAsync();
        var pr1 = await NewRequest(db); var pr2 = await NewRequest(db); var pr3 = await NewRequest(db); var pr4 = await NewRequest(db);

        var before = new FakeClock { UtcNowValue = EnforceAt.AddDays(-1) };
        Assert.Equal("PendingAcceptance", (await Assign(db, "2026-12-01T00:00:00Z", before).AssignAsync(pr1, none.Id.ToString(), "a", "e", "staff", null)).Status);

        var after = new FakeClock { UtcNowValue = EnforceAt.AddDays(1) };
        var e1 = await Assert.ThrowsAsync<CaregiverNotReadyException>(() => Assign(db, "2026-12-01T00:00:00Z", after).AssignAsync(pr2, none.Id.ToString(), "a", "e", "staff", null));
        Assert.Contains("police_clearance_missing", e1.Reasons);
        var e2 = await Assert.ThrowsAsync<CaregiverNotReadyException>(() => Assign(db, "2026-12-01T00:00:00Z", after).AssignAsync(pr3, pending.Id.ToString(), "a", "e", "staff", null));
        Assert.Contains("police_clearance_pending", e2.Reasons);                                 // pending ALSO blocks assignment
        Assert.Equal("PendingAcceptance", (await Assign(db, "2026-12-01T00:00:00Z", after).AssignAsync(pr4, approved.Id.ToString(), "a", "e", "staff", null)).Status);
        Console.WriteLine($"[evidence] AssignAsync after enforcement: none -> {string.Join(",", e1.Reasons)}; pending -> {string.Join(",", e2.Reasons)}; approved -> assigned");
    }

    // ───────── accept re-check ─────────
    private static async Task<(string assignmentId, string cgId, string prId)> Offered(CareProDbContext db, Caregiver cg)
    {
        var pr = await NewRequest(db);
        // Offer made in the grace period so the assign gate isn't what's being tested here.
        var a = await Assign(db, null, new FakeClock()).AssignAsync(pr, cg.Id.ToString(), "a", "e", "staff", null);
        return (a.Id, cg.Id.ToString(), pr);
    }

    [Fact]
    public async Task Accept_AfterEnforcement_WithoutApproval_Refused_AssignmentUntouched()
    {
        using var db = PoliceClearanceTestSupport.CreateDb(PoliceClearanceTestSupport.NewDbName());
        var cg = ReadyCaregiver(db); await db.SaveChangesAsync();
        var (aid, cgId, prId) = await Offered(db, cg);
        var after = new FakeClock { UtcNowValue = EnforceAt.AddDays(1) };

        var ex = await Assert.ThrowsAsync<CaregiverNotReadyException>(() => Assign(db, "2026-12-01T00:00:00Z", after).AcceptAsync(aid, cgId));
        Assert.Equal(new[] { "police_clearance_missing" }, ex.Reasons);

        var a = await db.Assignments.AsNoTracking().FirstAsync(x => x.Id == ObjectId.Parse(aid));
        var pr = await db.PackageRequests.AsNoTracking().FirstAsync(x => x.Id == ObjectId.Parse(prId));
        Assert.Equal(AssignmentStatuses.PendingAcceptance, a.Status);
        Assert.Null(a.RespondedAt);
        Assert.NotEqual(PackageRequestStatuses.Confirmed, pr.Status);
        Assert.Null(pr.ConfirmedCaregiverId);

        // pending clearance: still refused, with the "under review" reason
        Clearance(db, cg, PoliceClearanceStatuses.Pending); await db.SaveChangesAsync();
        var ex2 = await Assert.ThrowsAsync<CaregiverNotReadyException>(() => Assign(db, "2026-12-01T00:00:00Z", after).AcceptAsync(aid, cgId));
        Assert.Equal(new[] { "police_clearance_pending" }, ex2.Reasons);
    }

    [Fact]
    public async Task Accept_BeforeEnforcement_Unchanged_And_AfterEnforcement_WithApproval_Succeeds()
    {
        using var db = PoliceClearanceTestSupport.CreateDb(PoliceClearanceTestSupport.NewDbName());
        var cg = ReadyCaregiver(db); await db.SaveChangesAsync();
        var (aid, cgId, _) = await Offered(db, cg);

        var before = new FakeClock { UtcNowValue = EnforceAt.AddDays(-1) };
        var r = await Assign(db, "2026-12-01T00:00:00Z", before).AcceptAsync(aid, cgId);   // grace period: no clearance needed
        Assert.True(r.Success);

        var cg2 = ReadyCaregiver(db); Clearance(db, cg2, PoliceClearanceStatuses.Approved); await db.SaveChangesAsync();
        var (aid2, cg2Id, _) = await Offered(db, cg2);
        var after = new FakeClock { UtcNowValue = EnforceAt.AddDays(1) };
        Assert.True((await Assign(db, "2026-12-01T00:00:00Z", after).AcceptAsync(aid2, cg2Id)).Success);
    }

    [Fact]
    public async Task Accept_OtherReadinessReasons_AreStillNotChecked()
    {
        // Accept behaviour for every other readiness reason is deliberately unchanged: an identity-unverified caregiver with an
        // APPROVED clearance can still accept, because the re-check covers only the police clearance.
        using var db = PoliceClearanceTestSupport.CreateDb(PoliceClearanceTestSupport.NewDbName());
        var cg = ReadyCaregiver(db); Clearance(db, cg, PoliceClearanceStatuses.Approved); await db.SaveChangesAsync();
        var (aid, cgId, _) = await Offered(db, cg);
        cg.IsIdentityVerified = false; await db.SaveChangesAsync();
        var after = new FakeClock { UtcNowValue = EnforceAt.AddDays(1) };
        Assert.True((await Assign(db, "2026-12-01T00:00:00Z", after).AcceptAsync(aid, cgId)).Success);
    }

    // ───────── revocation / rejection alert ─────────
    private sealed class AdminRig
    {
        public CareProDbContext Db = null!;
        public Mock<IPoliceClearanceNotifier> Notifier = new();
        public FakeClock Clock = new() { UtcNowValue = EnforceAt.AddDays(1) };
        public PoliceClearanceAdminService Service = null!;
    }

    private static AdminRig AdminBuild(string? enforcement)
    {
        var r = new AdminRig { Db = PoliceClearanceTestSupport.CreateDb(PoliceClearanceTestSupport.NewDbName()) };
        r.Service = new PoliceClearanceAdminService(r.Db, new FakeClearanceFileStore(), r.Notifier.Object, Cfg(enforcement),
            Mock.Of<ILogger<PoliceClearanceAdminService>>(), r.Clock);
        return r;
    }

    private static readonly AdminIdentity Admin = new() { Id = "adm-1", Email = "adm1@x.test" };

    private static async Task<List<Assignment>> AcceptedAssignments(CareProDbContext db, Caregiver cg, int n)
    {
        var list = new List<Assignment>();
        for (var i = 0; i < n; i++)
        {
            var prId = await NewRequest(db);
            var a = new Assignment { Id = ObjectId.GenerateNewId(), PackageRequestId = prId, CaregiverId = cg.Id.ToString(), ClientId = "c", Status = AssignmentStatuses.Accepted, AssignedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow };
            db.Assignments.Add(a); list.Add(a);
        }
        db.Assignments.Add(new Assignment { Id = ObjectId.GenerateNewId(), PackageRequestId = "x", CaregiverId = cg.Id.ToString(), ClientId = "c", Status = AssignmentStatuses.Declined, AssignedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return list;
    }

    [Fact]
    public async Task Revoke_AfterEnforcement_WithActiveAssignments_AlertsAdmins_CancelsNothing()
    {
        var r = AdminBuild("2026-12-01T00:00:00Z");
        var cg = ReadyCaregiver(r.Db);
        var rec = new PoliceClearance { Id = ObjectId.GenerateNewId(), CaregiverId = cg.Id.ToString(), Status = PoliceClearanceStatuses.Approved, SubmittedAt = DateTime.UtcNow, Superseded = false };
        r.Db.PoliceClearances.Add(rec);
        await r.Db.SaveChangesAsync();
        var accepted = await AcceptedAssignments(r.Db, cg, 2);

        await r.Service.RevokeAsync(rec.Id.ToString(), Admin, "Document found to be altered.");

        r.Notifier.Verify(n => n.NotifyAssignmentsAtRiskAsync(cg.Id.ToString(), It.IsAny<string>(), "revoked", rec.Id.ToString(),
            It.Is<IReadOnlyList<string>>(l => l.Count == 2 && accepted.All(a => l.Any(line => line.Contains(a.Id.ToString())))
                                              && l.All(line => line.Contains(Category)))), Times.Once);
        using var check = r.Db;
        var statuses = await check.Assignments.Where(a => a.CaregiverId == cg.Id.ToString()).Select(a => a.Status).ToListAsync();
        Assert.Equal(2, statuses.Count(s => s == AssignmentStatuses.Accepted));                  // nothing cancelled
        Assert.DoesNotContain(AssignmentStatuses.Cancelled, statuses);
        Console.WriteLine($"[evidence] revoke after enforcement with 2 Accepted assignments -> admin alert listing both; assignment statuses afterwards: {string.Join(",", statuses)}");
    }

    [Fact]
    public async Task Reject_AfterEnforcement_WithActiveAssignments_AlsoAlerts()
    {
        var r = AdminBuild("2026-12-01T00:00:00Z");
        var cg = ReadyCaregiver(r.Db);
        var rec = new PoliceClearance { Id = ObjectId.GenerateNewId(), CaregiverId = cg.Id.ToString(), Status = PoliceClearanceStatuses.Pending, SubmittedAt = DateTime.UtcNow, Superseded = false };
        r.Db.PoliceClearances.Add(rec); await r.Db.SaveChangesAsync();
        await AcceptedAssignments(r.Db, cg, 1);
        await r.Service.RejectAsync(rec.Id.ToString(), Admin, "Photo unreadable, please retake.");
        r.Notifier.Verify(n => n.NotifyAssignmentsAtRiskAsync(cg.Id.ToString(), It.IsAny<string>(), "rejected", rec.Id.ToString(), It.Is<IReadOnlyList<string>>(l => l.Count == 1)), Times.Once);
    }

    [Fact]
    public async Task NoAlert_BeforeEnforcement_OrWhenUnset_OrWithoutActiveAssignments()
    {
        foreach (var enforcement in new[] { "2027-06-01T00:00:00Z", null, "nonsense" })
        {
            var r = AdminBuild(enforcement);
            var cg = ReadyCaregiver(r.Db);
            var rec = new PoliceClearance { Id = ObjectId.GenerateNewId(), CaregiverId = cg.Id.ToString(), Status = PoliceClearanceStatuses.Approved, SubmittedAt = DateTime.UtcNow, Superseded = false };
            r.Db.PoliceClearances.Add(rec); await r.Db.SaveChangesAsync();
            await AcceptedAssignments(r.Db, cg, 1);
            await r.Service.RevokeAsync(rec.Id.ToString(), Admin, "Revoked during the grace period.");
            r.Notifier.Verify(n => n.NotifyAssignmentsAtRiskAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()), Times.Never);
            r.Notifier.Verify(n => n.NotifyRevokedAsync(cg.Id.ToString(), rec.Id.ToString(), It.IsAny<string>()), Times.Once);   // caregiver is still told
        }
        var r2 = AdminBuild("2026-12-01T00:00:00Z");
        var cg2 = ReadyCaregiver(r2.Db);
        var rec2 = new PoliceClearance { Id = ObjectId.GenerateNewId(), CaregiverId = cg2.Id.ToString(), Status = PoliceClearanceStatuses.Approved, SubmittedAt = DateTime.UtcNow, Superseded = false };
        r2.Db.PoliceClearances.Add(rec2); await r2.Db.SaveChangesAsync();
        await r2.Service.RevokeAsync(rec2.Id.ToString(), Admin, "No assignments exist for this one.");
        r2.Notifier.Verify(n => n.NotifyAssignmentsAtRiskAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()), Times.Never);
    }

    // ───────── Brevo & matching facts (documented, asserted so they can't drift silently) ─────────
    [Fact]
    public async Task VettingScore_DivisorUnchanged_ClearanceNotCountedInMatchingScore()
    {
        using var db = PoliceClearanceTestSupport.CreateDb(PoliceClearanceTestSupport.NewDbName());
        var cg = ReadyCaregiver(db); await db.SaveChangesAsync();
        // The matching score counts five criteria (identity, guarantors, address, assessment, category certificates) — no clearance.
        var src = File.ReadAllText(FindUp("Infrastructure/Content/Services/CareRequestMatchingService.cs"));
        Assert.Contains("return met / 5.0;", src);
        Assert.DoesNotContain("PoliceClearance", src);
    }

    private static string FindUp(string relative)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, relative))) return Path.Combine(d.FullName, relative);
        throw new FileNotFoundException(relative);
    }
}

/// <summary>Item 6 over HTTP: the new reasons are in the 409 bodies, and the vetting readiness endpoint exposes the step.</summary>
public class PoliceClearanceReadinessHttpTests
{
    private const string Category = "Post Surgery Care";

    private static ClearanceHostFactory Factory(string? enforcement)
    {
        var f = new ClearanceHostFactory();
        f.ExtraConfig["PoliceClearance:EnforcementDate"] = enforcement;
        return f;
    }

    private static async Task<(Caregiver cg, string prId)> Seed(ClearanceHostFactory f, string? clearanceStatus = null)
    {
        using var db = f.Db();
        var cg = new Caregiver
        {
            Id = ObjectId.GenerateNewId(), FirstName = "CLEARANCETEST", LastName = "Http", Email = $"h{Guid.NewGuid():N}@x.com", Password = "x", Role = "Caregiver", // pragma: allowlist-secret
            Status = true, IsAvailable = true, IsIdentityVerified = true, CaregiverType = CaregiverType.RegisteredNurse, CreatedAt = DateTime.UtcNow
        };
        db.CareGivers.Add(cg);
        for (int i = 0; i < 2; i++)
            db.Guarantors.Add(new Guarantor { Id = ObjectId.GenerateNewId(), CaregiverId = cg.Id.ToString(), Name = $"G{i}", RelationshipToCaregiver = "U", PhoneNo = "0800", Status = GuarantorStatuses.Confirmed });
        var now = DateTime.UtcNow;
        db.CaregiverAddressHistories.Add(new CaregiverAddressHistory { Id = ObjectId.GenerateNewId(), CaregiverId = cg.Id.ToString(), Address = "A", MovedIn = now.AddYears(-6), MovedOut = now.AddYears(-3), CreatedAt = now });
        db.CaregiverAddressHistories.Add(new CaregiverAddressHistory { Id = ObjectId.GenerateNewId(), CaregiverId = cg.Id.ToString(), Address = "B", MovedIn = now.AddYears(-3), MovedOut = null, ProofStatus = AddressProofStatuses.Approved, CreatedAt = now });
        if (clearanceStatus != null)
            db.PoliceClearances.Add(new PoliceClearance { Id = ObjectId.GenerateNewId(), CaregiverId = cg.Id.ToString(), Status = clearanceStatus, SubmittedAt = DateTime.UtcNow, Superseded = false });
        var client = new Client { Id = ObjectId.GenerateNewId(), FirstName = "C", LastName = "L", Email = $"c{Guid.NewGuid():N}@x.com", Password = "x", Role = "Client", Status = true }; // pragma: allowlist-secret
        db.Clients.Add(client);
        var pr = new PackageRequest
        {
            Id = ObjectId.GenerateNewId(), ClientId = client.Id.ToString(), PackageId = ObjectId.GenerateNewId().ToString(), PackageCategory = Category,
            PackageTierLabel = "Essential Recovery", RequiredCaregiverType = CaregiverType.RegisteredNurse, ServiceCategory = Category,
            Status = PackageRequestStatuses.Pending, BillingType = PackageRequestBillingTypes.OneTime, CreatedAt = DateTime.UtcNow
        };
        db.PackageRequests.Add(pr);
        await db.SaveChangesAsync();
        return (cg, pr.Id.ToString());
    }

    [Fact]
    public async Task AdminAssign_409Body_ContainsTheNewReasons_WhenEnforced()
    {
        using var f = Factory("2020-01-01T00:00:00Z");
        var (cg, prId) = await Seed(f);
        var res = await f.Client("Admin:CareLeads", "ops-1", "ops@x.test").PostAsJsonAsync("/api/admin/assignments", new { packageRequestId = prId, caregiverId = cg.Id.ToString() });
        var text = await res.Content.ReadAsStringAsync();
        Console.WriteLine($"[evidence] POST /api/admin/assignments (enforced, no clearance) -> {(int)res.StatusCode} {text}");
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var j = JsonSerializer.Deserialize<JsonElement>(text);
        Assert.Equal("CAREGIVER_NOT_READY", j.GetProperty("errorCode").GetString());
        Assert.Contains("police_clearance_missing", j.GetProperty("reasons").EnumerateArray().Select(x => x.GetString()));
    }

    [Fact]
    public async Task AdminAssign_BeforeEnforcement_Succeeds_WithoutClearance()
    {
        using var f = Factory("2099-01-01T00:00:00Z");
        var (cg, prId) = await Seed(f);
        var res = await f.Client("Admin:CareLeads", "ops-1", "ops@x.test").PostAsJsonAsync("/api/admin/assignments", new { packageRequestId = prId, caregiverId = cg.Id.ToString() });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task CaregiverAccept_409_WithReasons_WhenEnforcedAndNotApproved_AssignmentUnchanged()
    {
        using var f = Factory(null);                                          // offer made in the grace period (unset = not enforced)
        var (cg, prId) = await Seed(f);
        var assign = await f.Client("Admin:CareLeads", "ops-1", "ops@x.test").PostAsJsonAsync("/api/admin/assignments", new { packageRequestId = prId, caregiverId = cg.Id.ToString() });
        Assert.Equal(HttpStatusCode.OK, assign.StatusCode);
        var aid = (await assign.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("id").GetString()!;

        using var enforced = Factory("2020-01-01T00:00:00Z");
        enforced.DbNameOverride = f.DbName;                                   // same database, different configuration
        var res = await enforced.Client("Caregiver", cg.Id.ToString()).PostAsync($"/api/caregiver/assignments/{aid}/accept", null);
        var text = await res.Content.ReadAsStringAsync();
        Console.WriteLine($"[evidence] POST /api/caregiver/assignments/{{id}}/accept (enforced, no clearance) -> {(int)res.StatusCode} {text}");
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var j = JsonSerializer.Deserialize<JsonElement>(text);
        Assert.Equal("CAREGIVER_NOT_READY", j.GetProperty("errorCode").GetString());
        Assert.Equal("police_clearance_missing", j.GetProperty("reasons")[0].GetString());
        using var db = f.Db();
        Assert.Equal(AssignmentStatuses.PendingAcceptance, (await db.Assignments.FirstAsync(a => a.Id == ObjectId.Parse(aid))).Status);
    }

    [Fact]
    public async Task VettingReadiness_ExposesTheStep_Informational_BeforeEnforcement()
    {
        using var f = Factory("2099-01-01T00:00:00Z");
        var (cg, _) = await Seed(f);
        var res = await f.Client("Caregiver", cg.Id.ToString()).GetAsync("/api/caregiver/vetting/readiness");
        var j = await res.Content.ReadFromJsonAsync<JsonElement>();
        Console.WriteLine($"[evidence] GET /api/caregiver/vetting/readiness (before enforcement) -> {j}");
        Assert.True(j.GetProperty("isReady").GetBoolean());                    // informational only
        Assert.Equal("none", j.GetProperty("policeClearanceStatus").GetString());
        Assert.False(j.GetProperty("policeClearanceEnforced").GetBoolean());
        Assert.Contains("police_clearance_missing", j.GetProperty("informationalReasons").EnumerateArray().Select(x => x.GetString()));
        Assert.DoesNotContain("police_clearance_missing", j.GetProperty("ineligibilityReasons").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(2099, j.GetProperty("policeClearanceEnforcementDate").GetDateTime().Year);
    }
}
