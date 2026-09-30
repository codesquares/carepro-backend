using Application.Commands;
using Application.DTOs;
using Application.Interfaces.Content;
using Application.Interfaces.Email;
using Domain.Entities;
using Infrastructure.Content.Data;
using Infrastructure.Content.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using MongoDB.Bson;
using Xunit;

namespace CommitmentGate.Tests;

/// <summary>
/// Payment → request → assignment email/alert coverage. Real local MongoDB (replica set on
/// 127.0.0.1:27018), real PackageRequestService / OpsAlertService / PackageContractService (real PDF);
/// only the outbound edges (IEmailService, IMediator) are mocked, so we assert exactly what would be sent.
/// </summary>
public class PackageLifecycleEmailTests
{
    private static CareProDbContext CreateDb(string name) =>
        new TestCareProDbContext(new DbContextOptionsBuilder<CareProDbContext>()
            .UseMongoDB("mongodb://127.0.0.1:27018", name).Options);

    private static string NewDbName() => $"carepro_lifecycle_email_{Guid.NewGuid():N}";

    private sealed class Rig
    {
        public required Mock<IEmailService> Email { get; init; }
        public required Mock<IMediator> Mediator { get; init; }
        public required OpsAlertService Ops { get; init; }
    }

    private static Rig NewRig(CareProDbContext db)
    {
        var email = new Mock<IEmailService>();
        var mediator = new Mock<IMediator>();
        return new Rig { Email = email, Mediator = mediator,
            Ops = new OpsAlertService(db, mediator.Object, email.Object, Mock.Of<ILogger<OpsAlertService>>()) };
    }

    private static AdminUser SeedAdmin(CareProDbContext db, string? status = AdminUserStatus.Approved, string email = "ops-admin@example.com")
    {
        var a = new AdminUser
        {
            Id = ObjectId.GenerateNewId(), FirstName = "Ops", LastName = "Admin", Email = email,
            Password = "x", Role = "Admin", Status = status, IsDeleted = false, CreatedAt = DateTime.UtcNow // pragma: allowlist-secret
        };
        db.AdminUsers.Add(a);
        return a;
    }

    private static Client SeedClient(CareProDbContext db)
    {
        var c = new Client
        {
            Id = ObjectId.GenerateNewId(), FirstName = "Amara", LastName = "Okafor",
            Email = $"client-{Guid.NewGuid():N}@example.com", Password = "x", // pragma: allowlist-secret
            Role = "Client", IsDeleted = false, Status = true, CreatedAt = DateTime.UtcNow
        };
        db.Clients.Add(c);
        return c;
    }

    private static Package SeedPackage(CareProDbContext db)
    {
        var p = new Package
        {
            Id = ObjectId.GenerateNewId(), Category = "Post-Partum Care", TierLabel = "Premium",
            RequiredCaregiverType = CaregiverType.RegisteredNurse, RequiredSpecialty = "Midwifery",
            BasePrice = 350000m, AdditionalDayPrice = 20000m, Description = "pkg", IsActive = true,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        db.Packages.Add(p);
        return p;
    }

    private static PendingPackagePayment SeedPending(CareProDbContext db, Client client, Package pkg, string txRef)
    {
        var pp = new PendingPackagePayment
        {
            Id = ObjectId.GenerateNewId(), TransactionReference = txRef, ClientId = client.Id.ToString(),
            PackageId = pkg.Id.ToString(), BasePrice = pkg.BasePrice, TotalAmount = pkg.BasePrice, Currency = "NGN",
            Email = client.Email, RedirectUrl = "https://example.com", PaymentLink = "https://pay.test/x",
            Status = PendingPackagePaymentStatus.Pending, CreatedAt = DateTime.UtcNow
        };
        db.PendingPackagePayments.Add(pp);
        return pp;
    }

    private static PackagePaymentService PaymentSvc(CareProDbContext db, Rig rig, IPackageRequestService requests)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FrontendUrl"] = "https://example.com", ["Flutterwave:SecretKey"] = "k", ["Flutterwave:PublicKey"] = "k",
            ["Flutterwave:EncryptionKey"] = "k", ["Flutterwave:WebhookSecretHash"] = "k"
        }).Build();
        return new PackagePaymentService(db, Mock.Of<IPackageService>(), requests,
            Mock.Of<IPackageSubscriptionService>(), new FlutterwaveService(config, Mock.Of<ILogger<FlutterwaveService>>()),
            config, rig.Email.Object, rig.Ops, Mock.Of<ILogger<PackagePaymentService>>());
    }

    // ── 1. one combined confirmation email, never re-sent on webhook replay ──

    [Fact]
    public async Task PaymentCompletes_ClientGetsOneCombinedEmail_AndReplayDoesNotResend()
    {
        using var db = CreateDb(NewDbName());
        var client = SeedClient(db); var pkg = SeedPackage(db); SeedAdmin(db);
        SeedPending(db, client, pkg, "CAREPRO-PKG-EMAIL-1");
        await db.SaveChangesAsync();
        var rig = NewRig(db);
        var svc = PaymentSvc(db, rig, new PackageRequestService(db, rig.Ops, Mock.Of<ILogger<PackageRequestService>>()));

        var first = await svc.CompletePackagePaymentAsync("CAREPRO-PKG-EMAIL-1", "flw-1", 350000m);
        var replay = await svc.CompletePackagePaymentAsync("CAREPRO-PKG-EMAIL-1", "flw-1", 350000m);

        Assert.True(first.IsSuccess);
        Assert.True(replay.IsSuccess);
        rig.Email.Verify(e => e.SendPackagePaymentConfirmationEmailAsync(
            client.Email, "Amara", 350000m, "Post-Partum Care (Premium)", "CAREPRO-PKG-EMAIL-1"), Times.Once);
        rig.Email.Verify(e => e.SendPackagePaymentIssueEmailAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ConfirmationEmailFailure_DoesNotFailOrUndoTheCompletedPayment()
    {
        using var db = CreateDb(NewDbName());
        var client = SeedClient(db); var pkg = SeedPackage(db);
        SeedPending(db, client, pkg, "CAREPRO-PKG-EMAIL-2");
        await db.SaveChangesAsync();
        var rig = NewRig(db);
        rig.Email.Setup(e => e.SendPackagePaymentConfirmationEmailAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("smtp down"));
        var svc = PaymentSvc(db, rig, new PackageRequestService(db, rig.Ops, Mock.Of<ILogger<PackageRequestService>>()));

        var result = await svc.CompletePackagePaymentAsync("CAREPRO-PKG-EMAIL-2", "flw-2", 350000m);

        Assert.True(result.IsSuccess);
        Assert.Equal(PendingPackagePaymentStatus.Completed, result.Value!.Status);
    }

    // ── 2. charged but request creation fails: client + ops both told ──

    [Fact]
    public async Task RequestCreationFails_ClientGetsIssueEmail_AndOpsAlertedInAppAndByEmail()
    {
        var dbName = NewDbName();
        using var db = CreateDb(dbName);
        var client = SeedClient(db); var pkg = SeedPackage(db); var admin = SeedAdmin(db);
        SeedPending(db, client, pkg, "CAREPRO-PKG-FAIL-1");
        await db.SaveChangesAsync();
        var rig = NewRig(db);
        var requests = new Mock<IPackageRequestService>();
        requests.Setup(r => r.CreateAsync(It.IsAny<string>(), It.IsAny<CreatePackageRequestRequest>()))
            .ThrowsAsync(new InvalidOperationException("db exploded"));
        var svc = PaymentSvc(db, rig, requests.Object);

        var result = await svc.CompletePackagePaymentAsync("CAREPRO-PKG-FAIL-1", "flw-f", 350000m);

        Assert.False(result.IsSuccess);
        rig.Email.Verify(e => e.SendPackagePaymentIssueEmailAsync(client.Email, "Amara", 350000m, "CAREPRO-PKG-FAIL-1"), Times.Once);
        rig.Email.Verify(e => e.SendPackagePaymentConfirmationEmailAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        rig.Mediator.Verify(m => m.Send(It.Is<SendNotificationCommand>(c =>
            c.RecipientId == admin.Id.ToString() && c.Type == NotificationTypes.PackagePaymentRequestFailed
            && c.Content.Contains("CAREPRO-PKG-FAIL-1")), It.IsAny<CancellationToken>()), Times.Once);
        rig.Email.Verify(e => e.SendGenericNotificationEmailAsync(
            admin.Email, It.IsAny<string>(), It.Is<string>(s => s.Contains("Action needed")), It.IsAny<string>(),
            false, null, null), Times.Once);

        using var check = CreateDb(dbName);
        var pending = await check.PendingPackagePayments.FirstAsync(p => p.TransactionReference == "CAREPRO-PKG-FAIL-1");
        Assert.Equal(PendingPackagePaymentStatus.Failed, pending.Status);
    }

    // ── 3. new request → ops alert ──

    [Fact]
    public async Task NewPackageRequest_CreatesOpsAlertForEveryActiveAdmin()
    {
        using var db = CreateDb(NewDbName());
        var client = SeedClient(db); var pkg = SeedPackage(db); var admin = SeedAdmin(db);
        await db.SaveChangesAsync();
        var rig = NewRig(db);
        var svc = new PackageRequestService(db, rig.Ops, Mock.Of<ILogger<PackageRequestService>>());

        var pr = await svc.CreateAsync(client.Id.ToString(), new CreatePackageRequestRequest { PackageId = pkg.Id.ToString() });

        rig.Mediator.Verify(m => m.Send(It.Is<SendNotificationCommand>(c =>
            c.RecipientId == admin.Id.ToString() && c.Type == NotificationTypes.PackageRequestReceived
            && c.RelatedEntityId == pr.Id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OpsAlertFailure_NeverBreaksRequestCreation()
    {
        using var db = CreateDb(NewDbName());
        var client = SeedClient(db); var pkg = SeedPackage(db); SeedAdmin(db);
        await db.SaveChangesAsync();
        var rig = NewRig(db);
        rig.Mediator.Setup(m => m.Send(It.IsAny<SendNotificationCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("notification pipeline down"));
        var svc = new PackageRequestService(db, rig.Ops, Mock.Of<ILogger<PackageRequestService>>());

        var pr = await svc.CreateAsync(client.Id.ToString(), new CreatePackageRequestRequest { PackageId = pkg.Id.ToString() });

        Assert.False(string.IsNullOrEmpty(pr.Id));
    }

    // ── 4. acceptance: caregiver gets BOTH the confirmation email and the contract PDF ──

    private static AssignmentService AssignmentSvc(CareProDbContext db, Rig rig, IPackageContractService contracts)
    {
        var readiness = new Mock<ICaregiverReadinessService>();
        readiness.Setup(r => r.GetReadinessAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool?>()))
            .ReturnsAsync(new CaregiverReadinessResult { IsReady = true });
        return new AssignmentService(db, rig.Mediator.Object, rig.Email.Object, readiness.Object, contracts, rig.Ops,
            Mock.Of<ILogger<AssignmentService>>());
    }

    private static Caregiver SeedCaregiver(CareProDbContext db)
    {
        var cg = new Caregiver
        {
            Id = ObjectId.GenerateNewId(), FirstName = "Ngozi", LastName = "Balogun",
            Email = $"cg-{Guid.NewGuid():N}@example.com", Password = "x", // pragma: allowlist-secret
            Role = "Caregiver", Status = true, IsAvailable = true, IsIdentityVerified = true,
            CaregiverType = CaregiverType.RegisteredNurse, Specialty = "Midwifery", CreatedAt = DateTime.UtcNow
        };
        db.CareGivers.Add(cg);
        return cg;
    }

    [Fact]
    public async Task Acceptance_CaregiverGetsConfirmationEmail_AndContractPdf_ClientGetsBoth()
    {
        var dbName = NewDbName();
        using var db = CreateDb(dbName);
        var client = SeedClient(db); var pkg = SeedPackage(db); var cg = SeedCaregiver(db); SeedAdmin(db);
        await db.SaveChangesAsync();
        var rig = NewRig(db);
        var contracts = new PackageContractService(db, new ContractTemplateService(), new ContractPdfService(),
            rig.Mediator.Object, rig.Email.Object, rig.Ops, Mock.Of<ILogger<PackageContractService>>());
        var svc = AssignmentSvc(db, rig, contracts);
        var requests = new PackageRequestService(db, rig.Ops, Mock.Of<ILogger<PackageRequestService>>());

        var pr = await requests.CreateAsync(client.Id.ToString(), new CreatePackageRequestRequest
        { PackageId = pkg.Id.ToString(), ServiceCategory = pkg.Category, Location = "Ikoyi, Lagos" });
        var assignment = await svc.AssignAsync(pr.Id, cg.Id.ToString(), "admin-1", "ops@x", "staff", null);
        rig.Email.Invocations.Clear();

        await svc.AcceptAsync(assignment.Id, cg.Id.ToString());

        // dedicated confirmation to the caregiver
        rig.Email.Verify(e => e.SendGenericNotificationEmailAsync(
            cg.Email, "Ngozi", It.Is<string>(s => s.Contains("You're confirmed")), It.IsAny<string>(),
            false, null, null), Times.Once);
        // …and the client's confirmation (unchanged)
        rig.Email.Verify(e => e.SendGenericNotificationEmailAsync(
            client.Email, "Amara", It.Is<string>(s => s.Contains("caregiver is confirmed")), It.IsAny<string>(),
            false, null, null), Times.Once);
        // …alongside the contract PDF, to both parties, with real PDF bytes
        rig.Email.Verify(e => e.SendContractPdfEmailAsync(cg.Email, "Ngozi", It.IsAny<string>(), It.IsAny<string>(),
            It.Is<byte[]>(b => b.Length > 1000)), Times.Once);
        rig.Email.Verify(e => e.SendContractPdfEmailAsync(client.Email, "Amara", It.IsAny<string>(), It.IsAny<string>(),
            It.Is<byte[]>(b => b.Length > 1000)), Times.Once);
    }

    [Fact]
    public async Task Acceptance_WhenContractGenerationFails_AcceptanceStands_AndOpsAlerted()
    {
        using var db = CreateDb(NewDbName());
        var client = SeedClient(db); var pkg = SeedPackage(db); var cg = SeedCaregiver(db); var admin = SeedAdmin(db);
        await db.SaveChangesAsync();
        var rig = NewRig(db);
        var contracts = new Mock<IPackageContractService>();
        contracts.Setup(c => c.GenerateForConfirmedRequestAsync(It.IsAny<string>())).ThrowsAsync(new InvalidOperationException("template broke"));
        var svc = AssignmentSvc(db, rig, contracts.Object);
        var requests = new PackageRequestService(db, rig.Ops, Mock.Of<ILogger<PackageRequestService>>());
        var pr = await requests.CreateAsync(client.Id.ToString(), new CreatePackageRequestRequest { PackageId = pkg.Id.ToString() });
        var assignment = await svc.AssignAsync(pr.Id, cg.Id.ToString(), "admin-1", "ops@x", "staff", null);

        var result = await svc.AcceptAsync(assignment.Id, cg.Id.ToString());

        Assert.True(result.Success);
        rig.Mediator.Verify(m => m.Send(It.Is<SendNotificationCommand>(c =>
            c.RecipientId == admin.Id.ToString() && c.Type == NotificationTypes.PackageContractDeliveryFailed), It.IsAny<CancellationToken>()), Times.Once);
        rig.Email.Verify(e => e.SendGenericNotificationEmailAsync(
            admin.Email, It.IsAny<string>(), It.Is<string>(s => s.Contains("Action needed")), It.IsAny<string>(),
            false, null, null), Times.Once);
    }

    [Fact]
    public async Task ContractEmailDeliveryFailure_AlertsOps()
    {
        using var db = CreateDb(NewDbName());
        var client = SeedClient(db); var pkg = SeedPackage(db); var cg = SeedCaregiver(db); var admin = SeedAdmin(db);
        await db.SaveChangesAsync();
        var rig = NewRig(db);
        rig.Email.Setup(e => e.SendContractPdfEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>()))
            .ThrowsAsync(new InvalidOperationException("smtp down"));
        var contracts = new PackageContractService(db, new ContractTemplateService(), new ContractPdfService(),
            rig.Mediator.Object, rig.Email.Object, rig.Ops, Mock.Of<ILogger<PackageContractService>>());
        var svc = AssignmentSvc(db, rig, contracts);
        var requests = new PackageRequestService(db, rig.Ops, Mock.Of<ILogger<PackageRequestService>>());
        var pr = await requests.CreateAsync(client.Id.ToString(), new CreatePackageRequestRequest { PackageId = pkg.Id.ToString(), Location = "Ikoyi" });
        var assignment = await svc.AssignAsync(pr.Id, cg.Id.ToString(), "admin-1", "ops@x", "staff", null);

        var result = await svc.AcceptAsync(assignment.Id, cg.Id.ToString());

        Assert.True(result.Success);
        rig.Mediator.Verify(m => m.Send(It.Is<SendNotificationCommand>(c =>
            c.RecipientId == admin.Id.ToString() && c.Type == NotificationTypes.PackageContractDeliveryFailed), It.IsAny<CancellationToken>()),
            Times.Exactly(2)); // one per failed recipient (client + caregiver)
    }

    // ── Ops alerts reach Approved admins only (same rule as the login gate) ──

    private static bool AlertedInApp(Rig rig, AdminUser a, string type) =>
        rig.Mediator.Invocations.Any(i => i.Arguments[0] is SendNotificationCommand c
            && c.RecipientId == a.Id.ToString() && c.Type == type);

    private static bool AlertedByEmail(Rig rig, AdminUser a) =>
        rig.Email.Invocations.Any(i => i.Method.Name == nameof(IEmailService.SendGenericNotificationEmailAsync)
            && (string)i.Arguments[0] == a.Email);

    [Fact]
    public async Task NewRequest_AlertsApprovedAndLegacyAdmins_NotPendingOrRejected()
    {
        using var db = CreateDb(NewDbName());
        var client = SeedClient(db); var pkg = SeedPackage(db);
        var approved = SeedAdmin(db, AdminUserStatus.Approved, "approved@example.com");
        var legacyNull = SeedAdmin(db, null, "legacy@example.com");
        var pending = SeedAdmin(db, AdminUserStatus.PendingApproval, "pending@example.com");
        var rejected = SeedAdmin(db, AdminUserStatus.Rejected, "rejected@example.com");
        await db.SaveChangesAsync();
        var rig = NewRig(db);
        var svc = new PackageRequestService(db, rig.Ops, Mock.Of<ILogger<PackageRequestService>>());

        await svc.CreateAsync(client.Id.ToString(), new CreatePackageRequestRequest { PackageId = pkg.Id.ToString() });

        Assert.True(AlertedInApp(rig, approved, NotificationTypes.PackageRequestReceived));
        Assert.True(AlertedInApp(rig, legacyNull, NotificationTypes.PackageRequestReceived));
        Assert.False(AlertedInApp(rig, pending, NotificationTypes.PackageRequestReceived));
        Assert.False(AlertedInApp(rig, rejected, NotificationTypes.PackageRequestReceived));
    }

    [Fact]
    public async Task PaymentFailure_AlertsApprovedAdminInAppAndEmail_NotPendingAdmin()
    {
        using var db = CreateDb(NewDbName());
        var client = SeedClient(db); var pkg = SeedPackage(db);
        var approved = SeedAdmin(db, AdminUserStatus.Approved, "approved@example.com");
        var pending = SeedAdmin(db, AdminUserStatus.PendingApproval, "pending@example.com");
        SeedPending(db, client, pkg, "CAREPRO-PKG-FAIL-2");
        await db.SaveChangesAsync();
        var rig = NewRig(db);
        var requests = new Mock<IPackageRequestService>();
        requests.Setup(r => r.CreateAsync(It.IsAny<string>(), It.IsAny<CreatePackageRequestRequest>()))
            .ThrowsAsync(new InvalidOperationException("db exploded"));

        await PaymentSvc(db, rig, requests.Object).CompletePackagePaymentAsync("CAREPRO-PKG-FAIL-2", "flw-f2", 350000m);

        Assert.True(AlertedInApp(rig, approved, NotificationTypes.PackagePaymentRequestFailed));
        Assert.True(AlertedByEmail(rig, approved));
        Assert.False(AlertedInApp(rig, pending, NotificationTypes.PackagePaymentRequestFailed));
        Assert.False(AlertedByEmail(rig, pending));
    }
}
