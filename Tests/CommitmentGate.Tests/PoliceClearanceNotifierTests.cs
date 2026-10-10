using Application.Commands;
using Application.DTOs;
using Application.Interfaces.Content;
using Application.Interfaces.Email;
using Domain.Entities;
using Infrastructure.Content.Services;
using MediatR;
using Microsoft.Extensions.Logging;
using Moq;
using MongoDB.Bson;
using Xunit;

namespace CommitmentGate.Tests;

/// <summary>Item 7: notifications — in-app + email with reasons, real line breaks, admin alerts in-app only.</summary>
public class PoliceClearanceNotifierTests
{
    private sealed class Rig
    {
        public Mock<IMediator> Mediator = new();
        public Mock<IEmailService> Email = new();
        public Mock<IOpsAlertService> Ops = new();
        public PoliceClearanceNotifier Notifier = null!;
        public Caregiver Caregiver = null!;
        public List<SendNotificationCommand> Sent = new();
    }

    private static Rig Build()
    {
        var rig = new Rig();
        var db = PoliceClearanceTestSupport.CreateDb(PoliceClearanceTestSupport.NewDbName());
        rig.Caregiver = PoliceClearanceTestSupport.SeedCaregiver(db, "Ada", "CLEARANCETEST");
        rig.Mediator.Setup(m => m.Send(It.IsAny<SendNotificationCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<string>, CancellationToken>((c, _) => rig.Sent.Add((SendNotificationCommand)c)).ReturnsAsync("n1");
        rig.Notifier = new PoliceClearanceNotifier(db, rig.Mediator.Object, rig.Email.Object, rig.Ops.Object, Mock.Of<ILogger<PoliceClearanceNotifier>>());
        return rig;
    }

    [Fact]
    public async Task Rejected_ReasonInBothInAppAndEmail_WithRealLineBreaks()
    {
        var rig = Build();
        await rig.Notifier.NotifyRejectedAsync(rig.Caregiver.Id.ToString(), "clr1", "Name does not match.\nPlease <b>retake</b>.");

        var n = Assert.Single(rig.Sent);
        Assert.Equal(NotificationTypes.PoliceClearanceRejected, n.Type);
        Assert.Equal(rig.Caregiver.Id.ToString(), n.RecipientId);
        Assert.Equal("clr1", n.RelatedEntityId);
        Assert.Contains("Name does not match.", n.Content);

        string? content = null;
        rig.Email.Verify(e => e.SendGenericNotificationEmailAsync(rig.Caregiver.Email, "Ada", It.IsAny<string>(),
            It.Is<string>(c => Capture(c, out content)), false, null, null), Times.Once);
        Assert.Contains("Name does not match.<br />Please &lt;b&gt;retake&lt;/b&gt;.", content);   // real <br />, staff text encoded
        Assert.DoesNotContain("\\n", content);
        Assert.DoesNotContain("\n", content);
    }

    private static bool Capture(string c, out string? slot) { slot = c; return true; }

    [Fact]
    public async Task Revoked_ReasonInBoth()
    {
        var rig = Build();
        await rig.Notifier.NotifyRevokedAsync(rig.Caregiver.Id.ToString(), "c", "Found to be altered");
        Assert.Contains("Found to be altered", rig.Sent.Single().Content);
        Assert.Equal(NotificationTypes.PoliceClearanceRevoked, rig.Sent.Single().Type);
        rig.Email.Verify(e => e.SendGenericNotificationEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.Is<string>(c => c.Contains("Found to be altered")), false, null, null), Times.Once);
    }

    [Fact]
    public async Task Approved_InAppAndEmail_NoGateNoUnsubscribe()
    {
        var rig = Build();
        await rig.Notifier.NotifyApprovedAsync(rig.Caregiver.Id.ToString(), "c");
        Assert.Equal(NotificationTypes.PoliceClearanceApproved, rig.Sent.Single().Type);
        // includeUnsubscribeHeader=false, gateUserId=null, gateNotificationType=null  => Always-Send
        rig.Email.Verify(e => e.SendGenericNotificationEmailAsync(rig.Caregiver.Email, "Ada", It.IsAny<string>(), It.IsAny<string>(), false, null, null), Times.Once);
    }

    [Fact]
    public async Task Submitted_GoesToAdminsInAppOnly()
    {
        var rig = Build();
        var c = new PoliceClearance { Id = ObjectId.GenerateNewId() };
        await rig.Notifier.NotifySubmittedAsync(c, "Ada CLEARANCETEST");
        rig.Ops.Verify(o => o.NotifyAdminsAsync(NotificationTypes.PoliceClearanceSubmitted, It.IsAny<string>(),
            It.Is<string>(s => s.Contains("Ada CLEARANCETEST")), c.Id.ToString(), false), Times.Once);
        rig.Email.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AssignmentsAtRisk_ListsEachAssignment_InAppOnly()
    {
        var rig = Build();
        await rig.Notifier.NotifyAssignmentsAtRiskAsync("cg", "Ada", "revoked", "clr", new[] { "A1 (Post Surgery Care)", "A2 (Adult Care)" });
        rig.Ops.Verify(o => o.NotifyAdminsAsync(NotificationTypes.PoliceClearanceAssignmentsAtRisk, It.IsAny<string>(),
            It.Is<string>(s => s.Contains("A1 (Post Surgery Care)") && s.Contains("A2 (Adult Care)") && s.Contains("Nothing was cancelled")), "clr", false), Times.Once);
    }

    [Fact]
    public async Task DeliveryFailures_AreSwallowed_NeverThrown()
    {
        var rig = Build();
        rig.Mediator.Setup(m => m.Send(It.IsAny<SendNotificationCommand>(), It.IsAny<CancellationToken>())).ThrowsAsync(new Exception("down"));
        rig.Email.Setup(e => e.SendGenericNotificationEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<string?>())).ThrowsAsync(new Exception("smtp"));
        rig.Ops.Setup(o => o.NotifyAdminsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>())).ThrowsAsync(new Exception("ops"));
        await rig.Notifier.NotifyRejectedAsync(rig.Caregiver.Id.ToString(), "c", "x");
        await rig.Notifier.NotifySubmittedAsync(new PoliceClearance { Id = ObjectId.GenerateNewId() }, "n");
        await rig.Notifier.NotifyAssignmentsAtRiskAsync("cg", "n", "revoked", "c", new[] { "a" });
    }

    [Fact]
    public void Constants_AreStable()
    {
        Assert.Equal("police_clearance_submitted", NotificationTypes.PoliceClearanceSubmitted);
        Assert.Equal("police_clearance_approved", NotificationTypes.PoliceClearanceApproved);
        Assert.Equal("police_clearance_rejected", NotificationTypes.PoliceClearanceRejected);
        Assert.Equal("police_clearance_revoked", NotificationTypes.PoliceClearanceRevoked);
    }

    [Fact]
    public void PolicyDocument_HasRowsForEveryNewType()
    {
        var dir = AppContext.BaseDirectory;
        string? path = null;
        for (var d = new DirectoryInfo(dir); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "EMAIL-COMPLIANCE-CLASSIFICATION-POLICY.md"))) { path = Path.Combine(d.FullName, "EMAIL-COMPLIANCE-CLASSIFICATION-POLICY.md"); break; }
        Assert.NotNull(path);
        var text = File.ReadAllText(path!);
        foreach (var t in new[] { "police_clearance_submitted", "police_clearance_approved", "police_clearance_rejected", "police_clearance_revoked", "police_clearance_assignments_at_risk" })
            Assert.Contains(t, text);
    }
}
