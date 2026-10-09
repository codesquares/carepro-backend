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
            await Assert.ThrowsAsync<InvalidOperationException>(() => Svc(db).CancelByClientAsync(prId, clientId)); // already cancelled
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
}
