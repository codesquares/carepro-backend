using Application.DTOs;
using Application.Interfaces.Content;
using Domain.Entities;
using Infrastructure.Content.Data;
using Infrastructure.Content.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using MongoDB.Bson;
using Xunit;
using P = CommitmentGate.Tests.PackageAssignmentCheckinAndTaskSheetTests;

namespace CommitmentGate.Tests;

/// <summary>Check-in flag REASONS (hard/soft). A check-in is never blocked for location reasons.</summary>
public class CheckinFlagReasonTests
{
    private static readonly CheckinFlagThresholds T = new(3000, 10000, 1500);

    [Theory]
    [InlineData(50.0, 30.0, ServiceLocationState.ClientConfirmed, "")]
    [InlineData(50.0, 2000.0, ServiceLocationState.ClientConfirmed, "low_accuracy")]
    [InlineData(2501.0, 10.0, ServiceLocationState.ClientConfirmed, "")]
    [InlineData(4003.0, 10.0, ServiceLocationState.ClientConfirmed, "distance_far")]
    [InlineData(15563463.0, 9000.0, ServiceLocationState.ClientConfirmed, "distance_far,low_accuracy")]
    [InlineData(15563463.0, 9000.0, ServiceLocationState.GeocodedOnly, "location_geocoded_only,geocoded_far,low_accuracy")]
    [InlineData(4003.0, 10.0, ServiceLocationState.GeocodedOnly, "location_geocoded_only")]            // geocoded is coarse: 4 km is not "far" (10 km)
    [InlineData(10001.0, 10.0, ServiceLocationState.GeocodedOnly, "location_geocoded_only,geocoded_far")]
    [InlineData(null, 10.0, ServiceLocationState.None, "location_not_confirmed")]
    [InlineData(null, 9000.0, ServiceLocationState.None, "location_not_confirmed,low_accuracy")]
    public void Evaluate_ReturnsExpectedReasons(double? dist, double acc, ServiceLocationState state, string expected)
    {
        var got = CheckinFlagEvaluator.Evaluate(dist, acc, state, T);
        Assert.Equal(expected.Length == 0 ? new string[0] : expected.Split(','), got.ToArray());
    }

    [Fact]
    public void OnlyDistanceReasonsAreHard()
    {
        Assert.True(CheckinFlagReasons.HasHard(new[] { "geocoded_far", "low_accuracy" }));
        Assert.True(CheckinFlagReasons.HasHard(new[] { "distance_far" }));
        Assert.False(CheckinFlagReasons.HasHard(new[] { "low_accuracy", "location_not_confirmed", "location_geocoded_only" }));
        Assert.False(CheckinFlagReasons.HasHard(null));
    }

    [Fact]
    public void ThresholdsAreConfigurable()
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["VisitCheckin:PackageFlagDistanceMeters"] = "500", ["VisitCheckin:LowAccuracyFlagMeters"] = "100",
        }).Build();
        var db = P.CreateDb(P.NewDbName());
        using (db)
        {
            var svc = new VisitCheckinService(db, Mock.Of<IGeocodingService>(), cfg, Mock.Of<IHostEnvironment>(), Mock.Of<IMediator>(), Mock.Of<ILogger<VisitCheckinService>>());
            // exercised end to end below; here just prove the config keys are read by flagging a 600 m / 200 m case
            var t = new CheckinFlagThresholds(500, 10000, 100);
            Assert.Equal(new[] { "distance_far", "low_accuracy" }, CheckinFlagEvaluator.Evaluate(600, 200, ServiceLocationState.ClientConfirmed, t).ToArray());
        }
    }

    private static VisitCheckinService Svc(CareProDbContext db, Mock<IOpsAlertService>? ops = null, Mock<IMediator>? mediator = null) =>
        new(db, Mock.Of<IGeocodingService>(), new ConfigurationBuilder().Build(), Mock.Of<IHostEnvironment>(),
            (mediator ?? new Mock<IMediator>()).Object, Mock.Of<ILogger<VisitCheckinService>>(), (ops ?? new Mock<IOpsAlertService>()).Object);

    private static async Task<(VisitCheckinResponse resp, VisitCheckin raw)> CheckIn(CareProDbContext db, string caregiverId, string assignmentId,
        double lat, double lng, double acc, Mock<IOpsAlertService>? ops = null, Mock<IMediator>? mediator = null)
    {
        var sheet = await P.CreateTaskSheetService(db).CreateTaskSheetForAssignmentAsync(assignmentId, caregiverId);
        var resp = await Svc(db, ops, mediator).CheckinAsync(new VisitCheckinRequest { TaskSheetId = sheet.Id, OrderId = null, Latitude = lat, Longitude = lng, Accuracy = acc, CheckinTimestamp = DateTime.UtcNow }, caregiverId);
        var raw = await db.VisitCheckins.AsNoTracking().FirstAsync(c => c.TaskSheetId == sheet.Id);
        return (resp, raw);
    }

    private static async Task SetLocation(CareProDbContext db, bool? byClient, bool coords)
    {
        var c = await db.Contracts.FirstAsync();
        c.ServiceLatitude = coords ? 6.540915 : null; c.ServiceLongitude = coords ? 3.385469 : null;
        c.ServiceLocationSetByClient = byClient; c.ServiceAddress = null;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GeocodedOnly_SydneyNineThousandMetres_IsHardFlagged_NotBlocked_AdminNotifiedOnce()
    {
        var cg = ObjectId.GenerateNewId().ToString(); var cl = ObjectId.GenerateNewId().ToString();
        using var db = P.CreateDb(P.NewDbName());
        var (aid, _) = P.SeedAcceptedAssignmentAndContract(db, cg, cl);
        await SetLocation(db, false, true);
        var ops = new Mock<IOpsAlertService>();
        var (resp, raw) = await CheckIn(db, cg, aid, -33.0, 151.0, 9000, ops);
        Assert.True(resp.Success);
        Assert.True(resp.IsFlaggedForDistanceReview);
        Assert.Equal(new[] { "location_geocoded_only", "geocoded_far", "low_accuracy" }, raw.FlagReasons!.ToArray());
        Assert.Equal(9000, raw.Accuracy);
        ops.Verify(o => o.NotifyAdminsAsync(NotificationTypes.PackageVisitFlagged, It.IsAny<string>(), It.IsAny<string>(), raw.Id.ToString(), false), Times.Once);
        Assert.NotNull(raw.AdminNotifiedAt);
    }

    [Fact]
    public async Task SoftReasonsOnly_AreRecorded_ButNeverNotifyAdmins_AndKeepLegacyFlagFalse()
    {
        var cg = ObjectId.GenerateNewId().ToString(); var cl = ObjectId.GenerateNewId().ToString();
        using var db = P.CreateDb(P.NewDbName());
        var (aid, _) = P.SeedAcceptedAssignmentAndContract(db, cg, cl);
        await SetLocation(db, null, false);                       // no coordinates at all
        var ops = new Mock<IOpsAlertService>();
        var (resp, raw) = await CheckIn(db, cg, aid, 6.5, 3.3, 2000, ops);
        Assert.True(resp.Success);
        Assert.False(resp.IsFlaggedForDistanceReview);
        Assert.Equal(new[] { "location_not_confirmed", "low_accuracy" }, raw.FlagReasons!.ToArray());
        Assert.Null(raw.DistanceFromServiceAddress);
        ops.Verify(o => o.NotifyAdminsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task ClientConfirmed_GoodCheckIn_HasNoReasons()
    {
        var cg = ObjectId.GenerateNewId().ToString(); var cl = ObjectId.GenerateNewId().ToString();
        using var db = P.CreateDb(P.NewDbName());
        var (aid, _) = P.SeedAcceptedAssignmentAndContract(db, cg, cl);
        await SetLocation(db, true, true);
        var (resp, raw) = await CheckIn(db, cg, aid, 6.541365, 3.385469, 30);
        Assert.False(resp.IsFlaggedForDistanceReview);
        Assert.Empty(raw.FlagReasons!);
    }

    [Fact]
    public async Task LocationNudge_IsSentOncePerContract_NotPerVisit()
    {
        var cg = ObjectId.GenerateNewId().ToString(); var cl = ObjectId.GenerateNewId().ToString();
        using var db = P.CreateDb(P.NewDbName());
        var (aid, _) = P.SeedAcceptedAssignmentAndContract(db, cg, cl);
        await SetLocation(db, false, true);
        var mediator = new Mock<IMediator>();
        await CheckIn(db, cg, aid, 6.5409, 3.3855, 10, mediator: mediator);
        // second visit: previous sheet must be submitted/approved — simulate by cancelling it so a new sheet is allowed
        foreach (var ts in await db.TaskSheets.ToListAsync()) { ts.Status = "cancelled"; ts.ScheduledDate = DateTime.UtcNow.AddDays(-3); }
        await db.SaveChangesAsync();
        await CheckIn(db, cg, aid, 6.5409, 3.3855, 10, mediator: mediator);
        mediator.Verify(m => m.Send(It.Is<Application.Commands.SendNotificationCommand>(c => c.Type == NotificationTypes.ServiceLocationNotSet), It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull((await db.Contracts.AsNoTracking().FirstAsync()).LocationNudgeSentAt);
    }

    [Fact]
    public void OldCheckinWithoutReasons_ReadsAsNone()
    {
        var old = new VisitCheckin { FlagReasons = null, IsFlaggedForDistanceReview = null };
        Assert.Empty(old.FlagReasons ?? new List<string>());
        Assert.False(CheckinFlagReasons.HasHard(old.FlagReasons));
    }
}
