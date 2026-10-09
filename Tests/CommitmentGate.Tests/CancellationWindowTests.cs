using System.Text;
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
using CloudinaryDotNet;
using Infrastructure.Content.Services;
using Microsoft.Extensions.Options;
using Domain.Settings;
using Xunit;

namespace CommitmentGate.Tests;

/// <summary>Client 48-hour cancellation window on package contracts (hours are configurable).</summary>
public class CancellationWindowTests
{
    private static PackageContractService Svc(CareProDbContext db, Mock<IMediator>? mediator = null, Mock<IOpsAlertService>? ops = null,
        Mock<IEmailService>? email = null, int? hours = null)
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(
            hours.HasValue ? new Dictionary<string, string?> { ["PackageContract:CancellationWindowHours"] = hours.Value.ToString() } : new()).Build();
        return new PackageContractService(db, new ContractTemplateService(), new ContractPdfService(),
            (mediator ?? new Mock<IMediator>()).Object, (email ?? new Mock<IEmailService>()).Object,
            (ops ?? new Mock<IOpsAlertService>()).Object, Mock.Of<ILogger<PackageContractService>>(), cfg);
    }

    private static async Task<(CareProDbContext db, string prId, string clientId, string cgId)> Generated(int? hours = null, Mock<IEmailService>? email = null)
    {
        var db = Phase6ContractTests.CreateDb(Phase6ContractTests.NewDbName());
        var (client, pkg, cg) = Phase6ContractTests.Seed(db);
        var pr = Phase6ContractTests.AddConfirmedRequest(db, client, pkg, cg);
        db.Assignments.Add(new Assignment { Id = MongoDB.Bson.ObjectId.GenerateNewId(), PackageRequestId = pr.Id.ToString(), CaregiverId = cg.Id.ToString(),
            ClientId = client.Id.ToString(), Status = AssignmentStatuses.Accepted, AssignedAt = DateTime.UtcNow, RespondedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        await Svc(db, email: email, hours: hours).GenerateForConfirmedRequestAsync(pr.Id.ToString());
        return (db, pr.Id.ToString(), client.Id.ToString(), cg.Id.ToString());
    }

    [Fact]
    public async Task Generation_RecordsSentAndWindowEnd_DefaultsTo48h_AndAcknowledgementIsInHtmlAndPdf()
    {
        var (db, prId, _, _) = await Generated();
        using (db)
        {
            var dto = (await Svc(db).GetByPackageRequestAsync(prId))!;
            Assert.Equal(48, dto.CancellationWindowHours);
            Assert.True(dto.CancellationWindowOpen);
            Assert.Equal(dto.ContractSentAt!.Value.AddHours(48), dto.CancellationWindowEndsAt);
            Assert.Contains("You have 48 hours from", dto.GeneratedTermsHtml);
            Assert.Contains("you confirm that you have read and agree to all the terms of this agreement", dto.GeneratedTermsHtml);
            Assert.Contains("You have 48 hours from", dto.CancellationAcknowledgement);
            var pdf = await Svc(db).GeneratePdfAsync(dto.Id);
            Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
        }
    }

    [Fact]
    public async Task WindowHours_AreConfigurable()
    {
        var (db, prId, _, _) = await Generated(hours: 72);
        using (db) Assert.Equal(72, (await Svc(db).GetByPackageRequestAsync(prId))!.CancellationWindowHours);
    }

    [Fact]
    public async Task Email_ToClient_CarriesAcknowledgement_CaregiverDoesNot()
    {
        var email = new Mock<IEmailService>();
        var (db, _, _, _) = await Generated(email: email);
        using (db)
        {
            email.Verify(e => e.SendContractPdfEmailAsync(It.Is<string>(a => a.StartsWith("client-")), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<byte[]>(), It.Is<string?>(t => t != null && t.Contains("You have 48 hours from"))), Times.Once);
            email.Verify(e => e.SendContractPdfEmailAsync(It.Is<string>(a => a.StartsWith("cg-")), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<byte[]>(), It.Is<string?>(t => t == null)), Times.Once);
        }
    }

    [Fact]
    public async Task CancelInsideWindow_CancelsContractRequestAssignment_Notifies_AndAlertsAdmins_NoAutoRefund()
    {
        var mediator = new Mock<IMediator>(); var ops = new Mock<IOpsAlertService>();
        var (db, prId, clientId, _) = await Generated();
        using (db)
        {
            var dto = await Svc(db, mediator, ops).CancelByClientAsync(prId, clientId);
            Assert.Equal("Cancelled", dto.Status);
            Assert.NotNull(dto.CancelledAt);
            Assert.Equal($"Client:{clientId}", dto.CancelledBy);
            Assert.False(dto.CancellationWindowOpen);
            Assert.Equal(PackageRequestStatuses.Cancelled, (await db.PackageRequests.FirstAsync()).Status);
            Assert.Equal(AssignmentStatuses.Cancelled, (await db.Assignments.FirstAsync()).Status);
            mediator.Verify(m => m.Send(It.Is<Application.Commands.SendNotificationCommand>(c => c.Type == NotificationTypes.PackageAssignmentCancelled), It.IsAny<CancellationToken>()), Times.Once);
            ops.Verify(o => o.NotifyAdminsAsync(NotificationTypes.PackageRequestCancelledByClient, It.IsAny<string>(),
                It.Is<string>(c => c.Contains("No refund has been issued automatically")), prId, false), Times.Once);
            // Double call: safe result (200-equivalent), no duplicate notifications or admin alerts.
            var again = await Svc(db, mediator, ops).CancelByClientAsync(prId, clientId);
            Assert.Equal("Cancelled", again.Status);
            Assert.Equal(dto.CancelledAt, again.CancelledAt);
            mediator.Verify(m => m.Send(It.IsAny<Application.Commands.SendNotificationCommand>(), It.IsAny<CancellationToken>()), Times.Once);
            ops.Verify(o => o.NotifyAdminsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Once);
        }
    }

    [Fact]
    public async Task CancelAfterWindow_IsRefused_AndAgreementByNonCancellationIsRecordedAtWindowEnd()
    {
        var (db, prId, clientId, _) = await Generated();
        using (db)
        {
            var c = await db.Contracts.FirstAsync();
            c.ContractSentAt = DateTime.UtcNow.AddHours(-50); c.CancellationWindowEndsAt = DateTime.UtcNow.AddHours(-2);
            await db.SaveChangesAsync();
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Svc(db).CancelByClientAsync(prId, clientId));
            Assert.Contains("cancellation window ended", ex.Message);
            var after = await db.Contracts.AsNoTracking().FirstAsync();
            Assert.Equal(after.CancellationWindowEndsAt, after.AcceptedByNonCancellationAt);
            Assert.Null(after.CancelledAt);
            Assert.False((await Svc(db).GetByPackageRequestAsync(prId))!.CancellationWindowOpen);
        }
    }

    [Fact]
    public async Task Sweeper_RecordsNonCancellation_ForExpiredWindows_Only()
    {
        var (db, prId, _, _) = await Generated();
        using (db)
        {
            Assert.Equal(0, await Svc(db).FinalizeExpiredWindowsAsync()); // still open
            var c = await db.Contracts.FirstAsync();
            c.CancellationWindowEndsAt = DateTime.UtcNow.AddMinutes(-1); await db.SaveChangesAsync();
            Assert.Equal(1, await Svc(db).FinalizeExpiredWindowsAsync());
            Assert.NotNull((await db.Contracts.AsNoTracking().FirstAsync()).AcceptedByNonCancellationAt);
        }
    }

    [Fact]
    public async Task AnotherClient_CannotCancel()
    {
        var (db, prId, _, _) = await Generated();
        using (db) await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Svc(db).CancelByClientAsync(prId, MongoDB.Bson.ObjectId.GenerateNewId().ToString()));
    }

    [Fact]
    public async Task LegacyPackageContractWithoutWindow_LoadsAndHasNoAcknowledgement()
    {
        var (db, prId, clientId, _) = await Generated();
        using (db)
        {
            var c = await db.Contracts.FirstAsync();
            c.ContractSentAt = null; c.CancellationWindowEndsAt = null; await db.SaveChangesAsync();
            var dto = (await Svc(db).GetByPackageRequestAsync(prId))!;
            Assert.Null(dto.CancellationAcknowledgement);
            Assert.False(dto.CancellationWindowOpen);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Svc(db).CancelByClientAsync(prId, clientId));
        }
    }

    // ───────────── Visits are blocked until the window ends ─────────────

    private static TaskSheetService TaskSheets(CareProDbContext db) => new(db,
        new CloudinaryService(new Cloudinary(new Account("default", "default", "default")),
            Options.Create(new CloudinarySettings { CloudName = "default", ApiKey = "default", ApiSecret = "default" })),
        Mock.Of<IMediator>(), Mock.Of<IEmailService>(), Mock.Of<ILogger<TaskSheetService>>());

    [Fact]
    public async Task StartingAVisit_InsideTheWindow_IsBlocked_WithReasonCodeAndEndTime_ThenAllowedAfter()
    {
        var (db, prId, _, cgId) = await Generated();
        using (db)
        {
            var a = await db.Assignments.FirstAsync();
            var ex = await Assert.ThrowsAsync<CancellationWindowOpenException>(() => TaskSheets(db).CreateTaskSheetForAssignmentAsync(a.Id.ToString(), cgId));
            Assert.Equal("cancellation_window_open", ex.ErrorCode);
            var c = await db.Contracts.FirstAsync();
            Assert.Equal(c.CancellationWindowEndsAt, ex.WindowEndsAt);
            Assert.Empty(await db.TaskSheets.ToListAsync());

            c.CancellationWindowEndsAt = DateTime.UtcNow.AddMinutes(-1); await db.SaveChangesAsync();
            var sheet = await TaskSheets(db).CreateTaskSheetForAssignmentAsync(a.Id.ToString(), cgId);
            Assert.Equal(a.Id.ToString(), sheet.AssignmentId);
        }
    }

    [Fact]
    public async Task CancelledContract_BlocksVisits()
    {
        var (db, prId, clientId, cgId) = await Generated();
        using (db)
        {
            var a = await db.Assignments.FirstAsync();
            await Svc(db).CancelByClientAsync(prId, clientId);
            // assignment is no longer Accepted, so creation is refused outright
            await Assert.ThrowsAsync<InvalidOperationException>(() => TaskSheets(db).CreateTaskSheetForAssignmentAsync(a.Id.ToString(), cgId));
            // and the guard itself refuses a cancelled contract
            var c = await db.Contracts.AsNoTracking().FirstAsync();
            Assert.Throws<InvalidOperationException>(() => CancellationWindowGuard.EnsureCareMayBegin(c, DateTime.UtcNow));
        }
    }

    [Fact]
    public async Task WindowConfiguredAsZero_BlocksNothing_AndShowsNoAcknowledgement()
    {
        var (db, prId, _, cgId) = await Generated(hours: 0);
        using (db)
        {
            var dto = (await Svc(db, hours: 0).GetByPackageRequestAsync(prId))!;
            Assert.Equal(0, dto.CancellationWindowHours);
            Assert.False(dto.CancellationWindowOpen);
            Assert.Null(dto.CancellationAcknowledgement);
            Assert.DoesNotContain("You have 0 hours", dto.GeneratedTermsHtml);
            var a = await db.Assignments.FirstAsync();
            var sheet = await TaskSheets(db).CreateTaskSheetForAssignmentAsync(a.Id.ToString(), cgId);
            Assert.NotNull(sheet);
        }
    }

    [Fact]
    public async Task ContractStartDate_IsNeverBeforeTheWindowEndDate()
    {
        var (db, prId, _, _) = await Generated();
        using (db)
        {
            var dto = (await Svc(db).GetByPackageRequestAsync(prId))!;
            Assert.True(dto.ContractStartDate.Date >= dto.CancellationWindowEndsAt!.Value.Date,
                $"start {dto.ContractStartDate:O} must not precede window end {dto.CancellationWindowEndsAt:O}");
            Assert.Contains(dto.ContractStartDate.ToString("dddd, MMMM dd, yyyy"), dto.GeneratedTermsHtml);
        }
    }

    // ───────────── Explicit "I agree" tick ─────────────

    [Fact]
    public async Task Agree_RecordsTimestampAndClientFromCaller_IsIdempotent_AndDoesNotTouchTheWindow()
    {
        var (db, prId, clientId, cgId) = await Generated();
        using (db)
        {
            var before = (await Svc(db).GetByPackageRequestAsync(prId))!;
            Assert.Null(before.ClientAgreedAt);
            var first = await Svc(db).AgreeByClientAsync(prId, clientId);
            Assert.NotNull(first.ClientAgreedAt);
            Assert.Equal(clientId, first.ClientAgreedBy);
            Assert.True(first.CancellationWindowOpen);                       // does not shorten the window
            Assert.Equal(before.CancellationWindowEndsAt, first.CancellationWindowEndsAt);
            var second = await Svc(db).AgreeByClientAsync(prId, clientId);
            Assert.Equal(first.ClientAgreedAt, second.ClientAgreedAt);       // idempotent: first timestamp kept
            // does not unblock care
            var a = await db.Assignments.FirstAsync();
            await Assert.ThrowsAsync<CancellationWindowOpenException>(() => TaskSheets(db).CreateTaskSheetForAssignmentAsync(a.Id.ToString(), cgId));
        }
    }

    [Fact]
    public async Task Agree_AnotherClient_IsRefused_AndNotAgreeing_DoesNotBlockCareAfterTheWindow()
    {
        var (db, prId, _, cgId) = await Generated();
        using (db)
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Svc(db).AgreeByClientAsync(prId, MongoDB.Bson.ObjectId.GenerateNewId().ToString()));
            var c = await db.Contracts.FirstAsync();
            c.CancellationWindowEndsAt = DateTime.UtcNow.AddMinutes(-1); await db.SaveChangesAsync();
            var a = await db.Assignments.FirstAsync();
            Assert.NotNull(await TaskSheets(db).CreateTaskSheetForAssignmentAsync(a.Id.ToString(), cgId)); // never ticked, care still allowed
        }
    }

    // ───────────── Cancel hardening ─────────────

    [Fact]
    public async Task ConcurrentCancels_ProduceOneOutcome_OneNotificationSet_OneAdminAlert()
    {
        var name = Phase6ContractTests.NewDbName();
        var mediator = new Mock<IMediator>(); var ops = new Mock<IOpsAlertService>();
        string prId, clientId;
        using (var seed = Phase6ContractTests.CreateDb(name))
        {
            var (client, pkg, cg) = Phase6ContractTests.Seed(seed);
            var pr = Phase6ContractTests.AddConfirmedRequest(seed, client, pkg, cg);
            seed.Assignments.Add(new Assignment { Id = MongoDB.Bson.ObjectId.GenerateNewId(), PackageRequestId = pr.Id.ToString(), CaregiverId = cg.Id.ToString(),
                ClientId = client.Id.ToString(), Status = AssignmentStatuses.Accepted, AssignedAt = DateTime.UtcNow, RespondedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow });
            await seed.SaveChangesAsync();
            await Svc(seed).GenerateForConfirmedRequestAsync(pr.Id.ToString());
            prId = pr.Id.ToString(); clientId = client.Id.ToString();
        }
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            using var db = Phase6ContractTests.CreateDb(name);
            try { return (await Svc(db, mediator, ops).CancelByClientAsync(prId, clientId)).Status; }
            catch (Exception e) { return "ERR:" + e.GetType().Name; }
        }));
        using var check = Phase6ContractTests.CreateDb(name);
        Assert.Equal(AssignmentStatuses.Cancelled, (await check.Assignments.AsNoTracking().FirstAsync()).Status);
        Assert.Equal(PackageRequestStatuses.Cancelled, (await check.PackageRequests.AsNoTracking().FirstAsync()).Status);
        Assert.NotNull((await check.Contracts.AsNoTracking().FirstAsync()).CancelledAt);
        ops.Verify(o => o.NotifyAdminsAsync(NotificationTypes.PackageRequestCancelledByClient, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Once);
        mediator.Verify(m => m.Send(It.Is<Application.Commands.SendNotificationCommand>(c => c.Type == NotificationTypes.PackageAssignmentCancelled), It.IsAny<CancellationToken>()), Times.Once);
        Assert.All(results, r => Assert.DoesNotContain("ERR:", r));
    }

    [Fact]
    public async Task CancelRacingTheSweeper_NeverLeavesBothOutcomes()
    {
        var (db0, prId, clientId, _) = await Generated();
        using (db0)
        {
            // Window just ended → sweeper wants to record agreement, cancel (stale view) wants to cancel.
            var c = await db0.Contracts.FirstAsync();
            c.CancellationWindowEndsAt = DateTime.UtcNow.AddMilliseconds(-5); await db0.SaveChangesAsync();
            var swept = await Svc(db0).FinalizeExpiredWindowsAsync();
            Assert.Equal(1, swept);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Svc(db0).CancelByClientAsync(prId, clientId));
            var final = await db0.Contracts.AsNoTracking().FirstAsync();
            Assert.NotNull(final.AcceptedByNonCancellationAt);
            Assert.Null(final.CancelledAt);
            Assert.Equal(PackageRequestStatuses.Confirmed, (await db0.PackageRequests.AsNoTracking().FirstAsync()).Status);
            Assert.Equal(AssignmentStatuses.Accepted, (await db0.Assignments.AsNoTracking().FirstAsync()).Status);
        }
    }

    [Fact]
    public async Task Cancel_StopsRecurringSubscription_AndBillingRunSkipsIt_AndAdminAlertNamesEverything()
    {
        var ops = new Mock<IOpsAlertService>();
        var (db, prId, clientId, _) = await Generated();
        using (db)
        {
            db.PackageSubscriptions.Add(new PackageSubscription
            {
                Id = MongoDB.Bson.ObjectId.GenerateNewId(), PackageRequestId = prId, ClientId = clientId, RecurringAmount = 100000m,
                Email = "c@x.com", Status = SubscriptionStatus.Active, AutoRenew = true, NextChargeDate = DateTime.UtcNow.AddDays(-1),
                FlutterwavePaymentToken = "tok", // pragma: allowlist-secret
            });
            await db.SaveChangesAsync();
            var subSvc = new PackageSubscriptionService(db, null!, Mock.Of<IMediator>(), Mock.Of<ILogger<PackageSubscriptionService>>());
            Assert.Single(await subSvc.GetPackageSubscriptionsDueForBillingAsync());   // due before cancel

            await Svc(db, ops: ops).CancelByClientAsync(prId, clientId);

            var sub = await db.PackageSubscriptions.AsNoTracking().FirstAsync();
            Assert.Equal(SubscriptionStatus.Cancelled, sub.Status);
            Assert.False(sub.AutoRenew);
            Assert.Null(sub.NextChargeDate);
            Assert.Empty(await subSvc.GetPackageSubscriptionsDueForBillingAsync());     // billing run skips it
            ops.Verify(o => o.NotifyAdminsAsync(NotificationTypes.PackageRequestCancelledByClient, It.IsAny<string>(),
                It.Is<string>(m => m.Contains(prId) && m.Contains(clientId) && m.Contains("₦") && m.Contains("Recurring subscription") && m.Contains("No refund has been issued automatically")),
                prId, false), Times.Once);
        }
    }

    [Fact]
    public async Task BillingRun_SkipsActiveSubscription_WhoseRequestIsCancelled_EvenIfCancelFlowWasInterrupted()
    {
        var (db, prId, clientId, _) = await Generated();
        using (db)
        {
            db.PackageSubscriptions.Add(new PackageSubscription
            {
                Id = MongoDB.Bson.ObjectId.GenerateNewId(), PackageRequestId = prId, ClientId = clientId, RecurringAmount = 1m, Email = "c@x.com",
                Status = SubscriptionStatus.Active, AutoRenew = true, NextChargeDate = DateTime.UtcNow.AddDays(-1), FlutterwavePaymentToken = "tok", // pragma: allowlist-secret
            });
            (await db.PackageRequests.FirstAsync()).Status = PackageRequestStatuses.Cancelled;
            await db.SaveChangesAsync();
            var subSvc = new PackageSubscriptionService(db, null!, Mock.Of<IMediator>(), Mock.Of<ILogger<PackageSubscriptionService>>());
            Assert.Empty(await subSvc.GetPackageSubscriptionsDueForBillingAsync());
        }
    }

    // ───────────── Consistent agency wording ─────────────

    [Fact]
    public async Task GeneratedContract_HtmlAndPdfTemplate_HaveNoMarketplaceOrIndependentContractorWording()
    {
        var (db, prId, _, _) = await Generated();
        using (db)
        {
            var dto = (await Svc(db).GetByPackageRequestAsync(prId))!;
            foreach (var banned in new[] { "marketplace", "independent contractor", "independent caregivers", "solely responsible", "market participants",
                                           "Facilitated through", "background check", "not liable for the actions" })
                Assert.DoesNotContain(banned, dto.GeneratedTermsHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("CarePro is a home-care service provider", dto.GeneratedTermsHtml);
            Assert.Contains("onboarding and vetting process", dto.GeneratedTermsHtml);
        }
    }
}

/// <summary>Writes the real rendered HTML contract, PDF and client email body to EVIDENCE_DIR (skipped when unset).</summary>
public class ContractEvidenceDump
{
    [Fact]
    public async Task DumpRenderedArtifacts()
    {
        var dir = Environment.GetEnvironmentVariable("EVIDENCE_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);

        var db = Phase6ContractTests.CreateDb(Phase6ContractTests.NewDbName());
        using (db)
        {
            var (client, pkg, cg) = Phase6ContractTests.Seed(db);
            var pr = Phase6ContractTests.AddConfirmedRequest(db, client, pkg, cg);
            var svc = new PackageContractService(db, new ContractTemplateService(), new ContractPdfService(), Mock.Of<IMediator>(), Mock.Of<IEmailService>(),
                Mock.Of<IOpsAlertService>(), Mock.Of<ILogger<PackageContractService>>());
            var dto = await svc.GenerateForConfirmedRequestAsync(pr.Id.ToString());
            await File.WriteAllTextAsync(Path.Combine(dir, "contract.html"), dto.GeneratedTermsHtml);
            await File.WriteAllBytesAsync(Path.Combine(dir, "contract.pdf"), await svc.GeneratePdfAsync(dto.Id));

            var mail = new Infrastructure.Services.EmailService(
                Options.Create(new MailSettings { FromEmail = "noreply@x.com", FromName = "CarePro", SmtpServer = "x", SmtpPort = 1, AppPassword = "x" }), // pragma: allowlist-secret
                db, Mock.Of<Application.Interfaces.Authentication.ITokenHandler>(), Mock.Of<ILogger<Infrastructure.Services.EmailService>>(),
                Mock.Of<Application.Interfaces.Email.IEmailNotificationTrackingService>());
            var msg = mail.BuildContractPdfEmailMessage("client@x.com", "Amara", dto.Id, "Post-Partum Care (Premium) Package", new byte[] { 1, 2, 3 }, dto.CancellationAcknowledgement);
            await File.WriteAllTextAsync(Path.Combine(dir, "client-email.html"), msg.HtmlBody ?? "");
            var cgMsg = mail.BuildContractPdfEmailMessage("cg@x.com", "Ngozi", dto.Id, "Post-Partum Care (Premium) Package", new byte[] { 1, 2, 3 }, null);
            await File.WriteAllTextAsync(Path.Combine(dir, "caregiver-email.html"), cgMsg.HtmlBody ?? "");
            await File.WriteAllTextAsync(Path.Combine(dir, "dates.txt"), $"sent={dto.ContractSentAt:O}\nwindowEnds={dto.CancellationWindowEndsAt:O}\nstart={dto.ContractStartDate:O}\n");
        }
    }
}
