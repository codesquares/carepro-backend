using Application.DTOs;
using Application.Interfaces.Content;
using Application.Interfaces.Email;
using CloudinaryDotNet;
using Domain.Entities;
using Infrastructure.Content.Data;
using Infrastructure.Content.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MediatR;
using Moq;
using MongoDB.Bson;
using Xunit;

namespace CommitmentGate.Tests;

/// <summary>
/// Direct regression proof, requested after Phase 9.5 shipped: does the pre-existing
/// Gig/ClientOrder check-in → submit flow still work end to end, unchanged, now that
/// VisitCheckinService/TaskSheetService branch on TaskSheet.AssignmentId to support the
/// new package-assignment path? This exercises ONLY the legacy path (a TaskSheet with no
/// AssignmentId, OrderId + Approved Contract as authorization) through the real
/// CheckinAsync → SubmitTaskSheetAsync chain — no package/Assignment data involved at all.
/// Real local MongoDB (replica set on 127.0.0.1:27018, same infra as the other suites).
/// </summary>
public class LegacyCheckinEndToEndRegressionTests
{
    private static CareProDbContext CreateDb(string databaseName)
    {
        var options = new DbContextOptionsBuilder<CareProDbContext>()
            .UseMongoDB("mongodb://127.0.0.1:27018", databaseName)
            .Options;
        return new TestCareProDbContext(options);
    }

    private static string NewDbName() => $"carepro_legacy_e2e_{Guid.NewGuid():N}";

    private static VisitCheckinService CreateCheckinService(CareProDbContext db)
        => new(db, Mock.Of<IGeocodingService>(), new ConfigurationBuilder().Build(), Mock.Of<IHostEnvironment>(),
            Mock.Of<IMediator>(), Mock.Of<ILogger<VisitCheckinService>>());

    private static CloudinaryService CreateCloudinaryService()
    {
        var cloudinary = new Cloudinary(new Account("default", "default", "default"));
        var options = Options.Create(new CloudinarySettings
        { CloudName = "default", ApiKey = "default", ApiSecret = "default" });
        return new CloudinaryService(cloudinary, options);
    }

    private static TaskSheetService CreateTaskSheetService(CareProDbContext db)
        => new(db, CreateCloudinaryService(), Mock.Of<IMediator>(), Mock.Of<IEmailService>(),
            Mock.Of<ILogger<TaskSheetService>>());

    /// <summary>
    /// Seeds exactly the legacy shape: ClientOrder + Approved Contract + TaskSheet with
    /// OrderId set and AssignmentId/PackageRequestId left null — no package data anywhere.
    /// </summary>
    private static (string orderId, string taskSheetId, string clientId) SeedLegacyOrderContractAndTaskSheet(
        CareProDbContext db, string caregiverId, bool withScheduleWindow = true)
    {
        var nigeria = TimeZoneInfo.FindSystemTimeZoneById("Africa/Lagos");
        var todayNigeria = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, nigeria).Date;
        var clientId = ObjectId.GenerateNewId().ToString();

        var order = new ClientOrder
        {
            Id = ObjectId.GenerateNewId(),
            ClientId = clientId,
            GigId = ObjectId.GenerateNewId().ToString(),
            CaregiverId = caregiverId,
            PaymentOption = "full",
            Amount = 100000,
            TransactionId = string.Empty
        };
        db.ClientOrders.Add(order);

        var contract = new Contract
        {
            Id = ObjectId.GenerateNewId().ToString(),
            OrderId = order.Id.ToString(),
            GigId = order.GigId,
            ClientId = clientId,
            CaregiverId = caregiverId,
            Status = ContractStatus.Approved
            // PackageRequestId/PackageId intentionally left null — legacy contract.
        };
        db.Contracts.Add(contract);

        var taskSheet = new TaskSheet
        {
            Id = ObjectId.GenerateNewId(),
            OrderId = order.Id.ToString(),
            // AssignmentId / PackageRequestId intentionally left null — legacy task sheet.
            CaregiverId = caregiverId,
            SheetNumber = 1,
            Status = "in-progress",
            ScheduledDate = todayNigeria,
            ScheduledStartTime = withScheduleWindow ? "00:00" : null,
            ScheduledEndTime = withScheduleWindow ? "23:59" : null
        };
        db.TaskSheets.Add(taskSheet);

        db.SaveChanges();
        return (order.Id.ToString(), taskSheet.Id.ToString(), clientId);
    }

    [Fact]
    public async Task Legacy_CheckinThenSubmit_EndToEnd_Unchanged_AfterPhase95Branching()
    {
        var dbName = NewDbName();
        var caregiverId = ObjectId.GenerateNewId().ToString();
        using var db = CreateDb(dbName);
        var (orderId, taskSheetId, clientId) = SeedLegacyOrderContractAndTaskSheet(db, caregiverId);

        // Real check-in through the legacy OrderId + ClientOrder + Approved Contract path.
        var checkinResponse = await CreateCheckinService(db).CheckinAsync(new VisitCheckinRequest
        {
            TaskSheetId = taskSheetId,
            OrderId = orderId,
            Latitude = 6.5,
            Longitude = 3.3,
            Accuracy = 10,
            CheckinTimestamp = DateTime.UtcNow
        }, caregiverId);

        Assert.True(checkinResponse.Success);
        Assert.False(checkinResponse.AlreadyCheckedIn);

        var rawCheckin = await db.VisitCheckins.FirstAsync(c => c.TaskSheetId == taskSheetId);
        Assert.Equal(orderId, rawCheckin.OrderId);
        Assert.Null(rawCheckin.AssignmentId);
        Assert.Null(rawCheckin.PackageRequestId);

        // Backdate the server-authoritative check-in time to simulate a real ~90-minute
        // visit — check-in and submit otherwise happen milliseconds apart in a test.
        rawCheckin.ServerReceivedAt = DateTime.UtcNow.AddMinutes(-90);
        await db.SaveChangesAsync();

        // Real submission through the same legacy path.
        var submitted = await CreateTaskSheetService(db).SubmitTaskSheetAsync(
            taskSheetId, new SubmitTaskSheetRequest(), caregiverId);

        Assert.Equal("submitted", submitted.Status);
        Assert.Null(submitted.AssignmentId);
        Assert.Equal(orderId, submitted.OrderId);
        Assert.NotNull(submitted.VisitDurationMinutes);
        Assert.InRange(submitted.VisitDurationMinutes!.Value, 89, 91);

        var rawSheet = await db.TaskSheets.FirstAsync(t => t.Id == ObjectId.Parse(taskSheetId));
        Assert.Equal("submitted", rawSheet.Status);
        Assert.Equal(orderId, rawSheet.OrderId);
        Assert.Null(rawSheet.AssignmentId);
    }

    /// <summary>
    /// Idempotent re-checkin still returns the legacy-shaped response unchanged — proves
    /// the "already checked in" short-circuit (evaluated before the 9.5 branch) wasn't
    /// altered either.
    /// </summary>
    [Fact]
    public async Task Legacy_Checkin_Idempotent_ReturnsAlreadyCheckedIn()
    {
        var dbName = NewDbName();
        var caregiverId = ObjectId.GenerateNewId().ToString();
        using var db = CreateDb(dbName);
        var (orderId, taskSheetId, _) = SeedLegacyOrderContractAndTaskSheet(db, caregiverId);

        var request = new VisitCheckinRequest
        {
            TaskSheetId = taskSheetId,
            OrderId = orderId,
            Latitude = 6.5,
            Longitude = 3.3,
            Accuracy = 10,
            CheckinTimestamp = DateTime.UtcNow
        };

        var first = await CreateCheckinService(db).CheckinAsync(request, caregiverId);
        Assert.False(first.AlreadyCheckedIn);

        var second = await CreateCheckinService(db).CheckinAsync(request, caregiverId);
        Assert.True(second.AlreadyCheckedIn);
        Assert.Equal(first.CheckinId, second.CheckinId);
    }

    /// <summary>
    /// Proves the legacy path's strict schedule enforcement is fully intact — it must
    /// still throw when there's no time-window data, unlike the package-assignment path's
    /// deliberate allowMissingSchedule carve-out. If this ever passed instead of throwing,
    /// it would mean the 9.5 relaxation leaked into the legacy flow.
    /// </summary>
    [Fact]
    public async Task Legacy_NoScheduleData_StillThrows_RelaxationDidNotLeakIntoLegacyPath()
    {
        var dbName = NewDbName();
        var caregiverId = ObjectId.GenerateNewId().ToString();
        using var db = CreateDb(dbName);
        var (orderId, taskSheetId, _) = SeedLegacyOrderContractAndTaskSheet(db, caregiverId, withScheduleWindow: false);
        // Contract.Schedule is also empty (default), so there is genuinely no time-window
        // data anywhere for this legacy sheet — exactly the situation Phase 9.5 relaxed
        // for package assignments only.

        await Assert.ThrowsAsync<CheckinValidationException>(() => CreateCheckinService(db).CheckinAsync(new VisitCheckinRequest
        {
            TaskSheetId = taskSheetId,
            OrderId = orderId,
            Latitude = 6.5,
            Longitude = 3.3,
            Accuracy = 10,
            CheckinTimestamp = DateTime.UtcNow
        }, caregiverId));
    }

    /// <summary>
    /// Wrong caregiver / wrong order-ownership rejection is unchanged.
    /// </summary>
    [Fact]
    public async Task Legacy_WrongCaregiver_ThrowsUnauthorized_Unchanged()
    {
        var dbName = NewDbName();
        var caregiverId = ObjectId.GenerateNewId().ToString();
        using var db = CreateDb(dbName);
        var (orderId, taskSheetId, _) = SeedLegacyOrderContractAndTaskSheet(db, caregiverId);

        var otherCaregiverId = ObjectId.GenerateNewId().ToString();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => CreateCheckinService(db).CheckinAsync(new VisitCheckinRequest
        {
            TaskSheetId = taskSheetId,
            OrderId = orderId,
            Latitude = 6.5,
            Longitude = 3.3,
            Accuracy = 10,
            CheckinTimestamp = DateTime.UtcNow
        }, otherCaregiverId));
    }

    /// <summary>
    /// Legacy check-in without an approved contract still throws — proves the
    /// order-branch's contract lookup (Approved/Accepted, matched by OrderId) is intact.
    /// </summary>
    [Fact]
    public async Task Legacy_NoApprovedContract_StillThrows()
    {
        var dbName = NewDbName();
        var caregiverId = ObjectId.GenerateNewId().ToString();
        using var db = CreateDb(dbName);

        var order = new ClientOrder
        {
            Id = ObjectId.GenerateNewId(),
            ClientId = ObjectId.GenerateNewId().ToString(),
            GigId = ObjectId.GenerateNewId().ToString(),
            CaregiverId = caregiverId,
            PaymentOption = "full",
            Amount = 100000,
            TransactionId = string.Empty
        };
        db.ClientOrders.Add(order);
        // No Contract seeded at all.
        var taskSheet = new TaskSheet
        {
            Id = ObjectId.GenerateNewId(),
            OrderId = order.Id.ToString(),
            CaregiverId = caregiverId,
            SheetNumber = 1,
            Status = "in-progress"
        };
        db.TaskSheets.Add(taskSheet);
        db.SaveChanges();

        await Assert.ThrowsAsync<CheckinValidationException>(() => CreateCheckinService(db).CheckinAsync(new VisitCheckinRequest
        {
            TaskSheetId = taskSheet.Id.ToString(),
            OrderId = order.Id.ToString(),
            Latitude = 6.5,
            Longitude = 3.3,
            Accuracy = 10,
            CheckinTimestamp = DateTime.UtcNow
        }, caregiverId));
    }
}
