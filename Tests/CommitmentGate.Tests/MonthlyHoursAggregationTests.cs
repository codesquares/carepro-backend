using Application.DTOs;
using Application.Interfaces.Content;
using Application.Interfaces.Email;
using CloudinaryDotNet;
using Domain.Entities;
using Infrastructure.Content.Data;
using Infrastructure.Content.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MediatR;
using Moq;
using MongoDB.Bson;
using Xunit;

namespace CommitmentGate.Tests;

/// <summary>
/// Phase 9.6 — SUM(TaskSheet.VisitDurationMinutes) grouped by CaregiverId and calendar
/// month, feeding Phase 9.7 payroll. Seeds real TaskSheet documents directly (the way
/// they'd land after real check-in/submit cycles — see PackageAssignmentCheckinAndTaskSheetTests
/// for that end-to-end path) rather than re-running the full check-in flow for every
/// data point, since this is testing the aggregation query itself.
/// Real local MongoDB (replica set on 127.0.0.1:27018, same infra as the other suites).
/// </summary>
public class MonthlyHoursAggregationTests
{
    private static CareProDbContext CreateDb(string databaseName)
    {
        var options = new DbContextOptionsBuilder<CareProDbContext>()
            .UseMongoDB("mongodb://127.0.0.1:27018", databaseName)
            .Options;
        return new TestCareProDbContext(options);
    }

    private static string NewDbName() => $"carepro_hours_agg_tests_{Guid.NewGuid():N}";

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

    private static TaskSheet SubmittedSheet(
        string caregiverId, DateTime scheduledDate, double durationMinutes,
        string? assignmentId = null, int sheetNumber = 1) => new()
    {
        Id = ObjectId.GenerateNewId(),
        OrderId = assignmentId == null ? ObjectId.GenerateNewId().ToString() : string.Empty,
        AssignmentId = assignmentId,
        PackageRequestId = assignmentId != null ? ObjectId.GenerateNewId().ToString() : null,
        CaregiverId = caregiverId,
        SheetNumber = sheetNumber,
        Status = "submitted",
        ScheduledDate = scheduledDate,
        SubmittedAt = scheduledDate.AddHours(4),
        VisitDurationMinutes = durationMinutes,
        CreatedAt = scheduledDate,
        UpdatedAt = scheduledDate
    };

    [Fact]
    public async Task SumsOnlySubmittedSheetsWithinTheCalendarMonth()
    {
        var caregiverId = ObjectId.GenerateNewId().ToString();
        using var db = CreateDb(NewDbName());

        // Three visits inside March 2026, worth 480, 300, 120 minutes.
        db.TaskSheets.AddRange(
            SubmittedSheet(caregiverId, new DateTime(2026, 3, 1), 480, sheetNumber: 1),
            SubmittedSheet(caregiverId, new DateTime(2026, 3, 15), 300, sheetNumber: 2),
            SubmittedSheet(caregiverId, new DateTime(2026, 3, 31), 120, sheetNumber: 3),
            // Outside the month — must not be counted.
            SubmittedSheet(caregiverId, new DateTime(2026, 2, 28), 999, sheetNumber: 4),
            SubmittedSheet(caregiverId, new DateTime(2026, 4, 1), 999, sheetNumber: 5),
            // Different caregiver — must not be counted.
            SubmittedSheet(ObjectId.GenerateNewId().ToString(), new DateTime(2026, 3, 10), 999, sheetNumber: 1)
        );
        await db.SaveChangesAsync();

        var result = await CreateTaskSheetService(db).GetMonthlyHoursForCaregiverAsync(caregiverId, 2026, 3);

        Assert.Equal(3, result.TaskSheetCount);
        Assert.Equal(900, result.TotalMinutes);
        Assert.Equal(15.0, result.TotalHours);
        Assert.Equal(caregiverId, result.CaregiverId);
        Assert.Equal(2026, result.Year);
        Assert.Equal(3, result.Month);
    }

    [Fact]
    public async Task ExcludesUnsubmittedAndCancelledSheets()
    {
        var caregiverId = ObjectId.GenerateNewId().ToString();
        using var db = CreateDb(NewDbName());

        var inProgress = SubmittedSheet(caregiverId, new DateTime(2026, 6, 5), 200, sheetNumber: 1);
        inProgress.Status = "in-progress";
        inProgress.VisitDurationMinutes = null;

        var cancelled = SubmittedSheet(caregiverId, new DateTime(2026, 6, 6), 500, sheetNumber: 2);
        cancelled.Status = "cancelled";

        var actuallySubmitted = SubmittedSheet(caregiverId, new DateTime(2026, 6, 7), 240, sheetNumber: 3);

        db.TaskSheets.AddRange(inProgress, cancelled, actuallySubmitted);
        await db.SaveChangesAsync();

        var result = await CreateTaskSheetService(db).GetMonthlyHoursForCaregiverAsync(caregiverId, 2026, 6);

        Assert.Equal(1, result.TaskSheetCount);
        Assert.Equal(240, result.TotalMinutes);
    }

    [Fact]
    public async Task NoSheetsThatMonth_ReturnsZero()
    {
        using var db = CreateDb(NewDbName());
        var result = await CreateTaskSheetService(db).GetMonthlyHoursForCaregiverAsync(
            ObjectId.GenerateNewId().ToString(), 2026, 1);

        Assert.Equal(0, result.TaskSheetCount);
        Assert.Equal(0, result.TotalMinutes);
        Assert.Equal(0, result.TotalHours);
    }

    [Fact]
    public async Task ScopedToOneAssignment_ExcludesCaregiversOtherConcurrentAssignment()
    {
        var caregiverId = ObjectId.GenerateNewId().ToString();
        var assignmentA = ObjectId.GenerateNewId().ToString();
        var assignmentB = ObjectId.GenerateNewId().ToString();
        using var db = CreateDb(NewDbName());

        db.TaskSheets.AddRange(
            SubmittedSheet(caregiverId, new DateTime(2026, 5, 3), 300, assignmentId: assignmentA, sheetNumber: 1),
            SubmittedSheet(caregiverId, new DateTime(2026, 5, 4), 180, assignmentId: assignmentA, sheetNumber: 2),
            // Same caregiver, different concurrent package assignment — must be excluded when scoped.
            SubmittedSheet(caregiverId, new DateTime(2026, 5, 3), 600, assignmentId: assignmentB, sheetNumber: 1)
        );
        await db.SaveChangesAsync();

        var scoped = await CreateTaskSheetService(db).GetMonthlyHoursForCaregiverAsync(caregiverId, 2026, 5, assignmentA);
        Assert.Equal(2, scoped.TaskSheetCount);
        Assert.Equal(480, scoped.TotalMinutes);
        Assert.Equal(assignmentA, scoped.AssignmentId);

        var unscoped = await CreateTaskSheetService(db).GetMonthlyHoursForCaregiverAsync(caregiverId, 2026, 5);
        Assert.Equal(3, unscoped.TaskSheetCount);
        Assert.Equal(1080, unscoped.TotalMinutes);
    }

    [Fact]
    public async Task InvalidMonth_Throws()
    {
        using var db = CreateDb(NewDbName());
        await Assert.ThrowsAsync<ArgumentException>(() =>
            CreateTaskSheetService(db).GetMonthlyHoursForCaregiverAsync(ObjectId.GenerateNewId().ToString(), 2026, 13));
    }

    /// <summary>
    /// End-to-end: real task sheet data produced by the actual Phase 9.5 check-in/submit
    /// flow (not hand-seeded), then aggregated by this method — the exact scenario the
    /// spec asks to verify before moving on to Phase 9.7.
    /// </summary>
    [Fact]
    public async Task RealCheckinAndSubmitFlow_ProducesCorrectMonthlyAggregate()
    {
        var caregiverId = ObjectId.GenerateNewId().ToString();
        var clientId = ObjectId.GenerateNewId().ToString();
        var packageRequestId = ObjectId.GenerateNewId().ToString();
        using var db = CreateDb(NewDbName());

        var assignment = new Assignment
        {
            Id = ObjectId.GenerateNewId(),
            PackageRequestId = packageRequestId,
            CaregiverId = caregiverId,
            ClientId = clientId,
            Status = AssignmentStatuses.Accepted,
            AssignedByAdminId = ObjectId.GenerateNewId().ToString(),
            AssignedAt = DateTime.UtcNow,
            RespondedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        db.Assignments.Add(assignment);
        db.Contracts.Add(new Contract
        {
            Id = ObjectId.GenerateNewId().ToString(),
            OrderId = string.Empty,
            GigId = string.Empty,
            ClientId = clientId,
            CaregiverId = caregiverId,
            PackageRequestId = packageRequestId,
            Status = ContractStatus.Generated
        });
        await db.SaveChangesAsync();

        var taskSheetService = CreateTaskSheetService(db);
        var checkinService = new VisitCheckinService(db, Mock.Of<IGeocodingService>(),
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
            Mock.Of<Microsoft.Extensions.Hosting.IHostEnvironment>(), Mock.Of<IMediator>(),
            Mock.Of<ILogger<VisitCheckinService>>());

        var taskSheet = await taskSheetService.CreateTaskSheetForAssignmentAsync(assignment.Id.ToString(), caregiverId);
        await checkinService.CheckinAsync(new VisitCheckinRequest
        {
            TaskSheetId = taskSheet.Id,
            OrderId = null,
            Latitude = 6.5,
            Longitude = 3.3,
            Accuracy = 10,
            CheckinTimestamp = DateTime.UtcNow
        }, caregiverId);
        var submitted = await taskSheetService.SubmitTaskSheetAsync(taskSheet.Id, new SubmitTaskSheetRequest(), caregiverId);

        Assert.NotNull(submitted.VisitDurationMinutes);

        var today = DateTime.UtcNow.Date;
        var result = await taskSheetService.GetMonthlyHoursForCaregiverAsync(caregiverId, today.Year, today.Month);

        Assert.Equal(1, result.TaskSheetCount);
        Assert.Equal(submitted.VisitDurationMinutes!.Value, result.TotalMinutes);
    }
}
