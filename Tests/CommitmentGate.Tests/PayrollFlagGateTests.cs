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
using P = CommitmentGate.Tests.PackageAssignmentCheckinAndTaskSheetTests;

namespace CommitmentGate.Tests;

/// <summary>Payroll soft gate: uncleared HARD-flagged visits need an explicit, recorded confirmation — they never auto-hold pay.</summary>
public class PayrollFlagGateTests
{
    private static PayrollService Svc(CareProDbContext db) => new(db, Mock.Of<ITaskSheetService>(), Mock.Of<ICaregiverWalletService>(),
        Mock.Of<IEarningsLedgerService>(), Mock.Of<IMediator>(), Mock.Of<IEmailService>(), Mock.Of<ILogger<PayrollService>>(), new PackageVisitAdminService(db));

    private static async Task<(string payrollId, string caregiverId, List<VisitCheckin> flagged)> Seed(CareProDbContext db, int hardFlags, bool inPeriod = true)
    {
        var cg = ObjectId.GenerateNewId().ToString(); var asg = ObjectId.GenerateNewId().ToString();
        var now = DateTime.UtcNow;
        var period = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var pay = new Payroll { Id = ObjectId.GenerateNewId(), AssignmentId = asg, PackageRequestId = "pr", CaregiverId = cg, ClientId = "cl", PayPeriod = period,
            PayCalculationType = PayCalculationType.Fixed, CalculatedAmount = 100000, FinalAmount = 100000, Status = PayrollStatuses.Draft };
        db.Payrolls.Add(pay);
        var list = new List<VisitCheckin>();
        for (int i = 0; i < hardFlags; i++)
        {
            var c = new VisitCheckin { Id = ObjectId.GenerateNewId(), TaskSheetId = ObjectId.GenerateNewId().ToString(), AssignmentId = asg, PackageRequestId = "pr", CaregiverId = cg,
                Accuracy = 10, CheckinTimestamp = inPeriod ? now : period.AddMonths(-1).AddDays(3), FlagReasons = new List<string> { "distance_far" }, IsFlaggedForDistanceReview = true };
            db.VisitCheckins.Add(c); list.Add(c);
        }
        await db.SaveChangesAsync();
        return (pay.Id.ToString(), cg, list);
    }

    [Fact]
    public async Task Approve_WithUnclearedHardFlags_WithoutConfirmation_IsRefused_AndNothingChanges()
    {
        using var db = P.CreateDb(P.NewDbName());
        var (id, _, _) = await Seed(db, 2);
        var ex = await Assert.ThrowsAsync<UnclearedFlaggedVisitsException>(() => Svc(db).ApprovePayrollAsync(id, new ApprovePayrollRequest(), "admin1", "a@x"));
        Assert.Equal(2, ex.Count);
        Assert.Equal(PayrollStatuses.Draft, (await db.Payrolls.AsNoTracking().FirstAsync()).Status);
        // a wrong / stale count does not count as a confirmation
        await Assert.ThrowsAsync<UnclearedFlaggedVisitsException>(() => Svc(db).ApprovePayrollAsync(id, new ApprovePayrollRequest { AcknowledgeUnclearedFlaggedVisits = 1 }, "admin1", "a@x"));
    }

    [Fact]
    public async Task Approve_WithExplicitConfirmation_Succeeds_AndRecordsCountAndAdmin()
    {
        using var db = P.CreateDb(P.NewDbName());
        var (id, _, _) = await Seed(db, 2);
        var dto = await Svc(db).ApprovePayrollAsync(id, new ApprovePayrollRequest { AcknowledgeUnclearedFlaggedVisits = 2 }, "admin1", "a@x");
        Assert.Equal(PayrollStatuses.Approved, dto.Status);
        Assert.Equal(2, dto.ApprovedWithUnclearedFlags);
        var audit = await db.AdminAuditLogs.AsNoTracking().FirstAsync(a => a.Action == "PayrollApprovedWithUnclearedFlags");
        Assert.Equal("admin1", audit.AdminId);
        Assert.Equal(100000m, dto.FinalAmount);        // flagged visits are still paid in full
    }

    [Fact]
    public async Task ClearingTheFlags_RemovesTheWarning_SoPlainApprovalWorks()
    {
        using var db = P.CreateDb(P.NewDbName());
        var (id, _, flagged) = await Seed(db, 2);
        var visits = new PackageVisitAdminService(db);
        foreach (var f in flagged) await visits.ClearFlagAsync(f.Id.ToString(), "admin1", "a@x", "checked with client");
        var list = await Svc(db).GetPayrollByIdAsync(id);
        Assert.Equal(0, list!.UnclearedHardFlagCount);
        var dto = await Svc(db).ApprovePayrollAsync(id, new ApprovePayrollRequest(), "admin1", "a@x");
        Assert.Equal(PayrollStatuses.Approved, dto.Status);
        Assert.Null(dto.ApprovedWithUnclearedFlags);
    }

    [Fact]
    public async Task DraftDto_ShowsLiveCount_AndFlagsOutsideThePayMonthDontCount()
    {
        using var db = P.CreateDb(P.NewDbName());
        var (id, _, _) = await Seed(db, 3);
        Assert.Equal(3, (await Svc(db).GetPayrollByIdAsync(id))!.UnclearedHardFlagCount);
        using var db2 = P.CreateDb(P.NewDbName());
        var (id2, _, _) = await Seed(db2, 3, inPeriod: false);
        Assert.Equal(0, (await Svc(db2).GetPayrollByIdAsync(id2))!.UnclearedHardFlagCount);
        var approved = await Svc(db2).ApprovePayrollAsync(id2, new ApprovePayrollRequest(), "admin1", "a@x");
        Assert.Equal(PayrollStatuses.Approved, approved.Status);
    }

    [Fact]
    public async Task SoftFlagsNeverGateApproval()
    {
        using var db = P.CreateDb(P.NewDbName());
        var (id, _, flagged) = await Seed(db, 1);
        flagged[0].FlagReasons = new List<string> { "low_accuracy", "location_not_confirmed" }; flagged[0].IsFlaggedForDistanceReview = false;
        await db.SaveChangesAsync();
        Assert.Equal(PayrollStatuses.Approved, (await Svc(db).ApprovePayrollAsync(id, new ApprovePayrollRequest(), "admin1", "a@x")).Status);
    }

    [Fact]
    public async Task ClearFlag_RequiresNote_RecordsWhoWhenWhy_AndIsIdempotent()
    {
        using var db = P.CreateDb(P.NewDbName());
        var (_, _, flagged) = await Seed(db, 1);
        var visits = new PackageVisitAdminService(db);
        await Assert.ThrowsAsync<ArgumentException>(() => visits.ClearFlagAsync(flagged[0].Id.ToString(), "admin1", "a@x", " "));
        var row = await visits.ClearFlagAsync(flagged[0].Id.ToString(), "admin1", "a@x", "first note");
        Assert.True(row.FlagCleared); Assert.Equal("first note", row.FlagClearNote); Assert.Equal("a@x", row.FlagClearedByAdminEmail);
        var again = await visits.ClearFlagAsync(flagged[0].Id.ToString(), "admin2", "b@x", "second note");
        Assert.Equal("first note", again.FlagClearNote);             // history kept, not overwritten
        Assert.Single(await visits.ListAsync(new PackageVisitAdminQuery { Flagged = "hard" })); // cleared flags stay visible
    }

    [Fact]
    public async Task RepeatedCheckinCall_DoesNotNotifyAdminsTwice()
    {
        var cg = ObjectId.GenerateNewId().ToString(); var cl = ObjectId.GenerateNewId().ToString();
        using var db = P.CreateDb(P.NewDbName());
        var (aid, _) = P.SeedAcceptedAssignmentAndContract(db, cg, cl);
        var c = await db.Contracts.FirstAsync(); c.ServiceLatitude = 6.54; c.ServiceLongitude = 3.38; c.ServiceLocationSetByClient = true; await db.SaveChangesAsync();
        var sheet = await P.CreateTaskSheetService(db).CreateTaskSheetForAssignmentAsync(aid, cg);
        var ops = new Mock<IOpsAlertService>();
        var svc = new VisitCheckinService(db, Mock.Of<IGeocodingService>(), new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
            Mock.Of<Microsoft.Extensions.Hosting.IHostEnvironment>(), Mock.Of<IMediator>(), Mock.Of<ILogger<VisitCheckinService>>(), ops.Object);
        var req = new VisitCheckinRequest { TaskSheetId = sheet.Id, OrderId = null, Latitude = -33, Longitude = 151, Accuracy = 10, CheckinTimestamp = DateTime.UtcNow };
        await svc.CheckinAsync(req, cg);
        var second = await svc.CheckinAsync(req, cg);
        Assert.True(second.AlreadyCheckedIn);
        ops.Verify(o => o.NotifyAdminsAsync(NotificationTypes.PackageVisitFlagged, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Once);
    }
}
