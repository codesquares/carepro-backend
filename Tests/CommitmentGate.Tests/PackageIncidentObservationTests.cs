using System.Linq;
using System.Threading;
using Application.Commands;
using Application.DTOs;
using CloudinaryDotNet;
using Domain.Entities;
using Infrastructure.Content.Data;
using Infrastructure.Content.Services;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MongoDB.Bson;
using Xunit;

namespace CommitmentGate.Tests;

/// <summary>
/// Incident + observation reports on the package-assignment path, side by side with the legacy order path.
/// Same data captured, same notification recipients/types, same visibility rules; the legacy path is
/// exercised in the same fixture to prove it is unchanged. Real local MongoDB (127.0.0.1:27018).
/// </summary>
public class PackageIncidentObservationTests
{
    private sealed class Rig
    {
        public CareProDbContext Db = null!;
        public List<SendNotificationCommand> Sent = new();
        public IncidentReportService Incidents = null!;
        public ObservationReportService Observations = null!;
        public string CaregiverId = ObjectId.GenerateNewId().ToString();
        public string ClientId = ObjectId.GenerateNewId().ToString();
        public string AssignmentId = "", PackageRequestId = "", PkgSheetId = "";
        public string OrderId = "", OrderSheetId = "";
        public string ApprovedAdminId = "";
    }

    private static Rig Build()
    {
        var rig = new Rig { Db = PackageAssignmentCheckinAndTaskSheetTests.CreateDb(PackageAssignmentCheckinAndTaskSheetTests.NewDbName()) };

        // package side
        (rig.AssignmentId, rig.PackageRequestId) = PackageAssignmentCheckinAndTaskSheetTests
            .SeedAcceptedAssignmentAndContract(rig.Db, rig.CaregiverId, rig.ClientId);
        rig.Db.PackageRequests.Add(new PackageRequest
        {
            Id = ObjectId.Parse(rig.PackageRequestId), ClientId = rig.ClientId, Status = "confirmed", CreatedAt = DateTime.UtcNow
        });
        var pkgSheet = new TaskSheet { Id = ObjectId.GenerateNewId(), AssignmentId = rig.AssignmentId, PackageRequestId = rig.PackageRequestId, CaregiverId = rig.CaregiverId, SheetNumber = 1 };
        rig.Db.TaskSheets.Add(pkgSheet);
        rig.PkgSheetId = pkgSheet.Id.ToString();

        // legacy side
        var order = new ClientOrder { Id = ObjectId.GenerateNewId(), ClientId = rig.ClientId, CaregiverId = rig.CaregiverId };
        rig.Db.ClientOrders.Add(order);
        rig.OrderId = order.Id.ToString();
        var orderSheet = new TaskSheet { Id = ObjectId.GenerateNewId(), OrderId = rig.OrderId, CaregiverId = rig.CaregiverId, SheetNumber = 1 };
        rig.Db.TaskSheets.Add(orderSheet);
        rig.OrderSheetId = orderSheet.Id.ToString();

        // one approved admin (+ a pending one that must NOT be notified)
        var admin = new AdminUser { Id = ObjectId.GenerateNewId(), FirstName = "a", LastName = "b", Email = $"a-{Guid.NewGuid():N}@example.com", Password = "x", Role = "SuperAdmin", Department = AdminDepartments.Finance, Status = AdminUserStatus.Approved, CreatedAt = DateTime.UtcNow }; // pragma: allowlist-secret
        var pending = new AdminUser { Id = ObjectId.GenerateNewId(), FirstName = "p", LastName = "b", Email = $"p-{Guid.NewGuid():N}@example.com", Password = "x", Role = "SuperAdmin", Department = AdminDepartments.Finance, Status = AdminUserStatus.PendingApproval, CreatedAt = DateTime.UtcNow }; // pragma: allowlist-secret
        rig.Db.AdminUsers.AddRange(admin, pending);
        rig.ApprovedAdminId = admin.Id.ToString();
        rig.Db.SaveChanges();

        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<SendNotificationCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<string>, CancellationToken>((c, _) => rig.Sent.Add((SendNotificationCommand)c))
            .ReturnsAsync("n");
        var cloud = new CloudinaryService(new Cloudinary(new Account("default", "default", "default")),
            Options.Create(new CloudinarySettings { CloudName = "default", ApiKey = "default", ApiSecret = "default" }));
        rig.Incidents = new IncidentReportService(rig.Db, cloud, mediator.Object, Mock.Of<ILogger<IncidentReportService>>());
        rig.Observations = new ObservationReportService(rig.Db, cloud, mediator.Object, Mock.Of<ILogger<ObservationReportService>>());
        return rig;
    }

    private static CreateIncidentReportRequest Inc(string? orderId, string? assignmentId, string? sheetId, string severity = "serious") => new()
    {
        OrderId = orderId, AssignmentId = assignmentId, TaskSheetId = sheetId, IncidentType = "fall", DateTime = DateTime.UtcNow,
        Description = "Client slipped in the bathroom", ActionsTaken = "Helped up, checked for injury", Witnesses = "None",
        Severity = severity, ReportedAt = DateTime.UtcNow,
    };

    private static CreateObservationReportRequest Obs(string? orderId, string? assignmentId, string sheetId) => new()
    {
        OrderId = orderId, AssignmentId = assignmentId, TaskSheetId = sheetId, Category = "health_observation",
        Description = "Swollen ankle", Severity = "medium", ReportedAt = DateTime.UtcNow,
    };

    // ───────────── Incident: package vs legacy parity ─────────────

    [Fact]
    public async Task Incident_Serious_PackageAndLegacy_CaptureSameDataAndNotifySameRecipients()
    {
        var r = Build();
        var legacy = await r.Incidents.CreateAsync(Inc(r.OrderId, null, r.OrderSheetId), r.CaregiverId);
        var legacyNotes = r.Sent.ToList(); r.Sent.Clear();
        var pkg = await r.Incidents.CreateAsync(Inc(null, r.AssignmentId, r.PkgSheetId), r.CaregiverId);
        var pkgNotes = r.Sent.ToList();

        // data captured
        Assert.Equal(legacy.IncidentType, pkg.IncidentType);
        Assert.Equal(legacy.Severity, pkg.Severity);
        Assert.Equal(legacy.Description, pkg.Description);
        Assert.Equal(legacy.ActionsTaken, pkg.ActionsTaken);
        Assert.Equal(legacy.Witnesses, pkg.Witnesses);
        Assert.Equal(r.CaregiverId, pkg.CaregiverId);
        Assert.Equal(r.AssignmentId, pkg.AssignmentId);
        Assert.Equal(r.PackageRequestId, pkg.PackageRequestId);
        Assert.Equal(string.Empty, pkg.OrderId);
        Assert.Null(legacy.AssignmentId);

        // notifications: approved admin + owning client, same type; pending admin never
        Assert.All(pkgNotes, n => Assert.Equal(NotificationTypes.IncidentReported, n.Type));
        Assert.Equal(legacyNotes.Select(n => n.RecipientId == r.ClientId ? "client" : "admin").OrderBy(x => x),
                     pkgNotes.Select(n => n.RecipientId == r.ClientId ? "client" : "admin").OrderBy(x => x));
        Assert.Contains(pkgNotes, n => n.RecipientId == r.ApprovedAdminId && n.RelatedEntityId == pkg.Id);
        Assert.Equal(2, pkgNotes.Count);
        var clientNote = pkgNotes.Single(n => n.RecipientId == r.ClientId);
        Assert.Equal(r.PackageRequestId, clientNote.RelatedEntityId);
        Assert.Null(clientNote.OrderId);
        // legacy client notification is exactly as before
        var legacyClient = legacyNotes.Single(n => n.RecipientId == r.ClientId);
        Assert.Equal(legacy.Id, legacyClient.RelatedEntityId);
        Assert.Equal(r.OrderId, legacyClient.OrderId);
    }

    [Fact]
    public async Task Incident_Minor_NotifiesNobody_OnBothPaths()
    {
        var r = Build();
        await r.Incidents.CreateAsync(Inc(r.OrderId, null, r.OrderSheetId, "minor"), r.CaregiverId);
        await r.Incidents.CreateAsync(Inc(null, r.AssignmentId, r.PkgSheetId, "minor"), r.CaregiverId);
        Assert.Empty(r.Sent);
    }

    [Fact]
    public async Task Incident_Package_Rejections()
    {
        var r = Build();
        var other = ObjectId.GenerateNewId().ToString();
        // not the assignment's caregiver
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => r.Incidents.CreateAsync(Inc(null, r.AssignmentId, r.PkgSheetId), other));
        // sheet from a different assignment / an order sheet
        await Assert.ThrowsAsync<ArgumentException>(() => r.Incidents.CreateAsync(Inc(null, r.AssignmentId, r.OrderSheetId), r.CaregiverId));
        // both / neither of OrderId + AssignmentId
        await Assert.ThrowsAsync<ArgumentException>(() => r.Incidents.CreateAsync(Inc(r.OrderId, r.AssignmentId, r.PkgSheetId), r.CaregiverId));
        await Assert.ThrowsAsync<ArgumentException>(() => r.Incidents.CreateAsync(Inc(null, null, null), r.CaregiverId));
        // unknown assignment
        await Assert.ThrowsAsync<KeyNotFoundException>(() => r.Incidents.CreateAsync(Inc(null, ObjectId.GenerateNewId().ToString(), null), r.CaregiverId));
        // not yet accepted
        var pending = new Assignment { Id = ObjectId.GenerateNewId(), PackageRequestId = r.PackageRequestId, CaregiverId = r.CaregiverId, ClientId = r.ClientId, Status = AssignmentStatuses.PendingAcceptance, AssignedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow };
        r.Db.Assignments.Add(pending); r.Db.SaveChanges();
        await Assert.ThrowsAsync<ArgumentException>(() => r.Incidents.CreateAsync(Inc(null, pending.Id.ToString(), null), r.CaregiverId));
        Assert.Empty(r.Db.IncidentReports.ToList());
    }

    [Fact]
    public async Task Incident_Package_Visibility_OwnerCaregiverAdminOnly()
    {
        var r = Build();
        var rep = await r.Incidents.CreateAsync(Inc(null, r.AssignmentId, r.PkgSheetId), r.CaregiverId);
        var foreignClient = ObjectId.GenerateNewId().ToString();
        var foreignCaregiver = ObjectId.GenerateNewId().ToString();

        // list by assignment
        Assert.Single(await r.Incidents.GetByAssignmentAsync(r.AssignmentId, r.CaregiverId, false));
        Assert.Single(await r.Incidents.GetByAssignmentAsync(r.AssignmentId, r.ClientId, false, true));
        Assert.Single(await r.Incidents.GetByAssignmentAsync(r.AssignmentId, "", true));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => r.Incidents.GetByAssignmentAsync(r.AssignmentId, foreignClient, false, true));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => r.Incidents.GetByAssignmentAsync(r.AssignmentId, foreignCaregiver, false));
        // a client id used as non-client must not pass either
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => r.Incidents.GetByAssignmentAsync(r.AssignmentId, r.ClientId, false, false));

        // by package request (client only)
        Assert.Single(await r.Incidents.GetByPackageRequestAsClientAsync(r.PackageRequestId, r.ClientId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => r.Incidents.GetByPackageRequestAsClientAsync(r.PackageRequestId, foreignClient));

        // by id
        Assert.Equal(rep.Id, (await r.Incidents.GetByIdAsync(rep.Id, r.ClientId, false, true)).Id);
        Assert.Equal(rep.Id, (await r.Incidents.GetByIdAsync(rep.Id, r.CaregiverId, false, false)).Id);
        Assert.Equal(rep.Id, (await r.Incidents.GetByIdAsync(rep.Id, "", true, false)).Id);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => r.Incidents.GetByIdAsync(rep.Id, foreignClient, false, true));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => r.Incidents.GetByIdAsync(rep.Id, foreignCaregiver, false, false));
    }

    [Fact]
    public async Task Incident_Legacy_VisibilityUnchanged_AndNotMixedIntoPackageLists()
    {
        var r = Build();
        var legacy = await r.Incidents.CreateAsync(Inc(r.OrderId, null, r.OrderSheetId), r.CaregiverId);
        await r.Incidents.CreateAsync(Inc(null, r.AssignmentId, r.PkgSheetId), r.CaregiverId);

        Assert.Equal(legacy.Id, Assert.Single(await r.Incidents.GetByOrderAsync(r.OrderId, r.ClientId, false, true)).Id);
        Assert.Single(await r.Incidents.GetByAssignmentAsync(r.AssignmentId, r.CaregiverId, false));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => r.Incidents.GetByOrderAsync(r.OrderId, ObjectId.GenerateNewId().ToString(), false, true));
        Assert.Equal(legacy.Id, (await r.Incidents.GetByIdAsync(legacy.Id, r.ClientId, false, true)).Id);
        Assert.Equal(1, await r.Incidents.GetCountByTaskSheetIdAsync(r.PkgSheetId));
        Assert.Equal(1, await r.Incidents.GetCountByTaskSheetIdAsync(r.OrderSheetId));
    }

    // ───────────── Observation: package vs legacy parity ─────────────

    [Fact]
    public async Task Observation_PackageAndLegacy_CaptureSameDataAndNotifyClientOnly()
    {
        var r = Build();
        var legacy = await r.Observations.CreateAsync(Obs(r.OrderId, null, r.OrderSheetId), r.CaregiverId);
        var legacyNotes = r.Sent.ToList(); r.Sent.Clear();
        var pkg = await r.Observations.CreateAsync(Obs(null, r.AssignmentId, r.PkgSheetId), r.CaregiverId);
        var pkgNotes = r.Sent.ToList();

        Assert.Equal(legacy.Category, pkg.Category);
        Assert.Equal(legacy.Severity, pkg.Severity);
        Assert.Equal(legacy.Description, pkg.Description);
        Assert.Equal(r.AssignmentId, pkg.AssignmentId);
        Assert.Equal(r.PackageRequestId, pkg.PackageRequestId);
        Assert.Equal(string.Empty, pkg.OrderId);

        var l = Assert.Single(legacyNotes); var p = Assert.Single(pkgNotes);
        Assert.Equal(NotificationTypes.ObservationReportFiled, p.Type);
        Assert.Equal(l.Type, p.Type); Assert.Equal(l.Content, p.Content); Assert.Equal(l.Title, p.Title);
        Assert.Equal(r.ClientId, p.RecipientId);
        Assert.Equal(r.PackageRequestId, p.RelatedEntityId);
        Assert.Null(p.OrderId);
        Assert.Equal(r.OrderId, l.OrderId); // legacy unchanged
        Assert.Equal(legacy.Id, l.RelatedEntityId);
    }

    [Fact]
    public async Task Observation_Package_Rejections_And_Visibility()
    {
        var r = Build();
        var other = ObjectId.GenerateNewId().ToString();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => r.Observations.CreateAsync(Obs(null, r.AssignmentId, r.PkgSheetId), other));
        await Assert.ThrowsAsync<ArgumentException>(() => r.Observations.CreateAsync(Obs(null, r.AssignmentId, r.OrderSheetId), r.CaregiverId));
        await Assert.ThrowsAsync<ArgumentException>(() => r.Observations.CreateAsync(Obs(r.OrderId, r.AssignmentId, r.PkgSheetId), r.CaregiverId));
        await Assert.ThrowsAsync<ArgumentException>(() => r.Observations.CreateAsync(Obs(null, null, r.PkgSheetId), r.CaregiverId));

        await r.Observations.CreateAsync(Obs(null, r.AssignmentId, r.PkgSheetId), r.CaregiverId);
        var foreignClient = ObjectId.GenerateNewId().ToString();
        Assert.Single(await r.Observations.GetByAssignmentAsync(r.AssignmentId, r.PkgSheetId, r.CaregiverId, false));
        Assert.Single(await r.Observations.GetByAssignmentAsync(r.AssignmentId, null, r.ClientId, false, true));
        Assert.Single(await r.Observations.GetByAssignmentAsync(r.AssignmentId, null, "", true));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => r.Observations.GetByAssignmentAsync(r.AssignmentId, null, foreignClient, false, true));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => r.Observations.GetByAssignmentAsync(r.AssignmentId, null, other, false));
        Assert.Single(await r.Observations.GetByPackageRequestAsClientAsync(r.PackageRequestId, null, r.ClientId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => r.Observations.GetByPackageRequestAsClientAsync(r.PackageRequestId, null, foreignClient));
        Assert.Equal(1, await r.Observations.GetCountByTaskSheetIdAsync(r.PkgSheetId));
        // legacy list unaffected by package reports
        Assert.Empty(await r.Observations.GetByOrderAsync(r.OrderId, null, r.ClientId, false, true));
    }
}
