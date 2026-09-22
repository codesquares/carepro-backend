using Application.DTOs;
using Application.Interfaces.Content;
using Application.Interfaces.Email;
using CloudinaryDotNet;
using Domain.Entities;
using Infrastructure.Content.Data;
using Infrastructure.Content.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MongoDB.Bson;
using Xunit;

namespace CommitmentGate.Tests;

/// <summary>
/// Phase 9 — full end-to-end evidence: a caregiver accepts a package assignment
/// (Phase 4/9.8: confirmation credits nothing), performs a real check-in/checkout
/// producing real hours (Phase 9.4/9.5/9.6), admin creates and approves a payroll
/// record using the rate table (Phase 9.1/9.3/9.7), and the caregiver's wallet is
/// credited correctly — with zero automatic credit at any earlier point in the flow.
/// Real local MongoDB (replica set on 127.0.0.1:27018, same infra as the other suites).
/// </summary>
public class Phase9EndToEndPayrollTests
{
    private static CareProDbContext CreateDb(string name)
    {
        var options = new DbContextOptionsBuilder<CareProDbContext>()
            .UseMongoDB("mongodb://127.0.0.1:27018", name).Options;
        return new TestCareProDbContext(options);
    }

    private static string NewDbName() => $"carepro_phase9_e2e_{Guid.NewGuid():N}";

    private static WalletLockManager Lock() => new();

    private static CaregiverWalletService CgWallet(CareProDbContext db) =>
        new(db, Mock.Of<ICareGiverService>(), Mock.Of<ILogger<CaregiverWalletService>>(), Lock());

    private static CloudinaryService CreateCloudinaryService()
    {
        var cloudinary = new Cloudinary(new Account("default", "default", "default"));
        var options = Options.Create(new CloudinarySettings { CloudName = "default", ApiKey = "default", ApiSecret = "default" });
        return new CloudinaryService(cloudinary, options);
    }

    private static TaskSheetService CreateTaskSheetService(CareProDbContext db) =>
        new(db, CreateCloudinaryService(), Mock.Of<IMediator>(), Mock.Of<IEmailService>(), Mock.Of<ILogger<TaskSheetService>>());

    private static VisitCheckinService CreateCheckinService(CareProDbContext db) =>
        new(db, Mock.Of<IGeocodingService>(), new ConfigurationBuilder().Build(), Mock.Of<IHostEnvironment>(),
            Mock.Of<IMediator>(), Mock.Of<ILogger<VisitCheckinService>>());

    // Real ContractTemplateService + real ContractPdfService — no external deps, same as Phase6ContractTests.
    private static PackageContractService CreateContractService(CareProDbContext db) =>
        new(db, new ContractTemplateService(), new ContractPdfService(), Mock.Of<IMediator>(),
            Mock.Of<IEmailService>(), Mock.Of<ILogger<PackageContractService>>());

    private static AssignmentService CreateAssignmentService(CareProDbContext db, PackageContractService contractService)
    {
        var readiness = new Mock<ICaregiverReadinessService>();
        readiness.Setup(r => r.GetReadinessAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool?>()))
            .ReturnsAsync(new CaregiverReadinessResult { IsReady = true });
        return new AssignmentService(
            db, Mock.Of<IMediator>(), Mock.Of<IEmailService>(), readiness.Object,
            contractService, Mock.Of<ILogger<AssignmentService>>());
    }

    [Fact]
    public async Task FullFlow_AcceptCheckinSubmitPayrollApprove_CreditsWalletCorrectly_WithNoEarlierAutoCredit()
    {
        var dbName = NewDbName();
        string caregiverId, assignmentId, taskSheetId, payrollId;
        decimal expectedAmount;

        using (var db = CreateDb(dbName))
        {
            // ── Setup: client, package (Hourly), caregiver classified for payroll, rate table ──
            var client = new Client
            {
                Id = ObjectId.GenerateNewId(), FirstName = "Cli", LastName = "Ent",
                Email = $"cl-{Guid.NewGuid():N}@x.com", Password = "x", Role = "Client", IsDeleted = false // pragma: allowlist-secret
            };
            var package = new Package
            {
                Id = ObjectId.GenerateNewId(), Category = PackageCategories.AdultElderCare, TierLabel = "Essential",
                RequiredCaregiverType = CaregiverType.AuxiliaryNurse, BasePrice = 150000,
                PayCalculationType = Domain.Entities.PayCalculationType.Hourly,
                Description = "d", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            };
            var caregiver = new Caregiver
            {
                Id = ObjectId.GenerateNewId(), FirstName = "Care", LastName = "Giver",
                Email = $"cg-{Guid.NewGuid():N}@x.com", Password = "x", Role = "Caregiver", // pragma: allowlist-secret
                Status = true, IsAvailable = true, IsIdentityVerified = true, CreatedAt = DateTime.UtcNow,
                CaregiverType = CaregiverType.AuxiliaryNurse, ExperienceTier = ExperienceTier.Mid
            };
            db.Clients.Add(client);
            db.Packages.Add(package);
            db.CareGivers.Add(caregiver);
            db.CaregiverPayRates.Add(new CaregiverPayRate
            {
                Id = ObjectId.GenerateNewId(), CaregiverType = CaregiverType.AuxiliaryNurse,
                ExperienceTier = ExperienceTier.Mid, HourlyRate = 1800, IsActive = true
            });
            await db.SaveChangesAsync();
            caregiverId = caregiver.Id.ToString();

            // ── Phase 4: request → assign → accept ──
            var contractService = CreateContractService(db);
            var assignmentService = CreateAssignmentService(db, contractService);
            var requestService = new PackageRequestService(db, Mock.Of<ILogger<PackageRequestService>>());

            var packageRequest = await requestService.CreateAsync(client.Id.ToString(), new CreatePackageRequestRequest
            { PackageId = package.Id.ToString(), ServiceCategory = package.Category });

            var assignment = await assignmentService.AssignAsync(packageRequest.Id, caregiverId, "admin-1", "ops@x", "staff", null);
            assignmentId = assignment.Id;

            var wallet = await CgWallet(db).GetOrCreateWalletAsync(caregiverId);
            Assert.Equal(0m, wallet.WithdrawableBalance);
            Assert.Equal(0m, wallet.PendingBalance);

            await assignmentService.AcceptAsync(assignment.Id, caregiverId);

            // Phase 9.8: acceptance/confirmation must NOT touch the wallet at all anymore.
            var afterAccept = await CgWallet(db).GetOrCreateWalletAsync(caregiverId);
            Assert.Equal(0m, afterAccept.WithdrawableBalance);
            Assert.Equal(0m, afterAccept.PendingBalance);
            Assert.Equal(0m, afterAccept.TotalEarned);

            var confirmedRequest = await db.PackageRequests.FirstAsync(p => p.Id == ObjectId.Parse(packageRequest.Id));
            Assert.False(confirmedRequest.CaregiverCredited);
        }

        // ── Phase 9.5: real check-in → submit, producing real hours ──
        using (var db = CreateDb(dbName))
        {
            var taskSheetService = CreateTaskSheetService(db);
            var taskSheet = await taskSheetService.CreateTaskSheetForAssignmentAsync(assignmentId, caregiverId);
            taskSheetId = taskSheet.Id;

            await CreateCheckinService(db).CheckinAsync(new VisitCheckinRequest
            {
                TaskSheetId = taskSheetId,
                OrderId = null,
                Latitude = 6.5,
                Longitude = 3.3,
                Accuracy = 10,
                CheckinTimestamp = DateTime.UtcNow
            }, caregiverId);

            // Backdate the server-authoritative check-in time to simulate a real ~2-hour
            // visit — check-in and submit otherwise happen milliseconds apart in a test,
            // which would round to 0 minutes and defeat the point of this evidence.
            var checkin = await db.VisitCheckins.FirstAsync(c => c.TaskSheetId == taskSheetId);
            checkin.ServerReceivedAt = DateTime.UtcNow.AddHours(-2);
            await db.SaveChangesAsync();

            var submitted = await taskSheetService.SubmitTaskSheetAsync(taskSheetId, new SubmitTaskSheetRequest(), caregiverId);
            Assert.Equal("submitted", submitted.Status);
            Assert.NotNull(submitted.VisitDurationMinutes);
            Assert.True(submitted.VisitDurationMinutes >= 119 && submitted.VisitDurationMinutes <= 121);

            // Still zero credited — payroll hasn't run yet.
            var stillZero = await CgWallet(db).GetOrCreateWalletAsync(caregiverId);
            Assert.Equal(0m, stillZero.WithdrawableBalance);
        }

        // ── Phase 9.7: admin creates and approves payroll for this month using the rate table ──
        var today = DateTime.UtcNow.Date;
        using (var db = CreateDb(dbName))
        {
            var payrollService = new PayrollService(db, CreateTaskSheetService(db), CgWallet(db),
                Mock.Of<IMediator>(), Mock.Of<IEmailService>(), Mock.Of<ILogger<PayrollService>>());

            var payroll = await payrollService.CreatePayrollAsync(new CreatePayrollRequest
            { AssignmentId = assignmentId, Year = today.Year, Month = today.Month });
            payrollId = payroll.Id;

            Assert.Equal("Hourly", payroll.PayCalculationType);
            Assert.Equal(1800m, payroll.RateApplied);
            Assert.NotNull(payroll.HoursWorked);
            expectedAmount = Math.Round((decimal)payroll.HoursWorked!.Value * 1800m, 2);
            Assert.Equal(expectedAmount, payroll.CalculatedAmount);
            Assert.Equal("Draft", payroll.Status);

            var approved = await payrollService.ApprovePayrollAsync(payrollId, new ApprovePayrollRequest(), "admin-9", "ops@carepro.example");
            Assert.Equal("Approved", approved.Status);
            Assert.Equal(expectedAmount, approved.FinalAmount);
            Assert.NotNull(approved.CreditedToWalletAt);
        }

        // ── Final: wallet correctly and exclusively credited via payroll approval ──
        using (var db = CreateDb(dbName))
        {
            var wallet = await CgWallet(db).GetOrCreateWalletAsync(caregiverId);
            Assert.Equal(expectedAmount, wallet.WithdrawableBalance);
            Assert.Equal(expectedAmount, wallet.TotalEarned);
            Assert.Equal(0m, wallet.PendingBalance); // never touched — no per-visit release ceremony needed

            // Existing withdrawal system works completely unchanged against this credit.
            await CgWallet(db).DebitWithdrawalAsync(caregiverId, expectedAmount);
            var afterWithdrawal = await CgWallet(db).GetOrCreateWalletAsync(caregiverId);
            Assert.Equal(0m, afterWithdrawal.WithdrawableBalance);
            Assert.Equal(expectedAmount, afterWithdrawal.TotalWithdrawn);
        }
    }
}
