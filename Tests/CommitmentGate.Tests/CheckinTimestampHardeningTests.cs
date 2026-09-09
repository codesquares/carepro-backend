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
/// Phase 9.4 — server-authoritative check-in timestamps. Verifies VisitCheckinService
/// captures ServerReceivedAt and flags large device/server discrepancies, and that
/// TaskSheetService's visit-duration calculation uses ServerReceivedAt (falling back to
/// the legacy device-supplied CheckinTimestamp only for pre-Phase-9.4 records).
/// Real local MongoDB (replica set on 127.0.0.1:27018, same infra as the other suites).
/// </summary>
public class CheckinTimestampHardeningTests
{
    private static CareProDbContext CreateDb(string databaseName)
    {
        var options = new DbContextOptionsBuilder<CareProDbContext>()
            .UseMongoDB("mongodb://127.0.0.1:27018", databaseName)
            .Options;
        return new TestCareProDbContext(options);
    }

    private static string NewDbName() => $"carepro_checkin_tests_{Guid.NewGuid():N}";

    private static IConfiguration EmptyConfig() => new ConfigurationBuilder().Build();

    private static VisitCheckinService CreateCheckinService(CareProDbContext db, IConfiguration? config = null)
        => new(db, Mock.Of<IGeocodingService>(), config ?? EmptyConfig(), Mock.Of<IHostEnvironment>(),
            Mock.Of<IMediator>(), Mock.Of<ILogger<VisitCheckinService>>());

    private static CloudinaryService CreateCloudinaryService()
    {
        var cloudinary = new Cloudinary(new Account("default", "default", "default"));
        var options = Options.Create(new CloudinarySettings
        {
            CloudName = "default",
            ApiKey = "default",
            ApiSecret = "default"
        });
        return new CloudinaryService(cloudinary, options);
    }

    private static TaskSheetService CreateTaskSheetService(CareProDbContext db)
        => new(db, CreateCloudinaryService(), Mock.Of<IMediator>(), Mock.Of<IEmailService>(),
            Mock.Of<ILogger<TaskSheetService>>());

    /// <summary>
    /// Seeds a ClientOrder + Approved Contract + in-progress TaskSheet with a wide-open
    /// schedule window (00:00-23:59 today, Nigerian time) so ValidateSchedule always
    /// passes regardless of when the test actually runs, and no GPS coordinates so
    /// proximity validation is skipped entirely.
    /// </summary>
    private static (string orderId, string taskSheetId) SeedOrderContractAndTaskSheet(
        CareProDbContext db, string caregiverId)
    {
        var nigeria = TimeZoneInfo.FindSystemTimeZoneById("Africa/Lagos");
        var todayNigeria = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, nigeria).Date;

        var order = new ClientOrder
        {
            Id = ObjectId.GenerateNewId(),
            ClientId = string.Empty,
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
            ClientId = string.Empty,
            CaregiverId = caregiverId,
            Status = ContractStatus.Approved
        };
        db.Contracts.Add(contract);

        var taskSheet = new TaskSheet
        {
            Id = ObjectId.GenerateNewId(),
            OrderId = order.Id.ToString(),
            CaregiverId = caregiverId,
            SheetNumber = 1,
            Status = "in-progress",
            ScheduledDate = todayNigeria,
            ScheduledStartTime = "00:00",
            ScheduledEndTime = "23:59"
        };
        db.TaskSheets.Add(taskSheet);

        db.SaveChanges();
        return (order.Id.ToString(), taskSheet.Id.ToString());
    }

    // ───────────────────── Discrepancy detection at check-in ─────────────────────

    [Fact]
    public async Task Checkin_NormalDeviceClock_NotFlagged_AndServerReceivedAtCaptured()
    {
        var dbName = NewDbName();
        var caregiverId = ObjectId.GenerateNewId().ToString();
        using var db = CreateDb(dbName);
        var (orderId, taskSheetId) = SeedOrderContractAndTaskSheet(db, caregiverId);

        var beforeCall = DateTime.UtcNow;
        var response = await CreateCheckinService(db).CheckinAsync(new VisitCheckinRequest
        {
            TaskSheetId = taskSheetId,
            OrderId = orderId,
            Latitude = 6.5,
            Longitude = 3.3,
            Accuracy = 10,
            CheckinTimestamp = DateTime.UtcNow
        }, caregiverId);
        var afterCall = DateTime.UtcNow;

        Assert.True(response.Success);
        Assert.False(response.HasTimestampDiscrepancy);

        var raw = await db.VisitCheckins.FirstAsync(c => c.TaskSheetId == taskSheetId);
        Assert.NotNull(raw.ServerReceivedAt);
        Assert.InRange(raw.ServerReceivedAt!.Value, beforeCall.AddSeconds(-1), afterCall.AddSeconds(1));
        Assert.False(raw.HasTimestampDiscrepancy);
        Assert.NotNull(raw.TimestampDiscrepancySeconds);
        Assert.True(raw.TimestampDiscrepancySeconds < 300);
    }

    [Fact]
    public async Task Checkin_LargeDeviceServerGap_IsFlagged()
    {
        var dbName = NewDbName();
        var caregiverId = ObjectId.GenerateNewId().ToString();
        using var db = CreateDb(dbName);
        var (orderId, taskSheetId) = SeedOrderContractAndTaskSheet(db, caregiverId);

        // Device clock is 3 hours ahead of the server — well past any real network latency.
        var response = await CreateCheckinService(db).CheckinAsync(new VisitCheckinRequest
        {
            TaskSheetId = taskSheetId,
            OrderId = orderId,
            Latitude = 6.5,
            Longitude = 3.3,
            Accuracy = 10,
            CheckinTimestamp = DateTime.UtcNow.AddHours(3)
        }, caregiverId);

        Assert.True(response.Success);
        Assert.True(response.HasTimestampDiscrepancy);

        var raw = await db.VisitCheckins.FirstAsync(c => c.TaskSheetId == taskSheetId);
        Assert.True(raw.HasTimestampDiscrepancy);
        Assert.True(raw.TimestampDiscrepancySeconds > 300);
    }

    [Fact]
    public async Task Checkin_JustUnderThreshold_NotFlagged_JustOverThreshold_Flagged()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["VisitCheckin:TimestampDiscrepancyThresholdSeconds"] = "60"
        }).Build();

        var dbName = NewDbName();

        var caregiver1 = ObjectId.GenerateNewId().ToString();
        using (var db = CreateDb(dbName))
        {
            var (orderId, taskSheetId) = SeedOrderContractAndTaskSheet(db, caregiver1);
            var response = await CreateCheckinService(db, config).CheckinAsync(new VisitCheckinRequest
            {
                TaskSheetId = taskSheetId,
                OrderId = orderId,
                Latitude = 6.5,
                Longitude = 3.3,
                Accuracy = 10,
                CheckinTimestamp = DateTime.UtcNow.AddSeconds(30)
            }, caregiver1);
            Assert.False(response.HasTimestampDiscrepancy);
        }

        var caregiver2 = ObjectId.GenerateNewId().ToString();
        using (var db = CreateDb(dbName))
        {
            var (orderId, taskSheetId) = SeedOrderContractAndTaskSheet(db, caregiver2);
            var response = await CreateCheckinService(db, config).CheckinAsync(new VisitCheckinRequest
            {
                TaskSheetId = taskSheetId,
                OrderId = orderId,
                Latitude = 6.5,
                Longitude = 3.3,
                Accuracy = 10,
                CheckinTimestamp = DateTime.UtcNow.AddSeconds(90)
            }, caregiver2);
            Assert.True(response.HasTimestampDiscrepancy);
        }
    }

    // ───────── Visit-duration calculation uses ServerReceivedAt, not CheckinTimestamp ─────────

    [Fact]
    public async Task SubmitTaskSheet_UsesServerReceivedAt_NotDeviceCheckinTimestamp()
    {
        var dbName = NewDbName();
        var caregiverId = ObjectId.GenerateNewId().ToString();
        string taskSheetId;
        DateTime serverReceivedAt;

        using (var db = CreateDb(dbName))
        {
            (_, taskSheetId) = SeedOrderContractAndTaskSheet(db, caregiverId);

            // Device clock claims check-in was 3 hours earlier than it actually was.
            serverReceivedAt = DateTime.UtcNow.AddMinutes(-30);
            db.VisitCheckins.Add(new VisitCheckin
            {
                Id = ObjectId.GenerateNewId(),
                TaskSheetId = taskSheetId,
                OrderId = (await db.TaskSheets.FirstAsync(t => t.Id == ObjectId.Parse(taskSheetId))).OrderId,
                CaregiverId = caregiverId,
                CheckinTimestamp = serverReceivedAt.AddHours(-3), // wildly wrong device clock
                ServerReceivedAt = serverReceivedAt,
                HasTimestampDiscrepancy = true,
                TimestampDiscrepancySeconds = 10800
            });
            await db.SaveChangesAsync();
        }

        using (var db = CreateDb(dbName))
        {
            var result = await CreateTaskSheetService(db).SubmitTaskSheetAsync(
                taskSheetId, new SubmitTaskSheetRequest(), caregiverId);

            // ~30 minutes, computed from ServerReceivedAt — not ~3.5 hours, which is what
            // the spoofed/incorrect device CheckinTimestamp would have produced.
            Assert.NotNull(result.VisitDurationMinutes);
            Assert.InRange(result.VisitDurationMinutes!.Value, 29, 31);
        }
    }

    [Fact]
    public async Task SubmitTaskSheet_LegacyCheckinWithoutServerReceivedAt_FallsBackToDeviceTimestamp()
    {
        var dbName = NewDbName();
        var caregiverId = ObjectId.GenerateNewId().ToString();
        string taskSheetId;

        using (var db = CreateDb(dbName))
        {
            (_, taskSheetId) = SeedOrderContractAndTaskSheet(db, caregiverId);

            // Simulates a real VisitCheckin document created before Phase 9.4 shipped.
            db.VisitCheckins.Add(new VisitCheckin
            {
                Id = ObjectId.GenerateNewId(),
                TaskSheetId = taskSheetId,
                OrderId = (await db.TaskSheets.FirstAsync(t => t.Id == ObjectId.Parse(taskSheetId))).OrderId,
                CaregiverId = caregiverId,
                CheckinTimestamp = DateTime.UtcNow.AddMinutes(-45)
                // ServerReceivedAt intentionally left unset.
            });
            await db.SaveChangesAsync();
        }

        using (var db = CreateDb(dbName))
        {
            var result = await CreateTaskSheetService(db).SubmitTaskSheetAsync(
                taskSheetId, new SubmitTaskSheetRequest(), caregiverId);

            Assert.NotNull(result.VisitDurationMinutes);
            Assert.InRange(result.VisitDurationMinutes!.Value, 44, 46);
        }
    }
}
