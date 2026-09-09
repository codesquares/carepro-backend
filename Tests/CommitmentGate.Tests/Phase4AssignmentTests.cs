using Application.Commands;
using Application.DTOs;
using Application.Interfaces.Content;
using Application.Interfaces.Email;
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

/// <summary>
/// Phase 4 — internal assignment engine + one-sided caregiver acceptance.
/// Real local MongoDB (replica set on 127.0.0.1:27018).
/// </summary>
public class Phase4AssignmentTests
{
    private static CareProDbContext CreateDb(string name)
    {
        var options = new DbContextOptionsBuilder<CareProDbContext>()
            .UseMongoDB("mongodb://127.0.0.1:27018", name).Options;
        return new TestCareProDbContext(options);
    }

    private static string NewDbName() => $"carepro_phase4_{Guid.NewGuid():N}";

    private static Caregiver AddCaregiver(CareProDbContext db, CaregiverType type, string? specialty,
        bool available = true, bool identityVerified = true, string category = "General")
    {
        var cg = new Caregiver
        {
            Id = ObjectId.GenerateNewId(),
            FirstName = "Case", LastName = "Giver",
            Email = $"cg-{Guid.NewGuid():N}@example.com",
            Password = "x", // pragma: allowlist-secret
            Role = "Caregiver",
            Status = true,
            IsAvailable = available,
            IsIdentityVerified = identityVerified,
            CaregiverType = type,
            Specialty = specialty,
            CreatedAt = DateTime.UtcNow
        };
        db.CareGivers.Add(cg);
        db.Gigs.Add(new Gig
        {
            Id = ObjectId.GenerateNewId(),
            CaregiverId = cg.Id.ToString(),
            Title = $"{category} care", Category = category, SubCategory = category,
            Status = "Active", Price = 15000, CreatedAt = DateTime.UtcNow
        });
        return cg;
    }

    private static Client AddClient(CareProDbContext db)
    {
        var c = new Client
        {
            Id = ObjectId.GenerateNewId(),
            FirstName = "Client", LastName = "Test",
            Email = $"client-{Guid.NewGuid():N}@example.com",
            Password = "x", // pragma: allowlist-secret
            Role = "Client", IsDeleted = false
        };
        db.Clients.Add(c);
        return c;
    }

    private static Package AddPackage(CareProDbContext db, CaregiverType type, string? specialty,
        string category = "General", string tier = "Standard")
    {
        var p = new Package
        {
            Id = ObjectId.GenerateNewId(),
            Category = category, TierLabel = tier,
            RequiredCaregiverType = type, RequiredSpecialty = specialty,
            BasePrice = 200000, Description = "pkg", IsActive = true,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        db.Packages.Add(p);
        return p;
    }

    // ═══════════════════ 4.1  Matching against a package-derived request ═══════════════════

    private static CareRequestMatchingService CreateMatchingService(CareProDbContext db)
        => new(
            db,
            Mock.Of<IGeocodingService>(),
            new EligibilityService(db, Mock.Of<ILogger<EligibilityService>>()),
            Mock.Of<ILogger<CareRequestMatchingService>>());

    [Fact]
    public async Task FindCandidatesForPackage_HardFiltersOnCaregiverTypeAndSpecialty()
    {
        using var db = CreateDb(NewDbName());
        var rnMidwife = AddCaregiver(db, CaregiverType.RegisteredNurse, "Midwifery");
        var rnNoSpecialty = AddCaregiver(db, CaregiverType.RegisteredNurse, null);
        var chew = AddCaregiver(db, CaregiverType.CHEW, null);
        await db.SaveChangesAsync();

        var svc = CreateMatchingService(db);

        // Post-Partum style requirement: RegisteredNurse + Midwifery
        var midwives = await svc.FindCandidatesForPackageAsync(new PackageAssignmentMatchQuery
        {
            ServiceCategory = "General",
            RequiredCaregiverType = "RegisteredNurse",
            RequiredSpecialty = "Midwifery"
        });
        Assert.Single(midwives);
        Assert.Equal(rnMidwife.Id.ToString(), midwives[0].CaregiverId);
        Assert.True(midwives[0].MatchScore > 0); // scoring pipeline still ran

        // Post Surgery style: RegisteredNurse, any specialty → both RNs, not the CHEW
        var nurses = await svc.FindCandidatesForPackageAsync(new PackageAssignmentMatchQuery
        {
            ServiceCategory = "General",
            RequiredCaregiverType = "RegisteredNurse"
        });
        Assert.Equal(2, nurses.Count);
        Assert.DoesNotContain(nurses, m => m.CaregiverId == chew.Id.ToString());

        // Adult/Elder "Standard" style: CHEW
        var chews = await svc.FindCandidatesForPackageAsync(new PackageAssignmentMatchQuery
        {
            ServiceCategory = "General",
            RequiredCaregiverType = "CHEW"
        });
        Assert.Single(chews);
        Assert.Equal(chew.Id.ToString(), chews[0].CaregiverId);
    }

    [Fact]
    public async Task FindCandidatesForPackage_ExcludesUnavailableCaregivers()
    {
        using var db = CreateDb(NewDbName());
        AddCaregiver(db, CaregiverType.CHEW, null, available: false);
        await db.SaveChangesAsync();

        var result = await CreateMatchingService(db).FindCandidatesForPackageAsync(new PackageAssignmentMatchQuery
        {
            ServiceCategory = "General",
            RequiredCaregiverType = "CHEW"
        });
        Assert.Empty(result);
    }

    // ═══════════════════ 4.2  Assignment + acceptance ═══════════════════

    private sealed class AssignHarness
    {
        public required AssignmentService Assignments { get; init; }
        public required PackageRequestService Requests { get; init; }
        public required Mock<IMediator> Mediator { get; init; }
    }

    private static AssignHarness CreateAssignHarness(CareProDbContext db, bool caregiverReady = true)
    {
        var mediator = new Mock<IMediator>();
        var readiness = new Mock<ICaregiverReadinessService>();
        readiness.Setup(r => r.GetReadinessAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool?>()))
            .ReturnsAsync(caregiverReady
                ? new CaregiverReadinessResult { IsReady = true }
                : new CaregiverReadinessResult { IsReady = false, IneligibilityReasons = { CaregiverReadinessReasons.NotIdentityVerified } });

        var assignments = new AssignmentService(
            db, mediator.Object, Mock.Of<IEmailService>(), readiness.Object,
            Mock.Of<IPackageContractService>(),
            Mock.Of<ILogger<AssignmentService>>());
        var requests = new PackageRequestService(db, Mock.Of<ILogger<PackageRequestService>>());

        return new AssignHarness { Assignments = assignments, Requests = requests, Mediator = mediator };
    }

    private static async Task<(string prId, string cgId, string clientId)> SeedRequestAndCaregiver(
        CareProDbContext db, CaregiverType pkgType = CaregiverType.RegisteredNurse, string? pkgSpecialty = "Midwifery")
    {
        var client = AddClient(db);
        var pkg = AddPackage(db, pkgType, pkgSpecialty);
        var cg = AddCaregiver(db, pkgType, pkgSpecialty);
        await db.SaveChangesAsync();

        var h = CreateAssignHarness(db);
        var pr = await h.Requests.CreateAsync(client.Id.ToString(), new CreatePackageRequestRequest
        {
            PackageId = pkg.Id.ToString(),
            ServiceCategory = "General",
            Location = "Lagos"
        });
        return (pr.Id, cg.Id.ToString(), client.Id.ToString());
    }

    [Fact]
    public async Task Assign_Accept_Finalizes_ClientSeesCaregiverOnlyAfterAcceptance()
    {
        var dbName = NewDbName();
        string prId, cgId, clientId, assignmentId;

        using (var db = CreateDb(dbName))
        {
            (prId, cgId, clientId) = await SeedRequestAndCaregiver(db);
            var h = CreateAssignHarness(db);

            var assignment = await h.Assignments.AssignAsync(prId, cgId, "admin-1", "ops@carepro.test", "staff", 72.5);
            assignmentId = assignment.Id;
            Assert.Equal("PendingAcceptance", assignment.Status);

            // Real notification to the caregiver.
            h.Mediator.Verify(m => m.Send(
                It.Is<SendNotificationCommand>(c =>
                    c.RecipientId == cgId && c.Type == NotificationTypes.PackageAssignmentOffered),
                It.IsAny<CancellationToken>()), Times.Once);

            // Client sees NO caregiver yet.
            var beforeAccept = await h.Requests.GetForClientAsync(clientId, prId);
            Assert.Equal("assigned", beforeAccept.Status);
            Assert.Null(beforeAccept.ConfirmedCaregiver);
        }

        using (var db = CreateDb(dbName))
        {
            var h = CreateAssignHarness(db);
            var result = await h.Assignments.AcceptAsync(assignmentId, cgId);
            Assert.True(result.Success);
            Assert.Equal("Accepted", result.Status);

            // Client gets a real confirmation notification.
            h.Mediator.Verify(m => m.Send(
                It.Is<SendNotificationCommand>(c =>
                    c.RecipientId == clientId && c.Type == NotificationTypes.PackageAssignmentConfirmed),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        using (var db = CreateDb(dbName))
        {
            var h = CreateAssignHarness(db);
            var afterAccept = await h.Requests.GetForClientAsync(clientId, prId);
            Assert.Equal("confirmed", afterAccept.Status);
            Assert.NotNull(afterAccept.ConfirmedCaregiver);
            Assert.Equal(cgId, afterAccept.ConfirmedCaregiver!.CaregiverId);
            Assert.Equal("RegisteredNurse", afterAccept.ConfirmedCaregiver.CaregiverType);

            var raw = await db.Assignments.FirstAsync(a => a.Id == ObjectId.Parse(assignmentId));
            Assert.Equal("Accepted", raw.Status);
            Assert.NotNull(raw.RespondedAt);
            Assert.Equal("admin-1", raw.AssignedByAdminId);
        }
    }

    [Fact]
    public async Task Assign_NotReadyCaregiver_ThrowsCaregiverNotReady()
    {
        using var db = CreateDb(NewDbName());
        var (prId, cgId, _) = await SeedRequestAndCaregiver(db);
        var h = CreateAssignHarness(db, caregiverReady: false);

        await Assert.ThrowsAsync<CaregiverNotReadyException>(
            () => h.Assignments.AssignAsync(prId, cgId, "admin-1", "ops@x", "staff", null));
    }

    [Fact]
    public async Task Assign_CaregiverTypeMismatch_Rejected()
    {
        using var db = CreateDb(NewDbName());
        var client = AddClient(db);
        var pkg = AddPackage(db, CaregiverType.RegisteredNurse, "Midwifery");
        var chew = AddCaregiver(db, CaregiverType.CHEW, null);
        await db.SaveChangesAsync();

        var h = CreateAssignHarness(db);
        var pr = await h.Requests.CreateAsync(client.Id.ToString(), new CreatePackageRequestRequest
        {
            PackageId = pkg.Id.ToString(), ServiceCategory = "General"
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => h.Assignments.AssignAsync(pr.Id, chew.Id.ToString(), "admin-1", "ops@x", "staff", null));
        Assert.Contains("does not match", ex.Message);
    }

    [Fact]
    public async Task Assign_Decline_ReturnsRequestToPending_AndNotifiesOps()
    {
        using var db = CreateDb(NewDbName());
        db.AdminUsers.Add(new AdminUser
        {
            Id = ObjectId.GenerateNewId(), FirstName = "Ops", LastName = "Admin",
            Email = "ops@carepro.test", Password = "x", Role = "Admin", Department = "CareLeads" // pragma: allowlist-secret
        });
        await db.SaveChangesAsync();

        var (prId, cgId, _) = await SeedRequestAndCaregiver(db);
        var h = CreateAssignHarness(db);
        var assignment = await h.Assignments.AssignAsync(prId, cgId, "admin-1", "ops@x", "staff", null);

        var result = await h.Assignments.DeclineAsync(assignment.Id, cgId, "Schedule conflict");
        Assert.Equal("Declined", result.Status);

        var pr = await db.PackageRequests.FirstAsync(p => p.Id == ObjectId.Parse(prId));
        Assert.Equal("pending", pr.Status); // back to the queue, no auto-reassignment

        h.Mediator.Verify(m => m.Send(
            It.Is<SendNotificationCommand>(c => c.Type == NotificationTypes.PackageAssignmentDeclined),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task PendingAcceptanceView_ShowsStalledAssignments_LongestFirst_UntilStaffActs()
    {
        using var db = CreateDb(NewDbName());

        // Two independent package requests, each assigned, neither accepted.
        var (pr1, cg1, _) = await SeedRequestAndCaregiver(db);
        var (pr2, cg2, _) = await SeedRequestAndCaregiver(db);
        var h = CreateAssignHarness(db);

        var a1 = await h.Assignments.AssignAsync(pr1, cg1, "admin-1", "ops@x", "staff", null);
        // Nudge a1's AssignedAt into the past so ordering is deterministic.
        var a1Raw = await db.Assignments.FirstAsync(a => a.Id == ObjectId.Parse(a1.Id));
        a1Raw.AssignedAt = DateTime.UtcNow.AddHours(-5);
        await db.SaveChangesAsync();

        var a2 = await h.Assignments.AssignAsync(pr2, cg2, "admin-1", "ops@x", "staff", null);

        var view = await h.Assignments.GetPendingAcceptanceAsync();
        Assert.Equal(2, view.Count);
        Assert.Equal(a1.Id, view[0].AssignmentId);          // longest-pending first
        Assert.True(view[0].PendingForHours >= 4.5);
        Assert.NotEqual(string.Empty, view[0].CaregiverName);

        // One caregiver accepts → drops out of the view; the other stays.
        await h.Assignments.AcceptAsync(a2.Id, cg2);
        var afterAccept = await h.Assignments.GetPendingAcceptanceAsync();
        Assert.Single(afterAccept);
        Assert.Equal(a1.Id, afterAccept[0].AssignmentId);

        // The stalled one STAYS (no silent auto-reassignment) across repeated reads.
        var stillThere = await h.Assignments.GetPendingAcceptanceAsync();
        Assert.Single(stillThere);
        Assert.Equal(a1.Id, stillThere[0].AssignmentId);

        // Only a staff action removes it.
        await h.Assignments.CancelAsync(a1.Id, "admin-1", "ops@x", "Caregiver unreachable after follow-up");
        var afterCancel = await h.Assignments.GetPendingAcceptanceAsync();
        Assert.Empty(afterCancel);

        var pr1Raw = await db.PackageRequests.FirstAsync(p => p.Id == ObjectId.Parse(pr1));
        Assert.Equal("pending", pr1Raw.Status);
    }

    [Fact]
    public async Task Assign_SecondActiveAssignment_Rejected_UntilFirstCancelled()
    {
        using var db = CreateDb(NewDbName());
        var (prId, cg1, _) = await SeedRequestAndCaregiver(db);
        var cg2 = AddCaregiver(db, CaregiverType.RegisteredNurse, "Midwifery");
        await db.SaveChangesAsync();
        var h = CreateAssignHarness(db);

        var a1 = await h.Assignments.AssignAsync(prId, cg1, "admin-1", "ops@x", "staff", null);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => h.Assignments.AssignAsync(prId, cg2.Id.ToString(), "admin-1", "ops@x", "staff", null));

        await h.Assignments.CancelAsync(a1.Id, "admin-1", "ops@x", "Reassigning to a closer caregiver");
        var a2 = await h.Assignments.AssignAsync(prId, cg2.Id.ToString(), "admin-1", "ops@x", "staff", null);
        Assert.Equal("PendingAcceptance", a2.Status);
    }

    [Fact]
    public async Task Accept_IsIdempotent()
    {
        using var db = CreateDb(NewDbName());
        var (prId, cgId, _) = await SeedRequestAndCaregiver(db);
        var h = CreateAssignHarness(db);
        var a = await h.Assignments.AssignAsync(prId, cgId, "admin-1", "ops@x", "staff", null);

        await h.Assignments.AcceptAsync(a.Id, cgId);
        var second = await h.Assignments.AcceptAsync(a.Id, cgId);
        Assert.True(second.Success);
        Assert.Equal("Accepted", second.Status);
    }

    [Fact]
    public async Task Accept_ByWrongCaregiver_Rejected()
    {
        using var db = CreateDb(NewDbName());
        var (prId, cgId, _) = await SeedRequestAndCaregiver(db);
        var other = AddCaregiver(db, CaregiverType.RegisteredNurse, "Midwifery");
        await db.SaveChangesAsync();
        var h = CreateAssignHarness(db);
        var a = await h.Assignments.AssignAsync(prId, cgId, "admin-1", "ops@x", "staff", null);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => h.Assignments.AcceptAsync(a.Id, other.Id.ToString()));
    }
}
