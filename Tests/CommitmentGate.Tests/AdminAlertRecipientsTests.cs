using System.Reflection;
using Application.Commands;
using Application.DTOs;
using Application.Interfaces.Content;
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

/// <summary>
/// A2.4: every admin-bound notification resolves recipients through AdminRecipients, which applies the same rule
/// as the login gate (Approved; null/blank counts as Approved). Real local Mongo (replica set on 127.0.0.1:27018),
/// mocked mediator captures who would have been notified.
/// </summary>
public class AdminAlertRecipientsTests
{
    private sealed class Rig
    {
        public CareProDbContext Db = null!;
        public Mock<IMediator> Mediator = new();
        public List<SendNotificationCommand> Sent = new();
        public AdminUser Approved = null!, Pending = null!, NullStatus = null!, Blank = null!, Rejected = null!, Deleted = null!;
        public IEnumerable<string> Recipients(string type) => Sent.Where(s => s.Type == type).Select(s => s.RecipientId).OrderBy(x => x).ToList();
        public string[] Expected => new[] { Approved, NullStatus, Blank }.Select(a => a.Id.ToString()).OrderBy(x => x).ToArray();
    }

    private static AdminUser Admin(string tag, string? status, bool deleted = false) => new()
    {
        Id = ObjectId.GenerateNewId(), FirstName = tag, LastName = "Admin", Email = $"{tag}-{Guid.NewGuid():N}@example.com",
        Password = "x", Role = "SuperAdmin", Department = AdminDepartments.Finance, Status = status, IsDeleted = deleted, CreatedAt = DateTime.UtcNow, // pragma: allowlist-secret
    };

    private static Rig Build()
    {
        var rig = new Rig { Db = PoliceClearanceTestSupport.CreateDb(PoliceClearanceTestSupport.NewDbName()) };
        rig.Approved = Admin("approved", AdminUserStatus.Approved);
        rig.Pending = Admin("pending", AdminUserStatus.PendingApproval);
        rig.NullStatus = Admin("nullstatus", null);
        rig.Blank = Admin("blank", "  ");
        rig.Rejected = Admin("rejected", AdminUserStatus.Rejected);
        rig.Deleted = Admin("deleted", AdminUserStatus.Approved, deleted: true);
        rig.Db.AdminUsers.AddRange(rig.Approved, rig.Pending, rig.NullStatus, rig.Blank, rig.Rejected, rig.Deleted);
        rig.Db.SaveChanges();
        rig.Mediator.Setup(m => m.Send(It.IsAny<SendNotificationCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<string>, CancellationToken>((c, _) => rig.Sent.Add((SendNotificationCommand)c)).ReturnsAsync("n1");
        return rig;
    }

    private static async Task Invoke(object svc, string method, params object[] args)
    {
        var m = svc.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)m.Invoke(svc, args)!;
    }

    [Fact]
    public async Task Helper_ReturnsOnlyApprovedNonDeleted_NullAndBlankCountAsApproved()
    {
        var rig = Build();
        var got = (await AdminRecipients.GetApprovedAsync(rig.Db)).Select(a => a.Id.ToString()).OrderBy(x => x).ToArray();
        Assert.Equal(rig.Expected, got);
    }

    [Fact]
    public async Task Helper_ScopeStillApplies_ThenApprovalFilter()
    {
        var rig = Build();
        var got = await AdminRecipients.GetApprovedAsync(rig.Db, a => a.Id == rig.Pending.Id || a.Id == rig.Approved.Id);
        Assert.Equal(rig.Approved.Id, Assert.Single(got).Id);
    }

    [Fact]
    public async Task OpsAlert_PackageTypes_OnlyApprovedAdminsReceive()
    {
        var rig = Build();
        var ops = new OpsAlertService(rig.Db, rig.Mediator.Object, Mock.Of<IEmailService>(), Mock.Of<ILogger<OpsAlertService>>());
        foreach (var t in new[] { NotificationTypes.PackageRequestReceived, NotificationTypes.PackageAssignmentDeclined,
                                  NotificationTypes.PackageContractDeliveryFailed, NotificationTypes.PackagePaymentRequestFailed })
        {
            await ops.NotifyAdminsAsync(t, "t", "c", "rel");
            Assert.Equal(rig.Expected, rig.Recipients(t));
        }
        Assert.DoesNotContain(rig.Sent, s => s.RecipientId == rig.Pending.Id.ToString() || s.RecipientId == rig.Rejected.Id.ToString() || s.RecipientId == rig.Deleted.Id.ToString());
    }

    [Fact]
    public async Task Incident_OnlyApprovedAdminsReceive()
    {
        var rig = Build();
        var svc = new IncidentReportService(rig.Db, null!, rig.Mediator.Object, Mock.Of<ILogger<IncidentReportService>>());
        var report = new IncidentReport { Id = ObjectId.GenerateNewId(), OrderId = ObjectId.GenerateNewId().ToString(), Severity = "minor", IncidentType = "other" };
        await Invoke(svc, "NotifyAdminsAsync", report, ObjectId.GenerateNewId().ToString());
        Assert.Equal(rig.Expected, rig.Recipients(NotificationTypes.IncidentReported));
    }

    [Fact]
    public async Task Dispute_OnlyApprovedAdminsReceive()
    {
        var rig = Build();
        var svc = new DisputeService(rig.Db, rig.Mediator.Object, Mock.Of<IEarningsLedgerService>(), Mock.Of<ICaregiverWalletService>(),
            Mock.Of<IClientWalletService>(), Mock.Of<ILogger<DisputeService>>(), Mock.Of<IEmailService>());
        var d = new Dispute
        {
            Id = ObjectId.GenerateNewId(), ClientId = ObjectId.GenerateNewId().ToString(), CaregiverId = ObjectId.GenerateNewId().ToString(),
            RaisedBy = ObjectId.GenerateNewId().ToString(), Category = "quality_of_care", Reason = "r", DisputeType = DisputeType.Visit,
        };
        await Invoke(svc, "NotifyDisputeRaisedAsync", d);
        // dispute notifications also go to the caregiver and client (not admins): compare admins only
        var adminIds = rig.Db.AdminUsers.ToList().Select(a => a.Id.ToString()).ToHashSet();
        var toAdmins = rig.Sent.Where(s => s.Type == NotificationTypes.DisputeRaised && adminIds.Contains(s.RecipientId)).Select(s => s.RecipientId).OrderBy(x => x);
        Assert.Equal(rig.Expected, toAdmins);
    }

    [Fact]
    public async Task Withdrawal_OnlyApprovedAdminsReceive()
    {
        var rig = Build();
        var cg = new Mock<ICareGiverService>();
        var svc = new WithdrawalRequestService(rig.Db, Mock.Of<IEarningsService>(), cg.Object, Mock.Of<IAdminUserService>(), rig.Mediator.Object,
            Mock.Of<ICaregiverWalletService>(), Mock.Of<IEarningsLedgerService>(), Mock.Of<IEmailService>());
        var w = new WithdrawalRequest { Id = ObjectId.GenerateNewId(), CaregiverId = ObjectId.GenerateNewId().ToString(), AmountRequested = 1000, FinalAmount = 900, BankName = "B", AccountName = "A", AccountNumber = "1" };
        await Invoke(svc, "NotifyAdminsAboutWithdrawalRequest", w);
        Assert.Equal(rig.Expected, rig.Recipients(NotificationTypes.WithdrawalRequest));
    }

    [Fact]
    public async Task Inbox_ExcludesLegacyAdminNoMatch_FromListAndUnreadCount_WithoutDeletingRows()
    {
        var rig = Build();
        var adminId = rig.Approved.Id.ToString();
        for (var i = 0; i < 3; i++)
            rig.Db.Notifications.Add(new Notification { Id = ObjectId.GenerateNewId(), RecipientId = adminId, Type = NotificationTypes.CareRequestAdminNoMatch, Content = "legacy", CreatedAt = DateTime.UtcNow });
        rig.Db.Notifications.Add(new Notification { Id = ObjectId.GenerateNewId(), RecipientId = adminId, Type = NotificationTypes.PackageRequestReceived, Content = "real", CreatedAt = DateTime.UtcNow });
        rig.Db.SaveChanges();

        var svc = new NotificationService(rig.Db, Mock.Of<Microsoft.AspNetCore.SignalR.IHubContext<Infrastructure.Content.Services.NotificationHub>>(),
            Mock.Of<IPushService>(), Mock.Of<ILogger<NotificationService>>());
        var page = await svc.GetUserNotificationsAsync(adminId);
        Assert.Equal(NotificationTypes.PackageRequestReceived, Assert.Single(page.Items).Type);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(1, await svc.GetUnreadNotificationCountAsync(adminId));
        Assert.Equal(4, rig.Db.Notifications.Count(n => n.RecipientId == adminId)); // legacy rows still exist
    }
}
