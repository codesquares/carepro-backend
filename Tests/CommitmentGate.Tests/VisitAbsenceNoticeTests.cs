using System.Globalization;
using System.Threading;
using Application.Commands;
using Application.DTOs;
using Application.Interfaces.Email;
using Domain.Entities;
using Infrastructure.Content.Data;
using Infrastructure.Content.Services;
using MediatR;
using Microsoft.Extensions.Logging;
using Moq;
using MongoDB.Bson;
using Xunit;

namespace CommitmentGate.Tests;

/// <summary>Package caregiver "can't attend" notice: ownership, validation, notifications, and that no money/sheet/assignment state moves.</summary>
public class VisitAbsenceNoticeTests
{
    private sealed class Rig
    {
        public CareProDbContext Db = null!;
        public VisitAbsenceService Svc = null!;
        public List<SendNotificationCommand> Sent = new();
        public Mock<IEmailService> Email = new();
        public string CaregiverId = ObjectId.GenerateNewId().ToString(), ClientId = ObjectId.GenerateNewId().ToString();
        public string AssignmentId = "", PackageRequestId = "", ApprovedAdminId = "";
    }

    private static string LagosDate(int plusDays) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Africa/Lagos")).Date
            .AddDays(plusDays).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static Rig Build()
    {
        var r = new Rig { Db = PackageAssignmentCheckinAndTaskSheetTests.CreateDb(PackageAssignmentCheckinAndTaskSheetTests.NewDbName()) };
        (r.AssignmentId, r.PackageRequestId) = PackageAssignmentCheckinAndTaskSheetTests.SeedAcceptedAssignmentAndContract(r.Db, r.CaregiverId, r.ClientId);
        r.Db.PackageRequests.Add(new PackageRequest { Id = ObjectId.Parse(r.PackageRequestId), ClientId = r.ClientId, Status = "confirmed", CreatedAt = DateTime.UtcNow });
        r.Db.Clients.Add(new Client { Id = ObjectId.Parse(r.ClientId), FirstName = "Cli", LastName = "Ent", Email = "client@example.com", Role = "Client", Status = true });
        var admin = new AdminUser { Id = ObjectId.GenerateNewId(), FirstName = "a", LastName = "b", Email = $"a-{Guid.NewGuid():N}@example.com", Password = "x", Role = "SuperAdmin", Department = AdminDepartments.Finance, Status = AdminUserStatus.Approved, CreatedAt = DateTime.UtcNow }; // pragma: allowlist-secret
        var pending = new AdminUser { Id = ObjectId.GenerateNewId(), FirstName = "p", LastName = "b", Email = $"p-{Guid.NewGuid():N}@example.com", Password = "x", Role = "SuperAdmin", Department = AdminDepartments.Finance, Status = AdminUserStatus.PendingApproval, CreatedAt = DateTime.UtcNow }; // pragma: allowlist-secret
        r.Db.AdminUsers.AddRange(admin, pending);
        r.ApprovedAdminId = admin.Id.ToString();
        r.Db.SaveChanges();
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<SendNotificationCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<string>, CancellationToken>((c, _) => r.Sent.Add((SendNotificationCommand)c)).ReturnsAsync("n");
        r.Svc = new VisitAbsenceService(r.Db, mediator.Object, r.Email.Object, Mock.Of<ILogger<VisitAbsenceService>>());
        return r;
    }

    private static CreateVisitAbsenceRequest Req(string date, string reason = "Family emergency") => new() { Date = date, Reason = reason };

    [Fact]
    public async Task FutureDay_NotifiesClientAndApprovedAdminsOnly_AndMovesNothing()
    {
        var r = Build();
        var dto = await r.Svc.CreateAsync(r.AssignmentId, Req(LagosDate(5)), r.CaregiverId);

        Assert.False(dto.ShortNotice);
        Assert.Equal(r.PackageRequestId, dto.PackageRequestId);
        Assert.Equal(2, r.Sent.Count);
        Assert.All(r.Sent, n => { Assert.Equal(NotificationTypes.VisitAbsenceNotified, n.Type); Assert.Equal(r.PackageRequestId, n.RelatedEntityId); Assert.Null(n.OrderId); });
        Assert.Contains(r.Sent, n => n.RecipientId == r.ClientId);
        Assert.Contains(r.Sent, n => n.RecipientId == r.ApprovedAdminId);
        r.Email.Verify(e => e.SendGenericNotificationEmailAsync("client@example.com", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), false, null, null), Times.Once);
        // nothing else moved
        Assert.Empty(r.Db.TaskSheets.ToList());
        Assert.Equal(AssignmentStatuses.Accepted, r.Db.Assignments.Single().Status);
    }

    [Fact]
    public async Task SameDayOrNextMorning_IsShortNotice_AndSaysSo()
    {
        var r = Build();
        var today = await r.Svc.CreateAsync(r.AssignmentId, Req(LagosDate(0)), r.CaregiverId);
        Assert.True(today.ShortNotice);
        Assert.All(r.Sent.Where(n => n.RecipientId == r.ClientId), n => Assert.Contains("short notice", n.Content));
        Assert.Contains(r.Sent, n => n.RecipientId == r.ApprovedAdminId && n.Content.Contains("review needed"));
    }

    [Fact]
    public async Task Rejections()
    {
        var r = Build();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => r.Svc.CreateAsync(r.AssignmentId, Req(LagosDate(5)), ObjectId.GenerateNewId().ToString()));
        await Assert.ThrowsAsync<ArgumentException>(() => r.Svc.CreateAsync(r.AssignmentId, Req(LagosDate(-1)), r.CaregiverId));
        await Assert.ThrowsAsync<ArgumentException>(() => r.Svc.CreateAsync(r.AssignmentId, Req(LagosDate(90)), r.CaregiverId));
        await Assert.ThrowsAsync<ArgumentException>(() => r.Svc.CreateAsync(r.AssignmentId, Req("12/10/2026"), r.CaregiverId));
        await Assert.ThrowsAsync<ArgumentException>(() => r.Svc.CreateAsync(r.AssignmentId, Req(LagosDate(5), "  "), r.CaregiverId));
        await Assert.ThrowsAsync<ArgumentException>(() => r.Svc.CreateAsync(r.AssignmentId, Req(LagosDate(5), new string('x', 501)), r.CaregiverId));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => r.Svc.CreateAsync(ObjectId.GenerateNewId().ToString(), Req(LagosDate(5)), r.CaregiverId));
        // duplicate day
        await r.Svc.CreateAsync(r.AssignmentId, Req(LagosDate(5)), r.CaregiverId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => r.Svc.CreateAsync(r.AssignmentId, Req(LagosDate(5)), r.CaregiverId));
        // not accepted
        var a = r.Db.Assignments.Single(); a.Status = AssignmentStatuses.Cancelled; r.Db.SaveChanges();
        await Assert.ThrowsAsync<InvalidOperationException>(() => r.Svc.CreateAsync(r.AssignmentId, Req(LagosDate(6)), r.CaregiverId));
        Assert.Single(r.Db.VisitAbsenceNotices.ToList());
    }

    [Fact]
    public async Task AlreadySubmittedVisitForThatDay_Rejected()
    {
        var r = Build();
        var day = DateTime.ParseExact(LagosDate(0), "yyyy-MM-dd", CultureInfo.InvariantCulture);
        r.Db.TaskSheets.Add(new TaskSheet { AssignmentId = r.AssignmentId, PackageRequestId = r.PackageRequestId, CaregiverId = r.CaregiverId, ScheduledDate = day, Status = "submitted" });
        r.Db.SaveChanges();
        await Assert.ThrowsAsync<InvalidOperationException>(() => r.Svc.CreateAsync(r.AssignmentId, Req(LagosDate(0)), r.CaregiverId));
    }

    [Fact]
    public async Task Visibility_CaregiverOwnerAndOwningClientOnly()
    {
        var r = Build();
        await r.Svc.CreateAsync(r.AssignmentId, Req(LagosDate(5)), r.CaregiverId);
        var stranger = ObjectId.GenerateNewId().ToString();
        Assert.Single(await r.Svc.GetForAssignmentAsync(r.AssignmentId, r.CaregiverId));
        Assert.Single(await r.Svc.GetForPackageRequestAsClientAsync(r.PackageRequestId, r.ClientId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => r.Svc.GetForAssignmentAsync(r.AssignmentId, stranger));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => r.Svc.GetForPackageRequestAsClientAsync(r.PackageRequestId, stranger));
    }
}
