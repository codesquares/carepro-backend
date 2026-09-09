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
/// Phase 9.5 — the alternative check-in/task-sheet path for package assignments.
/// Verifies an Accepted Assignment + Generated Contract is sufficient authorization
/// (no ClientOrder needed), that generated records link to AssignmentId/PackageRequestId,
/// and that the legacy Gig/ClientOrder path keeps working unchanged alongside it.
/// Real local MongoDB (replica set on 127.0.0.1:27018, same infra as the other suites).
/// </summary>
public class PackageAssignmentCheckinAndTaskSheetTests
{
    private static CareProDbContext CreateDb(string databaseName)
    {
        var options = new DbContextOptionsBuilder<CareProDbContext>()
            .UseMongoDB("mongodb://127.0.0.1:27018", databaseName)
            .Options;
        return new TestCareProDbContext(options);
    }

    private static string NewDbName() => $"carepro_pkg_checkin_tests_{Guid.NewGuid():N}";

    private static VisitCheckinService CreateCheckinService(CareProDbContext db)
        => new(db, Mock.Of<IGeocodingService>(), new ConfigurationBuilder().Build(), Mock.Of<IHostEnvironment>(),
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

    private static (string assignmentId, string packageRequestId) SeedAcceptedAssignmentAndContract(
        CareProDbContext db, string caregiverId, string clientId)
    {
        var packageRequestId = ObjectId.GenerateNewId().ToString();

        var assignment = new Assignment
        {
            Id = ObjectId.GenerateNewId(),
            PackageRequestId = packageRequestId,
            CaregiverId = caregiverId,
            ClientId = clientId,
            Status = AssignmentStatuses.Accepted,
            AssignedByAdminId = ObjectId.GenerateNewId().ToString(),
            AssignedBy = "staff",
            AssignedAt = DateTime.UtcNow,
            RespondedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        db.Assignments.Add(assignment);

        var contract = new Contract
        {
            Id = ObjectId.GenerateNewId().ToString(),
            OrderId = string.Empty,
            GigId = string.Empty,
            ClientId = clientId,
            CaregiverId = caregiverId,
            PackageRequestId = packageRequestId,
            Status = ContractStatus.Generated
        };
        db.Contracts.Add(contract);

        db.SaveChanges();
        return (assignment.Id.ToString(), packageRequestId);
    }

    // ───────────────────── Task sheet creation ─────────────────────

    [Fact]
    public async Task CreateTaskSheetForAssignment_AcceptedAssignment_Succeeds_LinksAssignmentAndPackageRequest()
    {
        var dbName = NewDbName();
        var caregiverId = ObjectId.GenerateNewId().ToString();
        var clientId = ObjectId.GenerateNewId().ToString();
        using var db = CreateDb(dbName);
        var (assignmentId, packageRequestId) = SeedAcceptedAssignmentAndContract(db, caregiverId, clientId);

        var result = await CreateTaskSheetService(db).CreateTaskSheetForAssignmentAsync(assignmentId, caregiverId);

        Assert.Equal(assignmentId, result.AssignmentId);
        Assert.Equal(packageRequestId, result.PackageRequestId);
        Assert.Equal(string.Empty, result.OrderId);
        Assert.Equal("in-progress", result.Status);
        Assert.Equal(1, result.SheetNumber);

        var raw = await db.TaskSheets.FirstAsync(t => t.Id == ObjectId.Parse(result.Id));
        Assert.Equal(assignmentId, raw.AssignmentId);
        Assert.Equal(packageRequestId, raw.PackageRequestId);
    }

    [Fact]
    public async Task CreateTaskSheetForAssignment_WrongCaregiver_ThrowsUnauthorized()
    {
        var dbName = NewDbName();
        var caregiverId = ObjectId.GenerateNewId().ToString();
        var clientId = ObjectId.GenerateNewId().ToString();
        using var db = CreateDb(dbName);
        var (assignmentId, _) = SeedAcceptedAssignmentAndContract(db, caregiverId, clientId);

        var otherCaregiverId = ObjectId.GenerateNewId().ToString();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            CreateTaskSheetService(db).CreateTaskSheetForAssignmentAsync(assignmentId, otherCaregiverId));
    }

    [Fact]
    public async Task CreateTaskSheetForAssignment_NotYetAccepted_Throws()
    {
        var dbName = NewDbName();
        var caregiverId = ObjectId.GenerateNewId().ToString();
        using var db = CreateDb(dbName);

        var assignment = new Assignment
        {
            Id = ObjectId.GenerateNewId(),
            PackageRequestId = ObjectId.GenerateNewId().ToString(),
            CaregiverId = caregiverId,
            ClientId = ObjectId.GenerateNewId().ToString(),
            Status = AssignmentStatuses.PendingAcceptance,
            AssignedByAdminId = ObjectId.GenerateNewId().ToString(),
            AssignedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        db.Assignments.Add(assignment);
        db.SaveChanges();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateTaskSheetService(db).CreateTaskSheetForAssignmentAsync(assignment.Id.ToString(), caregiverId));
    }

    [Fact]
    public async Task CreateTaskSheetForAssignment_SecondSheetSameDay_Throws()
    {
        var dbName = NewDbName();
        var caregiverId = ObjectId.GenerateNewId().ToString();
        var clientId = ObjectId.GenerateNewId().ToString();
        using var db = CreateDb(dbName);
        var (assignmentId, _) = SeedAcceptedAssignmentAndContract(db, caregiverId, clientId);

        await CreateTaskSheetService(db).CreateTaskSheetForAssignmentAsync(assignmentId, caregiverId);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateTaskSheetService(db).CreateTaskSheetForAssignmentAsync(assignmentId, caregiverId));
    }

    // ───────────────────── Check-in ─────────────────────

    [Fact]
    public async Task Checkin_PackageAssignmentTaskSheet_Succeeds_NoOrderIdNeeded()
    {
        var dbName = NewDbName();
        var caregiverId = ObjectId.GenerateNewId().ToString();
        var clientId = ObjectId.GenerateNewId().ToString();
        using var db = CreateDb(dbName);
        var (assignmentId, packageRequestId) = SeedAcceptedAssignmentAndContract(db, caregiverId, clientId);
        var taskSheet = await CreateTaskSheetService(db).CreateTaskSheetForAssignmentAsync(assignmentId, caregiverId);

        var response = await CreateCheckinService(db).CheckinAsync(new VisitCheckinRequest
        {
            TaskSheetId = taskSheet.Id,
            OrderId = null,
            Latitude = 6.5,
            Longitude = 3.3,
            Accuracy = 10,
            CheckinTimestamp = DateTime.UtcNow
        }, caregiverId);

        Assert.True(response.Success);

        var raw = await db.VisitCheckins.FirstAsync(c => c.TaskSheetId == taskSheet.Id);
        Assert.Equal(assignmentId, raw.AssignmentId);
        Assert.Equal(packageRequestId, raw.PackageRequestId);
        Assert.Equal(string.Empty, raw.OrderId);
    }

    [Fact]
    public async Task Checkin_PackageAssignment_WrongCaregiver_ThrowsUnauthorized()
    {
        var dbName = NewDbName();
        var caregiverId = ObjectId.GenerateNewId().ToString();
        var clientId = ObjectId.GenerateNewId().ToString();
        using var db = CreateDb(dbName);
        var (assignmentId, _) = SeedAcceptedAssignmentAndContract(db, caregiverId, clientId);
        var taskSheet = await CreateTaskSheetService(db).CreateTaskSheetForAssignmentAsync(assignmentId, caregiverId);

        var otherCaregiverId = ObjectId.GenerateNewId().ToString();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => CreateCheckinService(db).CheckinAsync(new VisitCheckinRequest
        {
            TaskSheetId = taskSheet.Id,
            OrderId = null,
            Latitude = 6.5,
            Longitude = 3.3,
            Accuracy = 10,
            CheckinTimestamp = DateTime.UtcNow
        }, otherCaregiverId));
    }

    // ───────────────────── End-to-end: check-in → submit → real VisitDurationMinutes ─────────────────────

    [Fact]
    public async Task PackageAssignment_CheckinThenSubmit_ProducesRealVisitDurationMinutes()
    {
        var dbName = NewDbName();
        var caregiverId = ObjectId.GenerateNewId().ToString();
        var clientId = ObjectId.GenerateNewId().ToString();
        using var db = CreateDb(dbName);
        var (assignmentId, _) = SeedAcceptedAssignmentAndContract(db, caregiverId, clientId);

        var taskSheet = await CreateTaskSheetService(db).CreateTaskSheetForAssignmentAsync(assignmentId, caregiverId);

        var checkinResponse = await CreateCheckinService(db).CheckinAsync(new VisitCheckinRequest
        {
            TaskSheetId = taskSheet.Id,
            OrderId = null,
            Latitude = 6.5,
            Longitude = 3.3,
            Accuracy = 10,
            CheckinTimestamp = DateTime.UtcNow
        }, caregiverId);
        Assert.True(checkinResponse.Success);

        var submitted = await CreateTaskSheetService(db).SubmitTaskSheetAsync(
            taskSheet.Id, new SubmitTaskSheetRequest(), caregiverId);

        Assert.Equal("submitted", submitted.Status);
        Assert.NotNull(submitted.VisitDurationMinutes);
        Assert.True(submitted.VisitDurationMinutes >= 0);
    }

    // ───────────────────── Legacy path still works, unaffected ─────────────────────

    [Fact]
    public async Task Checkin_LegacyOrderWithoutAssignmentId_StillRequiresOrderId()
    {
        using var db = CreateDb(NewDbName());
        var caregiverId = ObjectId.GenerateNewId().ToString();

        // A legacy-shaped task sheet (no AssignmentId) with OrderId omitted must be rejected,
        // proving the branch selection didn't loosen the legacy path's requirements.
        var taskSheet = new TaskSheet
        {
            Id = ObjectId.GenerateNewId(),
            OrderId = ObjectId.GenerateNewId().ToString(),
            CaregiverId = caregiverId,
            SheetNumber = 1,
            Status = "in-progress"
        };
        db.TaskSheets.Add(taskSheet);
        db.SaveChanges();

        await Assert.ThrowsAsync<ArgumentException>(() => CreateCheckinService(db).CheckinAsync(new VisitCheckinRequest
        {
            TaskSheetId = taskSheet.Id.ToString(),
            OrderId = null,
            Latitude = 6.5,
            Longitude = 3.3,
            Accuracy = 10,
            CheckinTimestamp = DateTime.UtcNow
        }, caregiverId));
    }
}
