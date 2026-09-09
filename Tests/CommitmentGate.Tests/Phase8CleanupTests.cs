using Application.Interfaces.Authentication;
using Application.Interfaces.Common;
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
/// Phase 8.2 — UserDeletionService no longer relies on Gig/ClientOrder to detect a
/// caregiver's genuine active work. It now blocks deletion when an active Assignment
/// (or a confirmed PackageRequest) exists.
/// </summary>
public class Phase8CleanupTests
{
    private static CareProDbContext CreateDb(string name)
    {
        var options = new DbContextOptionsBuilder<CareProDbContext>()
            .UseMongoDB("mongodb://127.0.0.1:27018", name).Options;
        return new TestCareProDbContext(options);
    }

    private static string NewDbName() => $"carepro_phase8_{Guid.NewGuid():N}";

    private static UserDeletionService CreateService(CareProDbContext db) => new(
        db, Mock.Of<IMediator>(), Mock.Of<IEmailService>(),
        Mock.Of<ILogger<UserDeletionService>>(), Mock.Of<ITokenHandler>(),
        Mock.Of<IOriginValidationService>());

    private static Caregiver SeedCaregiver(CareProDbContext db)
    {
        var cg = new Caregiver
        {
            Id = ObjectId.GenerateNewId(),
            FirstName = "Del", LastName = "Etion",
            Email = $"cg-{Guid.NewGuid():N}@example.com",
            Password = "x", // pragma: allowlist-secret
            Role = "Caregiver", Status = true, IsAvailable = true, IsDeleted = false,
            CreatedAt = DateTime.UtcNow
        };
        db.CareGivers.Add(cg);
        db.SaveChanges();
        return cg;
    }

    private static void AddAssignment(CareProDbContext db, string caregiverId, string status)
    {
        db.Assignments.Add(new Assignment
        {
            Id = ObjectId.GenerateNewId(),
            PackageRequestId = ObjectId.GenerateNewId().ToString(),
            CaregiverId = caregiverId,
            ClientId = ObjectId.GenerateNewId().ToString(),
            Status = status,
            AssignedByAdminId = "admin-1",
            AssignedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task CaregiverWithAcceptedAssignment_CannotBeDeleted()
    {
        using var db = CreateDb(NewDbName());
        var cg = SeedCaregiver(db);
        AddAssignment(db, cg.Id.ToString(), AssignmentStatuses.Accepted);

        var result = await CreateService(db).RequestCaregiverAccountDeletionAsync(cg.Id.ToString(), "leaving");

        Assert.False(result.Success);
        Assert.Contains(result.Blockers, b => b.Contains("active care assignment"));
    }

    [Fact]
    public async Task CaregiverWithPendingAcceptanceAssignment_CannotBeDeleted()
    {
        using var db = CreateDb(NewDbName());
        var cg = SeedCaregiver(db);
        AddAssignment(db, cg.Id.ToString(), AssignmentStatuses.PendingAcceptance);

        var result = await CreateService(db).RequestCaregiverAccountDeletionAsync(cg.Id.ToString(), "leaving");

        Assert.False(result.Success);
        Assert.NotEmpty(result.Blockers);
    }

    [Fact]
    public async Task CaregiverWithConfirmedPackageRequest_ButOrphanedAssignment_CannotBeDeleted()
    {
        using var db = CreateDb(NewDbName());
        var cg = SeedCaregiver(db);
        db.PackageRequests.Add(new PackageRequest
        {
            Id = ObjectId.GenerateNewId(),
            ClientId = ObjectId.GenerateNewId().ToString(),
            PackageId = ObjectId.GenerateNewId().ToString(),
            PackageCategory = "Post Surgery Care",
            RequiredCaregiverType = CaregiverType.RegisteredNurse,
            ServiceCategory = "Post Surgery Care",
            Status = PackageRequestStatuses.Confirmed,
            ConfirmedCaregiverId = cg.Id.ToString(),
            ConfirmedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await CreateService(db).RequestCaregiverAccountDeletionAsync(cg.Id.ToString(), "leaving");

        Assert.False(result.Success);
        Assert.NotEmpty(result.Blockers);
    }

    [Fact]
    public async Task CaregiverWithNoActiveWork_CanBeDeleted()
    {
        using var db = CreateDb(NewDbName());
        var cg = SeedCaregiver(db);

        var result = await CreateService(db).RequestCaregiverAccountDeletionAsync(cg.Id.ToString(), "leaving");

        Assert.True(result.Success, $"Unexpected blockers: {string.Join("; ", result.Blockers ?? new())}");
        Assert.NotNull(result.PermanentDeletionDate);
    }

    [Fact]
    public async Task CaregiverWithOnlyDeclinedOrCancelledAssignments_CanBeDeleted()
    {
        using var db = CreateDb(NewDbName());
        var cg = SeedCaregiver(db);
        AddAssignment(db, cg.Id.ToString(), AssignmentStatuses.Declined);
        AddAssignment(db, cg.Id.ToString(), AssignmentStatuses.Cancelled);

        var result = await CreateService(db).RequestCaregiverAccountDeletionAsync(cg.Id.ToString(), "leaving");

        Assert.True(result.Success, $"Unexpected blockers: {string.Join("; ", result.Blockers ?? new())}");
    }

    [Fact]
    public async Task OtherCaregiversActiveAssignment_DoesNotBlockThisCaregiver()
    {
        using var db = CreateDb(NewDbName());
        var me = SeedCaregiver(db);
        var other = SeedCaregiver(db);
        AddAssignment(db, other.Id.ToString(), AssignmentStatuses.Accepted);

        var result = await CreateService(db).RequestCaregiverAccountDeletionAsync(me.Id.ToString(), "leaving");

        Assert.True(result.Success, $"Unexpected blockers: {string.Join("; ", result.Blockers ?? new())}");
    }
}
