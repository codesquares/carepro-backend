using Application.Commands;
using Application.DTOs;
using Application.Interfaces.Content;
using Application.Interfaces.Email;
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
/// Failure injection for the accept / decline / cancel / assign split (status claim first, follow-on work after).
/// Real local MongoDB. Each test throws at one step and reports the state it leaves, then proves the retry / sweeper completes it
/// without repeating anything.
/// </summary>
public class AssignmentHalfDoneTests
{
    /// <summary>Throws once, from SaveChanges, when the predicate matches the pending changes.</summary>
    internal sealed class FaultDb : CareProDbContext
    {
        public Func<FaultDb, bool>? ThrowOnceWhen;
        public FaultDb(DbContextOptions<CareProDbContext> o) : base(o) { }
        protected override void OnModelCreating(ModelBuilder b)
        {
            base.OnModelCreating(b);
            b.Entity<WebhookLog>().Ignore(x => x.Headers);
            b.Ignore<AssessmentQuestion>();
        }
        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
        {
            if (ThrowOnceWhen != null && ThrowOnceWhen(this)) { ThrowOnceWhen = null; throw new InvalidOperationException("injected failure"); }
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
        }
        public bool Modifying<T>(EntityState s = EntityState.Modified) where T : class => ChangeTracker.Entries<T>().Any(e => e.State == s);
    }

    /// <summary>Stand-in contract generator: inserts a Contract row on success, can be told to fail.</summary>
    private sealed class FakeContracts
    {
        private readonly string _db;
        public int FailTimes;
        public int Calls;
        public readonly Mock<IPackageContractService> Mock = new();
        public FakeContracts(string db)
        {
            _db = db;
            Mock.Setup(c => c.GenerateForConfirmedRequestAsync(It.IsAny<string>())).Returns((string id) => GenerateAsync(id));
        }
        private async Task<PackageContractDTO> GenerateAsync(string packageRequestId)
        {
            Calls++;
            if (FailTimes > 0) { FailTimes--; throw new InvalidOperationException("injected contract failure"); }
            using var db = Phase4AssignmentTests.CreateDb(_db);
            if (!await db.Contracts.AnyAsync(c => c.PackageRequestId == packageRequestId))
            {
                db.Contracts.Add(new Contract { PackageRequestId = packageRequestId });
                await db.SaveChangesAsync();
            }
            return new PackageContractDTO();
        }
    }

    private sealed class Rig
    {
        public required string DbName { get; init; }
        public required FaultDb Db { get; set; }
        public required AssignmentService Svc { get; set; }
        public Func<FaultDb, AssignmentService>? Rebuild;
        /// <summary>A brand-new request-scoped context/service over the same database and the same mocks (what a retry really is).</summary>
        public void Renew() { Db = NewFaultDb(DbName); Svc = Rebuild!(Db); }
        public required FakeContracts Contracts { get; init; }
        public required Mock<IOpsAlertService> Ops { get; init; }
        public List<SendNotificationCommand> Sent { get; } = new();
        public int FailNextSend;
        public int Emails;
        public int Count(string type) => Sent.Count(c => c.Type == type);
        public int OpsCount(string type) => Ops.Invocations.Count(i => i.Method.Name == "NotifyAdminsAsync" && (string)i.Arguments[0] == type);
    }

    private static FaultDb NewFaultDb(string dbName) =>
        new(new DbContextOptionsBuilder<CareProDbContext>().UseMongoDB("mongodb://127.0.0.1:27018", dbName).Options);

    private static Rig NewRig(string dbName)
    {
        var db = NewFaultDb(dbName);
        var contracts = new FakeContracts(dbName);
        var ops = new Mock<IOpsAlertService>();
        var email = new Mock<IEmailService>();
        var mediator = new Mock<IMediator>();
        var readiness = new Mock<ICaregiverReadinessService>();
        readiness.Setup(r => r.GetReadinessAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool?>()))
            .ReturnsAsync(new CaregiverReadinessResult { IsReady = true });
        var rig = new Rig
        {
            DbName = dbName, Db = db, Contracts = contracts, Ops = ops,
            Svc = new AssignmentService(db, mediator.Object, email.Object, readiness.Object, contracts.Mock.Object, ops.Object,
                Mock.Of<ILogger<AssignmentService>>()),
        };
        rig.Rebuild = d => new AssignmentService(d, mediator.Object, email.Object, readiness.Object, contracts.Mock.Object, ops.Object,
            Mock.Of<ILogger<AssignmentService>>());
        mediator.Setup(m => m.Send(It.IsAny<SendNotificationCommand>(), It.IsAny<CancellationToken>()))
            .Returns((SendNotificationCommand c, CancellationToken _) =>
            {
                if (rig.FailNextSend > 0) { rig.FailNextSend--; throw new InvalidOperationException("injected notification failure"); }
                rig.Sent.Add(c); return Task.FromResult("id");
            });
        email.Setup(e => e.SendGenericNotificationEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(() => { rig.Emails++; return Task.CompletedTask; });
        return rig;
    }

    private static async Task<(Rig rig, string assignmentId, string prId, string cgId)> PendingAsync()
    {
        var name = Phase4AssignmentTests.NewDbName();
        string prId, cgId;
        using (var seed = Phase4AssignmentTests.CreateDb(name))
            (prId, cgId, _) = await Phase4AssignmentTests.SeedRequestAndCaregiver(seed);
        var rig = NewRig(name);
        var a = await rig.Svc.AssignAsync(prId, cgId, "admin-1", "ops@x", "staff", null);
        rig.Sent.Clear(); rig.Emails = 0;
        return (rig, a.Id, prId, cgId);
    }

    private static async Task<(Assignment a, PackageRequest r, bool contract)> StateAsync(string dbName, string assignmentId, string prId)
    {
        using var db = Phase4AssignmentTests.CreateDb(dbName);
        return (await db.Assignments.AsNoTracking().FirstAsync(x => x.Id == ObjectId.Parse(assignmentId)),
                await db.PackageRequests.AsNoTracking().FirstAsync(p => p.Id == ObjectId.Parse(prId)),
                await db.Contracts.AnyAsync(c => c.PackageRequestId == prId));
    }

    // ───────────────────────── Accept ─────────────────────────

    [Fact]
    public async Task Accept_ContractGenerationFails_AcceptanceStands_AdminAlertedOnce_CaregiverRetryCompletesWithoutResending()
    {
        var (rig, id, prId, cgId) = await PendingAsync();
        rig.Contracts.FailTimes = 1;

        var r = await rig.Svc.AcceptAsync(id, cgId);
        Assert.Equal("Accepted", r.Status);
        var s1 = await StateAsync(rig.DbName, id, prId);
        Assert.Equal("Accepted", s1.a.Status); Assert.Equal("confirmed", s1.r.Status); Assert.False(s1.contract);
        Assert.Equal(1, rig.Count(NotificationTypes.PackageAssignmentConfirmed));
        Assert.Equal(1, rig.OpsCount(NotificationTypes.PackageContractDeliveryFailed));

        await AgeAsync(rig.DbName, id); rig.Renew();
        // Same caregiver tries again: finishes the missing contract, success, nothing re-sent, no second alert.
        var again = await rig.Svc.AcceptAsync(id, cgId);
        Assert.True(again.Success);
        var s2 = await StateAsync(rig.DbName, id, prId);
        Assert.True(s2.contract);
        Assert.Equal(1, rig.Count(NotificationTypes.PackageAssignmentConfirmed));
        Assert.Equal(2, rig.Emails);   // client + caregiver, once each across both calls
        Assert.Equal(1, rig.OpsCount(NotificationTypes.PackageContractDeliveryFailed));

        // Fully complete now: another accept is the plain idempotent "Already accepted", still nothing re-sent.
        var third = await rig.Svc.AcceptAsync(id, cgId);
        Assert.Equal("Already accepted.", third.Message);
        Assert.Equal(1, rig.Count(NotificationTypes.PackageAssignmentConfirmed));
    }

    [Fact]
    public async Task Accept_RequestUpdateThrows_LeavesAcceptedWithRequestStillAssigned_RetryCompletes()
    {
        var (rig, id, prId, cgId) = await PendingAsync();
        rig.Db.ThrowOnceWhen = d => d.Modifying<PackageRequest>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Svc.AcceptAsync(id, cgId));
        var s1 = await StateAsync(rig.DbName, id, prId);
        Assert.Equal("Accepted", s1.a.Status); Assert.Equal("assigned", s1.r.Status); Assert.False(s1.contract);
        Assert.Empty(rig.Sent);

        await AgeAsync(rig.DbName, id); rig.Renew();
        var again = await rig.Svc.AcceptAsync(id, cgId);   // no 409: the caregiver can finish it
        Assert.True(again.Success);
        var s2 = await StateAsync(rig.DbName, id, prId);
        Assert.Equal("confirmed", s2.r.Status); Assert.Equal(cgId, s2.r.ConfirmedCaregiverId); Assert.True(s2.contract);
        Assert.Equal(1, rig.Count(NotificationTypes.PackageAssignmentConfirmed));
    }

    [Fact]
    public async Task Accept_NotificationThrows_ClaimReleased_RetrySendsExactlyOnce()
    {
        var (rig, id, prId, cgId) = await PendingAsync();
        rig.FailNextSend = 1;

        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Svc.AcceptAsync(id, cgId));
        var s1 = await StateAsync(rig.DbName, id, prId);
        Assert.Equal("Accepted", s1.a.Status); Assert.Equal("confirmed", s1.r.Status); Assert.Null(s1.a.DownstreamDoneAt);
        Assert.Empty(rig.Sent);

        await AgeAsync(rig.DbName, id); rig.Renew();
        await rig.Svc.AcceptAsync(id, cgId);
        Assert.Equal(1, rig.Count(NotificationTypes.PackageAssignmentConfirmed));
        Assert.True((await StateAsync(rig.DbName, id, prId)).contract);
    }

    [Fact]
    public async Task Accept_AnotherCaregiverCannotCompleteSomeoneElsesHalfDoneAccept()
    {
        var (rig, id, prId, cgId) = await PendingAsync();
        rig.Db.ThrowOnceWhen = d => d.Modifying<PackageRequest>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Svc.AcceptAsync(id, cgId));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Svc.AcceptAsync(id, ObjectId.GenerateNewId().ToString()));
        var s = await StateAsync(rig.DbName, id, prId);
        Assert.Equal("assigned", s.r.Status);   // untouched
        Assert.Empty(rig.Sent);
    }

    [Fact]
    public async Task Accept_LosesTheNotificationClaimToAConcurrentFinisher_StillSucceeds_SendsNothingTwice()
    {
        // Regression: the lost-claim branch used EF Reload, which throws ArgumentNullException on the Mongo provider (seen as an HTTP 400).
        var (a, id, prId, cgId) = await PendingAsync();
        a.Db.ThrowOnceWhen = d => d.Modifying<PackageRequest>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => a.Svc.AcceptAsync(id, cgId));   // half-done: Accepted, request still Assigned
        await AgeAsync(a.DbName, id);

        var b = NewRig(a.DbName);
        var other = NewRig(a.DbName);   // the other finisher: its own request-scoped context, as in production
        var fired = false;
        b.Db.ThrowOnceWhen = d =>
        {
            // The instant B is about to save its notification claim, the other finisher completes everything first.
            if (!fired && d.ChangeTracker.Entries<Assignment>().Any(e => e.State == EntityState.Modified && e.Entity.DownstreamDoneAt != null))
            {
                fired = true;
                other.Svc.AcceptAsync(id, cgId).GetAwaiter().GetResult();
            }
            return false;
        };

        var r = await b.Svc.AcceptAsync(id, cgId);
        Assert.True(fired); Assert.True(r.Success);
        Assert.Equal(1, other.Count(NotificationTypes.PackageAssignmentConfirmed));
        Assert.Equal(0, b.Count(NotificationTypes.PackageAssignmentConfirmed));   // B lost the claim: sends nothing, still succeeds
        Assert.True((await StateAsync(a.DbName, id, prId)).contract);
    }

    [Fact]
    public async Task Accept_DuplicateWhileFirstIsStillFinishing_IsA409_NotAJoinedRetry()
    {
        var (rig, id, prId, cgId) = await PendingAsync();
        rig.Db.ThrowOnceWhen = d => d.Modifying<PackageRequest>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Svc.AcceptAsync(id, cgId));   // half-done, claimed just now

        var ex = await Assert.ThrowsAsync<AssignmentConflictException>(() => rig.Svc.AcceptAsync(id, cgId));   // inside the grace window
        Assert.Contains("just changed", ex.Message);
        Assert.Equal("assigned", (await StateAsync(rig.DbName, id, prId)).r.Status);   // the duplicate did not run any steps
    }

    // ───────────────────────── Decline / cancel ─────────────────────────

    [Fact]
    public async Task Decline_RequestUpdateThrows_RetryCompletes_AdminToldExactlyOnce()
    {
        var (rig, id, prId, cgId) = await PendingAsync();
        rig.Db.ThrowOnceWhen = d => d.Modifying<PackageRequest>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Svc.DeclineAsync(id, cgId, "busy"));
        var s1 = await StateAsync(rig.DbName, id, prId);
        Assert.Equal("Declined", s1.a.Status); Assert.Equal("assigned", s1.r.Status);
        Assert.Equal(0, rig.OpsCount(NotificationTypes.PackageAssignmentDeclined));

        await AgeAsync(rig.DbName, id); rig.Renew();
        var again = await rig.Svc.DeclineAsync(id, cgId, "busy");
        Assert.True(again.Success);
        Assert.Equal("pending", (await StateAsync(rig.DbName, id, prId)).r.Status);
        Assert.Equal(1, rig.OpsCount(NotificationTypes.PackageAssignmentDeclined));

        // Complete now: a further decline is the normal 409 - and, like every other "lost to someone else" outcome, it carries ASSIGNMENT_CONFLICT.
        // await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Svc.DeclineAsync(id, cgId, "busy")); // was: plain InvalidOperationException (409 without errorCode)
        await Assert.ThrowsAsync<Domain.Entities.AssignmentConflictException>(() => rig.Svc.DeclineAsync(id, cgId, "busy"));
        Assert.Equal(1, rig.OpsCount(NotificationTypes.PackageAssignmentDeclined));
    }

    [Fact]
    public async Task Decline_AdminNotificationThrows_ClaimReleased_RetryNotifiesOnce()
    {
        var (rig, id, prId, cgId) = await PendingAsync();
        rig.Ops.Setup(o => o.NotifyAdminsAsync(NotificationTypes.PackageAssignmentDeclined, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
            .ThrowsAsync(new InvalidOperationException("injected ops failure"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Svc.DeclineAsync(id, cgId, null));
        Assert.Equal("pending", (await StateAsync(rig.DbName, id, prId)).r.Status);

        rig.Ops.Reset();
        await AgeAsync(rig.DbName, id); rig.Renew();
        await rig.Svc.DeclineAsync(id, cgId, null);
        Assert.Equal(1, rig.OpsCount(NotificationTypes.PackageAssignmentDeclined));
    }

    [Fact]
    public async Task StaffCancel_RequestUpdateThrows_RetryCompletes_CaregiverToldOnce()
    {
        var (rig, id, prId, _) = await PendingAsync();
        rig.Db.ThrowOnceWhen = d => d.Modifying<PackageRequest>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Svc.CancelAsync(id, "admin-1", "ops@x", "wrong person picked"));
        var s1 = await StateAsync(rig.DbName, id, prId);
        Assert.Equal("Cancelled", s1.a.Status); Assert.Equal("assigned", s1.r.Status);
        Assert.Empty(rig.Sent);

        await AgeAsync(rig.DbName, id); rig.Renew();
        await rig.Svc.CancelAsync(id, "admin-1", "ops@x", "wrong person picked");
        Assert.Equal("pending", (await StateAsync(rig.DbName, id, prId)).r.Status);
        Assert.Equal(1, rig.Count(NotificationTypes.PackageAssignmentCancelled));
        using var chk = Phase4AssignmentTests.CreateDb(rig.DbName);
        Assert.Equal(1, await chk.AdminAuditLogs.CountAsync(l => l.Action == "PackageAssignmentCancelled"));
    }

    // ───────────────────────── Assign ─────────────────────────

    [Fact]
    public async Task Assign_InsertThrowsAfterClaim_RequestPutBackToPreviousStatus()
    {
        var name = Phase4AssignmentTests.NewDbName();
        string prId, cgId;
        using (var seed = Phase4AssignmentTests.CreateDb(name))
            (prId, cgId, _) = await Phase4AssignmentTests.SeedRequestAndCaregiver(seed);
        var rig = NewRig(name);
        rig.Db.ThrowOnceWhen = d => d.Modifying<Assignment>(EntityState.Added);

        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Svc.AssignAsync(prId, cgId, "admin-1", "ops@x", "staff", null));

        using var chk = Phase4AssignmentTests.CreateDb(name);
        Assert.Equal("pending", (await chk.PackageRequests.AsNoTracking().FirstAsync(p => p.Id == ObjectId.Parse(prId))).Status);
        Assert.Equal(0, await chk.Assignments.CountAsync());

        // And it is assignable again straight away.
        var a = await NewRig(name).Svc.AssignAsync(prId, cgId, "admin-1", "ops@x", "staff", null);
        Assert.Equal("PendingAcceptance", a.Status);
    }

    // ───────────────────────── Recovery sweeper ─────────────────────────

    private static async Task AgeAsync(string dbName, string assignmentId)
    {
        using var db = Phase4AssignmentTests.CreateDb(dbName);
        var a = await db.Assignments.FirstAsync(x => x.Id == ObjectId.Parse(assignmentId));
        a.DownstreamPendingSince = DateTime.UtcNow.AddMinutes(-30);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Sweeper_FinishesHalfDoneAccept_AndDecline_AndCancel_NotifyingOnce()
    {
        // accept
        var (r1, id1, pr1, cg1) = await PendingAsync();
        r1.Db.ThrowOnceWhen = d => d.Modifying<PackageRequest>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => r1.Svc.AcceptAsync(id1, cg1));
        await AgeAsync(r1.DbName, id1);
        // decline
        var (r2, id2, pr2, cg2) = await PendingAsync();
        r2.Db.ThrowOnceWhen = d => d.Modifying<PackageRequest>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => r2.Svc.DeclineAsync(id2, cg2, null));
        await AgeAsync(r2.DbName, id2);
        // cancel
        var (r3, id3, pr3, _) = await PendingAsync();
        r3.Db.ThrowOnceWhen = d => d.Modifying<PackageRequest>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => r3.Svc.CancelAsync(id3, "admin-1", "a@x", "reason here"));
        await AgeAsync(r3.DbName, id3);

        foreach (var (rig, id, pr, expect) in new[] { (r1, id1, pr1, "confirmed"), (r2, id2, pr2, "pending"), (r3, id3, pr3, "pending") })
        {
            var sweeper = NewRig(rig.DbName);   // a different "instance"
            Assert.Equal(1, await sweeper.Svc.RecoverIncompleteTransitionsAsync(TimeSpan.FromMinutes(5)));
            Assert.Equal(expect, (await StateAsync(rig.DbName, id, pr)).r.Status);
            Assert.Equal(0, await NewRig(rig.DbName).Svc.RecoverIncompleteTransitionsAsync(TimeSpan.FromMinutes(5)));   // nothing left
            if (id == id1) { Assert.Equal(1, sweeper.Count(NotificationTypes.PackageAssignmentConfirmed)); Assert.True((await StateAsync(rig.DbName, id, pr)).contract); }
            if (id == id2) Assert.Equal(1, sweeper.OpsCount(NotificationTypes.PackageAssignmentDeclined));
            if (id == id3) Assert.Equal(1, sweeper.Count(NotificationTypes.PackageAssignmentCancelled));
        }
    }

    [Fact]
    public async Task Sweeper_TwoInstancesAtOnce_SendEverythingExactlyOnce()
    {
        for (var round = 0; round < 10; round++)
        {
            var (rig, id, pr, cg) = await PendingAsync();
            rig.Db.ThrowOnceWhen = d => d.Modifying<PackageRequest>();
            await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Svc.AcceptAsync(id, cg));
            await AgeAsync(rig.DbName, id); rig.Renew();

            var a = NewRig(rig.DbName); var b = NewRig(rig.DbName);
            var gate = new TaskCompletionSource();
            var ta = Task.Run(async () => { await gate.Task; await a.Svc.RecoverIncompleteTransitionsAsync(TimeSpan.FromMinutes(5)); });
            var tb = Task.Run(async () => { await gate.Task; await b.Svc.RecoverIncompleteTransitionsAsync(TimeSpan.FromMinutes(5)); });
            gate.SetResult(); await Task.WhenAll(ta, tb);

            Assert.Equal(1, a.Count(NotificationTypes.PackageAssignmentConfirmed) + b.Count(NotificationTypes.PackageAssignmentConfirmed));
            var s = await StateAsync(rig.DbName, id, pr);
            Assert.Equal("confirmed", s.r.Status); Assert.True(s.contract);
        }
    }

    [Fact]
    public async Task Sweeper_CannotComplete_AlertsAdminsOnce_NotOnEveryRun()
    {
        var (rig, id, pr, cg) = await PendingAsync();
        rig.Contracts.FailTimes = 1;                 // the accept's own contract attempt fails -> admin alert #1 (claims the once-only alert)
        await rig.Svc.AcceptAsync(id, cg);
        await AgeAsync(rig.DbName, id); rig.Renew();

        var sweeper = NewRig(rig.DbName);
        sweeper.Contracts.FailTimes = 5;             // and recovery keeps failing
        Assert.Equal(0, await sweeper.Svc.RecoverIncompleteTransitionsAsync(TimeSpan.FromMinutes(5)));
        var healthy = NewRig(rig.DbName);            // generator healthy again -> the next sweep completes it, silently
        Assert.Equal(1, await healthy.Svc.RecoverIncompleteTransitionsAsync(TimeSpan.FromMinutes(5)));
        Assert.Equal(0, healthy.OpsCount(NotificationTypes.PackageContractDeliveryFailed));
        Assert.True((await StateAsync(rig.DbName, id, pr)).contract);
        Assert.Equal(0, sweeper.OpsCount(NotificationTypes.PackageContractDeliveryFailed));   // already alerted by the accept: no repeat
        Assert.Equal(1, rig.OpsCount(NotificationTypes.PackageContractDeliveryFailed));
    }

    [Fact]
    public async Task Sweeper_FailureWithNoPriorAlert_AlertsOnce()
    {
        var (rig, id, pr, cg) = await PendingAsync();
        rig.Db.ThrowOnceWhen = d => d.Modifying<PackageRequest>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Svc.AcceptAsync(id, cg));
        await AgeAsync(rig.DbName, id); rig.Renew();

        for (var run = 0; run < 3; run++)
        {
            var sweeper = NewRig(rig.DbName);
            sweeper.Contracts.FailTimes = 99;
            await sweeper.Svc.RecoverIncompleteTransitionsAsync(TimeSpan.FromMinutes(5));
            Assert.Equal(run == 0 ? 1 : 0, sweeper.OpsCount(NotificationTypes.PackageContractDeliveryFailed));
        }
    }

    [Fact]
    public async Task Sweeper_IgnoresLegacyAssignmentsWithoutMarkers()
    {
        var (rig, id, pr, cg) = await PendingAsync();
        await rig.Svc.AcceptAsync(id, cg);
        using (var db = Phase4AssignmentTests.CreateDb(rig.DbName))
        {   // simulate a pre-change row: no markers, no contract
            var a = await db.Assignments.FirstAsync();
            a.DownstreamPendingSince = null; a.DownstreamDoneAt = null;
            db.Contracts.RemoveRange(db.Contracts);
            await db.SaveChangesAsync();
        }
        var sweeper = NewRig(rig.DbName);
        Assert.Equal(0, await sweeper.Svc.RecoverIncompleteTransitionsAsync(TimeSpan.Zero));
        Assert.Empty(sweeper.Sent); Assert.Equal(0, sweeper.Contracts.Calls);
    }
}
