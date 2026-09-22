using System.Threading;
using Application.Commands;
using Application.DTOs;
using Application.Interfaces.Content;
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
/// Phase 9.7 — Payroll entity, admin CRUD, and the wallet-credit trigger on approval.
/// Uses a real CaregiverWalletService (not mocked) so the wallet-credit assertions and
/// the "existing withdrawal system works unchanged" claim are verified against real
/// wallet-balance mechanics, not a stub. Real local MongoDB (replica set on
/// 127.0.0.1:27018, same infra as the other suites).
/// </summary>
public class PayrollServiceTests
{
    private static CareProDbContext CreateDb(string databaseName)
    {
        var options = new DbContextOptionsBuilder<CareProDbContext>()
            .UseMongoDB("mongodb://127.0.0.1:27018", databaseName)
            .Options;
        return new TestCareProDbContext(options);
    }

    private static string NewDbName() => $"carepro_payroll_tests_{Guid.NewGuid():N}";

    private static CaregiverWalletService CreateWalletService(CareProDbContext db)
        => new(db, Mock.Of<ICareGiverService>(), Mock.Of<ILogger<CaregiverWalletService>>(), new WalletLockManager());

    private static TaskSheetServiceStub HoursStub(CareProDbContext db) => new(db);

    private static PayrollService CreateService(CareProDbContext db, ITaskSheetService? taskSheetService = null,
        IMediator? mediator = null, IEmailService? emailService = null)
        => new(db, taskSheetService ?? HoursStub(db), CreateWalletService(db),
            mediator ?? Mock.Of<IMediator>(), emailService ?? Mock.Of<IEmailService>(),
            Mock.Of<ILogger<PayrollService>>());

    /// <summary>Thin real implementation of the one method PayrollService needs, backed by
    /// the actual Phase 9.6 aggregation logic, rather than mocking the whole interface.</summary>
    private class TaskSheetServiceStub : ITaskSheetService
    {
        private readonly TaskSheetService _real;
        public TaskSheetServiceStub(CareProDbContext db)
        {
            var cloudinary = new CloudinaryDotNet.Cloudinary(new CloudinaryDotNet.Account("default", "default", "default"));
            var options = Microsoft.Extensions.Options.Options.Create(new CloudinarySettings
            { CloudName = "default", ApiKey = "default", ApiSecret = "default" });
            _real = new TaskSheetService(db, new CloudinaryService(cloudinary, options),
                Mock.Of<MediatR.IMediator>(), Mock.Of<Application.Interfaces.Email.IEmailService>(),
                Mock.Of<ILogger<TaskSheetService>>());
        }

        public System.Threading.Tasks.Task<TaskSheetListResponse> GetTaskSheetsByOrderAsync(string orderId, int? billingCycleNumber, string caregiverId, bool isAdmin) => _real.GetTaskSheetsByOrderAsync(orderId, billingCycleNumber, caregiverId, isAdmin);
        public System.Threading.Tasks.Task<TaskSheetDTO> CreateTaskSheetAsync(string orderId, string caregiverId) => _real.CreateTaskSheetAsync(orderId, caregiverId);
        public System.Threading.Tasks.Task<TaskSheetDTO> CreateTaskSheetForAssignmentAsync(string assignmentId, string caregiverId) => _real.CreateTaskSheetForAssignmentAsync(assignmentId, caregiverId);
        public System.Threading.Tasks.Task<CaregiverMonthlyHoursDTO> GetMonthlyHoursForCaregiverAsync(string caregiverId, int year, int month, string? assignmentId = null) => _real.GetMonthlyHoursForCaregiverAsync(caregiverId, year, month, assignmentId);
        public System.Threading.Tasks.Task<TaskSheetDTO> UpdateTaskSheetAsync(string taskSheetId, UpdateTaskSheetRequest request, string caregiverId) => _real.UpdateTaskSheetAsync(taskSheetId, request, caregiverId);
        public System.Threading.Tasks.Task<TaskSheetDTO> SubmitTaskSheetAsync(string taskSheetId, SubmitTaskSheetRequest request, string caregiverId) => _real.SubmitTaskSheetAsync(taskSheetId, request, caregiverId);
        public System.Threading.Tasks.Task<TaskSheetDTO> ClientProposeTasksAsync(string taskSheetId, ClientProposeTasksRequest request, string clientId) => _real.ClientProposeTasksAsync(taskSheetId, request, clientId);
        public System.Threading.Tasks.Task<TaskSheetDTO> RespondToProposedTasksAsync(string taskSheetId, RespondToProposedTasksRequest request, string caregiverId) => _real.RespondToProposedTasksAsync(taskSheetId, request, caregiverId);
        public System.Threading.Tasks.Task<TaskSheetDTO> ActivateTaskSheetAsync(string taskSheetId, string caregiverId) => _real.ActivateTaskSheetAsync(taskSheetId, caregiverId);
        public System.Threading.Tasks.Task PreGenerateTaskSheetsAsync(string contractId, string orderId) => _real.PreGenerateTaskSheetsAsync(contractId, orderId);
        public System.Threading.Tasks.Task<RescheduleTaskSheetResponse> RescheduleTaskSheetAsync(string taskSheetId, RescheduleTaskSheetRequest request, string userId, bool isAdmin) => _real.RescheduleTaskSheetAsync(taskSheetId, request, userId, isAdmin);
    }

    private static Caregiver SeedCaregiver(CareProDbContext db, CaregiverType type, ExperienceTier tier)
    {
        var caregiver = new Caregiver
        {
            Id = ObjectId.GenerateNewId(),
            FirstName = "Payroll",
            LastName = "Subject",
            Email = $"payroll-{Guid.NewGuid():N}@example.com",
            Password = "x", // pragma: allowlist-secret
            Role = "Caregiver",
            Status = true,
            IsAvailable = true,
            CreatedAt = DateTime.UtcNow,
            CaregiverType = type,
            ExperienceTier = tier
        };
        db.CareGivers.Add(caregiver);
        db.SaveChanges();
        return caregiver;
    }

    private static (string assignmentId, string caregiverId, string clientId) SeedAssignmentForPackage(
        CareProDbContext db, Package package, CaregiverType type, ExperienceTier tier)
    {
        db.Packages.Add(package);

        var caregiver = SeedCaregiver(db, type, tier);
        var clientId = ObjectId.GenerateNewId().ToString();

        var packageRequest = new PackageRequest
        {
            Id = ObjectId.GenerateNewId(),
            ClientId = clientId,
            PackageId = package.Id.ToString(),
            PackageCategory = package.Category,
            PackageTierLabel = package.TierLabel,
            RequiredCaregiverType = package.RequiredCaregiverType,
            ServiceCategory = package.Category,
            Status = PackageRequestStatuses.Confirmed,
            ConfirmedCaregiverId = caregiver.Id.ToString(),
            CreatedAt = DateTime.UtcNow
        };
        db.PackageRequests.Add(packageRequest);

        var assignment = new Assignment
        {
            Id = ObjectId.GenerateNewId(),
            PackageRequestId = packageRequest.Id.ToString(),
            CaregiverId = caregiver.Id.ToString(),
            ClientId = clientId,
            Status = AssignmentStatuses.Accepted,
            AssignedByAdminId = ObjectId.GenerateNewId().ToString(),
            AssignedAt = DateTime.UtcNow,
            RespondedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        db.Assignments.Add(assignment);

        db.SaveChanges();
        return (assignment.Id.ToString(), caregiver.Id.ToString(), clientId);
    }

    private static Package HourlyPackage() => new()
    {
        Id = ObjectId.GenerateNewId(),
        Category = PackageCategories.AdultElderCare,
        TierLabel = "Essential",
        RequiredCaregiverType = CaregiverType.AuxiliaryNurse,
        BasePrice = 150000,
        PayCalculationType = Domain.Entities.PayCalculationType.Hourly,
        Description = "test",
        IsActive = true
    };

    private static Package FixedPackage(decimal fixedPay) => new()
    {
        Id = ObjectId.GenerateNewId(),
        Category = PackageCategories.LiveInPackage,
        TierLabel = "Essential",
        RequiredCaregiverType = CaregiverType.AuxiliaryNurse,
        BasePrice = 400000,
        PayCalculationType = Domain.Entities.PayCalculationType.Fixed,
        FixedCaregiverPay = fixedPay,
        Description = "test",
        IsActive = true
    };

    private static void SeedSubmittedTaskSheet(CareProDbContext db, string caregiverId, string assignmentId, DateTime scheduledDate, double minutes, int sheetNumber = 1)
    {
        db.TaskSheets.Add(new TaskSheet
        {
            Id = ObjectId.GenerateNewId(),
            OrderId = string.Empty,
            AssignmentId = assignmentId,
            CaregiverId = caregiverId,
            SheetNumber = sheetNumber,
            Status = "submitted",
            ScheduledDate = scheduledDate,
            SubmittedAt = scheduledDate.AddHours(4),
            VisitDurationMinutes = minutes,
            CreatedAt = scheduledDate,
            UpdatedAt = scheduledDate
        });
        db.SaveChanges();
    }

    // ───────────────────── Create — Fixed ─────────────────────

    [Fact]
    public async Task Create_FixedPackage_UsesFixedCaregiverPay_NoHoursNeeded()
    {
        using var db = CreateDb(NewDbName());
        var (assignmentId, _, _) = SeedAssignmentForPackage(db, FixedPackage(350000), CaregiverType.AuxiliaryNurse, ExperienceTier.Mid);

        var result = await CreateService(db).CreatePayrollAsync(new CreatePayrollRequest
        { AssignmentId = assignmentId, Year = 2026, Month = 3 });

        Assert.Equal("Fixed", result.PayCalculationType);
        Assert.Equal(350000m, result.CalculatedAmount);
        Assert.Equal(350000m, result.FinalAmount);
        Assert.Null(result.HoursWorked);
        Assert.Null(result.RateApplied);
        Assert.Equal("Draft", result.Status);
    }

    // ───────────────────── Create — Hourly ─────────────────────

    [Fact]
    public async Task Create_HourlyPackage_UsesMonthlyHoursTimesActiveRate()
    {
        var dbName = NewDbName();
        using var db = CreateDb(dbName);
        var (assignmentId, caregiverId, _) = SeedAssignmentForPackage(db, HourlyPackage(), CaregiverType.AuxiliaryNurse, ExperienceTier.Senior);

        db.CaregiverPayRates.Add(new CaregiverPayRate
        {
            Id = ObjectId.GenerateNewId(),
            CaregiverType = CaregiverType.AuxiliaryNurse,
            ExperienceTier = ExperienceTier.Senior,
            HourlyRate = 2000,
            IsActive = true
        });
        await db.SaveChangesAsync();

        // 10 hours total in March 2026 (600 minutes across two visits).
        SeedSubmittedTaskSheet(db, caregiverId, assignmentId, new DateTime(2026, 3, 5), 360, sheetNumber: 1);
        SeedSubmittedTaskSheet(db, caregiverId, assignmentId, new DateTime(2026, 3, 12), 240, sheetNumber: 2);

        var result = await CreateService(db).CreatePayrollAsync(new CreatePayrollRequest
        { AssignmentId = assignmentId, Year = 2026, Month = 3 });

        Assert.Equal("Hourly", result.PayCalculationType);
        Assert.Equal(10.0, result.HoursWorked);
        Assert.Equal(2000m, result.RateApplied);
        Assert.Equal(20000m, result.CalculatedAmount);
        Assert.Equal(20000m, result.FinalAmount);
    }

    [Fact]
    public async Task Create_HourlyPackage_NoActiveRate_Throws()
    {
        using var db = CreateDb(NewDbName());
        var (assignmentId, _, _) = SeedAssignmentForPackage(db, HourlyPackage(), CaregiverType.AuxiliaryNurse, ExperienceTier.Junior);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => CreateService(db).CreatePayrollAsync(
            new CreatePayrollRequest { AssignmentId = assignmentId, Year = 2026, Month = 3 }));
    }

    [Fact]
    public async Task Create_HourlyPackage_CaregiverMissingExperienceTier_Throws()
    {
        using var db = CreateDb(NewDbName());
        var package = HourlyPackage();
        db.Packages.Add(package);

        var caregiver = new Caregiver
        {
            Id = ObjectId.GenerateNewId(),
            FirstName = "No", LastName = "Tier",
            Email = $"notier-{Guid.NewGuid():N}@example.com",
            Password = "x", Role = "Caregiver", Status = true, IsAvailable = true, CreatedAt = DateTime.UtcNow,
            CaregiverType = CaregiverType.AuxiliaryNurse
            // ExperienceTier intentionally left unset.
        };
        db.CareGivers.Add(caregiver);

        var packageRequest = new PackageRequest
        {
            Id = ObjectId.GenerateNewId(), ClientId = ObjectId.GenerateNewId().ToString(), PackageId = package.Id.ToString(),
            PackageCategory = package.Category, PackageTierLabel = package.TierLabel, RequiredCaregiverType = package.RequiredCaregiverType,
            ServiceCategory = package.Category, Status = PackageRequestStatuses.Confirmed, CreatedAt = DateTime.UtcNow
        };
        db.PackageRequests.Add(packageRequest);

        var assignment = new Assignment
        {
            Id = ObjectId.GenerateNewId(), PackageRequestId = packageRequest.Id.ToString(), CaregiverId = caregiver.Id.ToString(),
            ClientId = ObjectId.GenerateNewId().ToString(), Status = AssignmentStatuses.Accepted,
            AssignedByAdminId = ObjectId.GenerateNewId().ToString(), AssignedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow
        };
        db.Assignments.Add(assignment);
        db.SaveChanges();

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService(db).CreatePayrollAsync(
            new CreatePayrollRequest { AssignmentId = assignment.Id.ToString(), Year = 2026, Month = 3 }));
    }

    [Fact]
    public async Task Create_PackageWithNoPayCalculationType_Throws()
    {
        using var db = CreateDb(NewDbName());
        var package = HourlyPackage();
        package.PayCalculationType = null; // simulates a legacy Package predating Phase 9.2
        var (assignmentId, _, _) = SeedAssignmentForPackage(db, package, CaregiverType.AuxiliaryNurse, ExperienceTier.Mid);

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService(db).CreatePayrollAsync(
            new CreatePayrollRequest { AssignmentId = assignmentId, Year = 2026, Month = 3 }));
    }

    [Fact]
    public async Task Create_DuplicateAssignmentAndPeriod_Throws()
    {
        using var db = CreateDb(NewDbName());
        var (assignmentId, _, _) = SeedAssignmentForPackage(db, FixedPackage(200000), CaregiverType.AuxiliaryNurse, ExperienceTier.Mid);

        await CreateService(db).CreatePayrollAsync(new CreatePayrollRequest { AssignmentId = assignmentId, Year = 2026, Month = 3 });

        await Assert.ThrowsAsync<ArgumentException>(() => CreateService(db).CreatePayrollAsync(
            new CreatePayrollRequest { AssignmentId = assignmentId, Year = 2026, Month = 3 }));
    }

    // ───────────────────── Approve → wallet credit ─────────────────────

    [Fact]
    public async Task Approve_CreditsWalletDirectlyToWithdrawableBalance_AndExistingWithdrawalWorks()
    {
        var dbName = NewDbName();
        using var db = CreateDb(dbName);
        var (assignmentId, caregiverId, _) = SeedAssignmentForPackage(db, FixedPackage(300000), CaregiverType.AuxiliaryNurse, ExperienceTier.Mid);
        var payroll = await CreateService(db).CreatePayrollAsync(new CreatePayrollRequest { AssignmentId = assignmentId, Year = 2026, Month = 3 });

        var walletService = CreateWalletService(db);
        var payrollService = new PayrollService(db, HoursStub(db), walletService, Mock.Of<IMediator>(), Mock.Of<IEmailService>(), Mock.Of<ILogger<PayrollService>>());

        var approved = await payrollService.ApprovePayrollAsync(payroll.Id, new ApprovePayrollRequest(), "admin1", "admin@carepro.example");

        Assert.Equal("Approved", approved.Status);
        Assert.Equal(300000m, approved.FinalAmount);
        Assert.NotNull(approved.CreditedToWalletAt);
        Assert.Equal("admin@carepro.example", approved.ApprovedByAdminEmail);

        var wallet = await walletService.GetOrCreateWalletAsync(caregiverId);
        Assert.Equal(300000m, wallet.WithdrawableBalance);
        Assert.Equal(300000m, wallet.TotalEarned);

        // Existing withdrawal system works completely unchanged against this credit.
        await walletService.DebitWithdrawalAsync(caregiverId, 300000m);
        var afterWithdrawal = await walletService.GetOrCreateWalletAsync(caregiverId);
        Assert.Equal(0m, afterWithdrawal.WithdrawableBalance);
        Assert.Equal(300000m, afterWithdrawal.TotalWithdrawn);
    }

    [Fact]
    public async Task Approve_WithOverride_RequiresReason_AndWritesAuditLog()
    {
        var dbName = NewDbName();
        using var db = CreateDb(dbName);
        var (assignmentId, _, _) = SeedAssignmentForPackage(db, FixedPackage(300000), CaregiverType.AuxiliaryNurse, ExperienceTier.Mid);
        var payroll = await CreateService(db).CreatePayrollAsync(new CreatePayrollRequest { AssignmentId = assignmentId, Year = 2026, Month = 3 });

        var payrollService = new PayrollService(db, HoursStub(db), CreateWalletService(db), Mock.Of<IMediator>(), Mock.Of<IEmailService>(), Mock.Of<ILogger<PayrollService>>());

        // Override without a reason is rejected.
        await Assert.ThrowsAsync<ArgumentException>(() => payrollService.ApprovePayrollAsync(
            payroll.Id, new ApprovePayrollRequest { FinalAmount = 250000 }, "admin1", "admin@carepro.example"));

        var approved = await payrollService.ApprovePayrollAsync(payroll.Id,
            new ApprovePayrollRequest { FinalAmount = 250000, OverrideReason = "Caregiver missed 2 days, adjusted downward." },
            "admin1", "admin@carepro.example");

        Assert.Equal(250000m, approved.FinalAmount);
        Assert.Equal(300000m, approved.CalculatedAmount);
        Assert.Equal("Caregiver missed 2 days, adjusted downward.", approved.OverrideReason);

        var wallet = await CreateWalletService(db).GetOrCreateWalletAsync((await db.Payrolls.FirstAsync()).CaregiverId);
        Assert.Equal(250000m, wallet.WithdrawableBalance);

        var auditEntry = await db.AdminAuditLogs.FirstOrDefaultAsync(a => a.TargetEntityType == "Payroll");
        Assert.NotNull(auditEntry);
        Assert.Equal("PayrollFinalAmountOverride", auditEntry!.Action);
        Assert.Equal("admin1", auditEntry.AdminId);
    }

    [Fact]
    public async Task Approve_NotifiesCaregiver_InApp_AndEmail()
    {
        using var db = CreateDb(NewDbName());
        var (assignmentId, caregiverId, _) = SeedAssignmentForPackage(db, FixedPackage(275000), CaregiverType.AuxiliaryNurse, ExperienceTier.Mid);
        var caregiverEmail = (await db.CareGivers.FirstAsync(c => c.Id.ToString() == caregiverId)).Email;

        var mediator = new Mock<IMediator>();
        var email = new Mock<IEmailService>();
        var payrollService = new PayrollService(db, HoursStub(db), CreateWalletService(db),
            mediator.Object, email.Object, Mock.Of<ILogger<PayrollService>>());

        var payroll = await payrollService.CreatePayrollAsync(new CreatePayrollRequest { AssignmentId = assignmentId, Year = 2026, Month = 3 });
        await payrollService.ApprovePayrollAsync(payroll.Id, new ApprovePayrollRequest(), "admin1", "admin@carepro.example");

        // In-app: a PayrollApproved notification to the caregiver.
        mediator.Verify(m => m.Send(
            It.Is<SendNotificationCommand>(c =>
                c.RecipientId == caregiverId && c.Type == NotificationTypes.PayrollApproved),
            It.IsAny<CancellationToken>()), Times.Once);

        // Email: the policy-#10 earnings email, addressed to the caregiver, for the credited amount.
        email.Verify(e => e.SendEarningsNotificationEmailAsync(
            caregiverEmail, It.IsAny<string>(), 275000m, It.IsAny<string>(), It.IsAny<string>()),
            Times.Once);

        // Mark paid also notifies (in-app + email).
        await payrollService.MarkPayrollPaidAsync(payroll.Id);
        mediator.Verify(m => m.Send(
            It.Is<SendNotificationCommand>(c =>
                c.RecipientId == caregiverId && c.Type == NotificationTypes.PayrollPaid),
            It.IsAny<CancellationToken>()), Times.Once);
        email.Verify(e => e.SendEarningsNotificationEmailAsync(
            caregiverEmail, It.IsAny<string>(), 275000m, It.IsAny<string>(), It.IsAny<string>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task Approve_NotificationFailure_DoesNotRollBackWalletCredit()
    {
        using var db = CreateDb(NewDbName());
        var (assignmentId, caregiverId, _) = SeedAssignmentForPackage(db, FixedPackage(120000), CaregiverType.AuxiliaryNurse, ExperienceTier.Mid);

        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<SendNotificationCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("notification bus down"));
        var walletService = CreateWalletService(db);
        var payrollService = new PayrollService(db, HoursStub(db), walletService,
            mediator.Object, Mock.Of<IEmailService>(), Mock.Of<ILogger<PayrollService>>());

        var payroll = await payrollService.CreatePayrollAsync(new CreatePayrollRequest { AssignmentId = assignmentId, Year = 2026, Month = 3 });
        var approved = await payrollService.ApprovePayrollAsync(payroll.Id, new ApprovePayrollRequest(), "admin1", "admin@carepro.example");

        Assert.Equal("Approved", approved.Status);
        Assert.NotNull(approved.CreditedToWalletAt);
        var wallet = await walletService.GetOrCreateWalletAsync(caregiverId);
        Assert.Equal(120000m, wallet.WithdrawableBalance);
    }

    [Fact]
    public async Task Approve_AlreadyApproved_Throws()
    {
        using var db = CreateDb(NewDbName());
        var (assignmentId, _, _) = SeedAssignmentForPackage(db, FixedPackage(100000), CaregiverType.AuxiliaryNurse, ExperienceTier.Mid);
        var payrollService = new PayrollService(db, HoursStub(db), CreateWalletService(db), Mock.Of<IMediator>(), Mock.Of<IEmailService>(), Mock.Of<ILogger<PayrollService>>());

        var payroll = await payrollService.CreatePayrollAsync(new CreatePayrollRequest { AssignmentId = assignmentId, Year = 2026, Month = 3 });
        await payrollService.ApprovePayrollAsync(payroll.Id, new ApprovePayrollRequest(), "admin1", "admin@carepro.example");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            payrollService.ApprovePayrollAsync(payroll.Id, new ApprovePayrollRequest(), "admin1", "admin@carepro.example"));
    }

    // ───────────────────── MarkPaid / Delete ─────────────────────

    [Fact]
    public async Task MarkPaid_RequiresApprovedFirst()
    {
        using var db = CreateDb(NewDbName());
        var (assignmentId, _, _) = SeedAssignmentForPackage(db, FixedPackage(100000), CaregiverType.AuxiliaryNurse, ExperienceTier.Mid);
        var payrollService = new PayrollService(db, HoursStub(db), CreateWalletService(db), Mock.Of<IMediator>(), Mock.Of<IEmailService>(), Mock.Of<ILogger<PayrollService>>());
        var payroll = await payrollService.CreatePayrollAsync(new CreatePayrollRequest { AssignmentId = assignmentId, Year = 2026, Month = 3 });

        await Assert.ThrowsAsync<InvalidOperationException>(() => payrollService.MarkPayrollPaidAsync(payroll.Id));

        await payrollService.ApprovePayrollAsync(payroll.Id, new ApprovePayrollRequest(), "admin1", "admin@carepro.example");
        Assert.True(await payrollService.MarkPayrollPaidAsync(payroll.Id));

        var fetched = await payrollService.GetPayrollByIdAsync(payroll.Id);
        Assert.Equal("Paid", fetched!.Status);
    }

    [Fact]
    public async Task Delete_OnlyAllowedForDraft()
    {
        using var db = CreateDb(NewDbName());
        var (assignmentId, _, _) = SeedAssignmentForPackage(db, FixedPackage(100000), CaregiverType.AuxiliaryNurse, ExperienceTier.Mid);
        var payrollService = new PayrollService(db, HoursStub(db), CreateWalletService(db), Mock.Of<IMediator>(), Mock.Of<IEmailService>(), Mock.Of<ILogger<PayrollService>>());
        var payroll = await payrollService.CreatePayrollAsync(new CreatePayrollRequest { AssignmentId = assignmentId, Year = 2026, Month = 3 });

        await payrollService.ApprovePayrollAsync(payroll.Id, new ApprovePayrollRequest(), "admin1", "admin@carepro.example");

        await Assert.ThrowsAsync<InvalidOperationException>(() => payrollService.DeletePayrollAsync(payroll.Id));
    }

    [Fact]
    public async Task GetAllPayrolls_FiltersByCaregiver()
    {
        using var db = CreateDb(NewDbName());
        var (assignment1, caregiver1, _) = SeedAssignmentForPackage(db, FixedPackage(100000), CaregiverType.AuxiliaryNurse, ExperienceTier.Mid);
        var (assignment2, caregiver2, _) = SeedAssignmentForPackage(db, FixedPackage(150000), CaregiverType.CHEW, ExperienceTier.Senior);

        var service = CreateService(db);
        await service.CreatePayrollAsync(new CreatePayrollRequest { AssignmentId = assignment1, Year = 2026, Month = 3 });
        await service.CreatePayrollAsync(new CreatePayrollRequest { AssignmentId = assignment2, Year = 2026, Month = 3 });

        var all = await service.GetAllPayrollsAsync();
        Assert.Equal(2, all.Count);

        var forCaregiver1 = await service.GetAllPayrollsAsync(caregiver1);
        Assert.Single(forCaregiver1);
        Assert.Equal(caregiver1, forCaregiver1[0].CaregiverId);
    }
}
