using System.Text.Json;
using Application.DTOs;
using Application.Interfaces.Content;
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
/// The caregiver-facing assignment DTO must withhold the client's surname, id, phone, street address and the
/// client-written Notes until the caregiver accepts — enforced in the API response itself, not the UI.
/// Real local MongoDB (replica set on 127.0.0.1:27018).
/// </summary>
public class CareBriefPrivacyTests
{
    private static CareProDbContext CreateDb() =>
        new TestCareProDbContext(new DbContextOptionsBuilder<CareProDbContext>()
            .UseMongoDB("mongodb://127.0.0.1:27018", $"carepro_brief_{Guid.NewGuid():N}").Options);

    private static AssignmentService Svc(CareProDbContext db) =>
        new(db, Mock.Of<IMediator>(), Mock.Of<Application.Interfaces.Email.IEmailService>(),
            Mock.Of<ICaregiverReadinessService>(), Mock.Of<IPackageContractService>(), Mock.Of<IOpsAlertService>(),
            Mock.Of<ILogger<AssignmentService>>());

    private static async Task<(string assignmentId, string caregiverId, Assignment a)> Seed(CareProDbContext db, string status, bool withBrief)
    {
        var client = new Client { Id = ObjectId.GenerateNewId(), FirstName = "Adaeze", LastName = "Okonkwo", Email = "c@x.com", Password = "x", Role = "Client", Status = true, PhoneNo = "08012345678" }; // pragma: allowlist-secret
        db.Clients.Add(client);
        var pr = new PackageRequest
        {
            Id = ObjectId.GenerateNewId(), ClientId = client.Id.ToString(), PackageId = ObjectId.GenerateNewId().ToString(),
            PackageCategory = "Post Surgery Care", PackageTierLabel = "Essential", RequiredCaregiverType = CaregiverType.RegisteredNurse,
            ServiceCategory = "Post Surgery Care", Status = PackageRequestStatuses.Assigned,
            Location = "12 Admiralty Way, Lekki Phase 1, Lagos", Notes = "call 08012345678, gate code 4471",
            CreatedAt = DateTime.UtcNow
        };
        db.PackageRequests.Add(pr);
        var cg = ObjectId.GenerateNewId().ToString();
        var a = new Assignment
        {
            Id = ObjectId.GenerateNewId(), PackageRequestId = pr.Id.ToString(), CaregiverId = cg, ClientId = client.Id.ToString(),
            Status = status, AssignedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow,
            RespondedAt = status == AssignmentStatuses.Accepted ? DateTime.UtcNow : null,
        };
        if (withBrief) { a.BriefCareNeeds = "Wound care"; a.BriefSchedule = "Mon-Fri"; a.BriefArea = "Lekki, Lagos"; a.BriefDuration = "4 weeks"; a.BriefStartDate = DateTime.UtcNow.AddDays(7); }
        db.Assignments.Add(a);
        await db.SaveChangesAsync();
        return (a.Id.ToString(), cg, a);
    }

    private static readonly string[] Forbidden = { "Okonkwo", "08012345678", "Admiralty", "4471", "Notes", "Location", "gate code" };

    [Fact]
    public async Task Pending_ReturnsFirstNameAndBrief_AndNothingElseAboutTheClient()
    {
        using var db = CreateDb();
        var (id, cg, a) = await Seed(db, AssignmentStatuses.PendingAcceptance, withBrief: true);
        var dto = await Svc(db).GetMyAssignmentDetailAsync(id, cg);
        var json = JsonSerializer.Serialize(dto);
        Assert.Equal("Adaeze", dto.ClientName);
        Assert.Equal(string.Empty, dto.ClientId);
        Assert.False(dto.ClientDetailsReleased);
        Assert.Equal("Lekki, Lagos", dto.CareBrief!.Area);
        foreach (var f in Forbidden) Assert.DoesNotContain(f, json, StringComparison.OrdinalIgnoreCase);
        var list = JsonSerializer.Serialize(await Svc(db).GetMyAssignmentsAsync(cg));
        foreach (var f in Forbidden) Assert.DoesNotContain(f, list, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Declined_StillWithholdsClientDetails()
    {
        using var db = CreateDb();
        var (id, cg, _) = await Seed(db, AssignmentStatuses.Declined, withBrief: true);
        var dto = await Svc(db).GetMyAssignmentDetailAsync(id, cg);
        Assert.Equal("Adaeze", dto.ClientName);
        Assert.False(dto.ClientDetailsReleased);
    }

    [Fact]
    public async Task Accepted_ReleasesFullName_ButNeverPhoneAddressOrNotes()
    {
        using var db = CreateDb();
        var (id, cg, _) = await Seed(db, AssignmentStatuses.Accepted, withBrief: true);
        var dto = await Svc(db).GetMyAssignmentDetailAsync(id, cg);
        var json = JsonSerializer.Serialize(dto);
        Assert.Equal("Adaeze Okonkwo", dto.ClientName);
        Assert.True(dto.ClientDetailsReleased);
        foreach (var f in new[] { "08012345678", "Admiralty", "4471", "Notes" }) Assert.DoesNotContain(f, json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LegacyAssignmentWithoutBrief_LoadsWithNullBrief()
    {
        using var db = CreateDb();
        var (id, cg, _) = await Seed(db, AssignmentStatuses.PendingAcceptance, withBrief: false);
        var dto = await Svc(db).GetMyAssignmentDetailAsync(id, cg);
        Assert.Null(dto.CareBrief);
    }

    [Fact]
    public async Task AnotherCaregiver_CannotReadAPendingAssignment()
    {
        using var db = CreateDb();
        var (id, _, _) = await Seed(db, AssignmentStatuses.PendingAcceptance, withBrief: true);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Svc(db).GetMyAssignmentDetailAsync(id, ObjectId.GenerateNewId().ToString()));
    }
}
