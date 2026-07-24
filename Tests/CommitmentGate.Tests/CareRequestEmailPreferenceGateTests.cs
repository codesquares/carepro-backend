using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Application.DTOs;
using Application.Interfaces.Email;
using Domain.Entities;
using Domain.Settings;
using Infrastructure.Content.Data;
using Infrastructure.Content.Services;
using Infrastructure.Content.Services.Authentication;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MongoDB.Bson;
using MongoDB.EntityFrameworkCore.Extensions;
using Xunit;

namespace CommitmentGate.Tests;

/// <summary>
/// Covers the three CareRequest email call sites (CareRequestMatchingService's match email,
/// CareRequestResponseService's responder email, and its inline hire email) that previously
/// sent unconditionally via SendGenericNotificationEmailAsync(preferenceGated: true) even
/// though that parameter never actually checked any preference. These tests exercise the real
/// gate now wired into SendGenericNotificationEmailAsync's gateUserId/gateNotificationType
/// parameters, using a real Mongo-backed EmailNotificationTrackingService and real preference
/// records — the same "real, not mocked" DB pattern used throughout this project. SMTP is
/// pointed at an unbound loopback port so a blocked send returns silently (no connection
/// attempted) while an allowed send throws (a real connection attempt was made and refused),
/// which is how we observe "did it try to send" without a real mail server.
/// </summary>
public class CareRequestEmailPreferenceGateTests
{
    [Fact]
    public async Task Caregiver_CareRequestUpdates_Gates_NewMatch_And_Hired_Email()
    {
        using var db = CreateDb();

        var caregiverId = ObjectId.GenerateNewId();
        var caregiverIdText = caregiverId.ToString();

        db.CareGivers.Add(new Caregiver
        {
            Id = caregiverId,
            FirstName = "Gate",
            LastName = "Caregiver",
            Email = "gate-caregiver@example.com",
            Password = "hashed",
            Role = "Caregiver",
            Status = true,
            IsAvailable = true,
            IsDeleted = false,
            HomeAddress = "1 Gate Road",
            CreatedAt = DateTime.UtcNow
        });

        db.AppUsers.Add(new AppUser
        {
            Id = ObjectId.GenerateNewId(),
            AppUserId = caregiverId,
            Email = "gate-caregiver@example.com",
            FirstName = "Gate",
            LastName = "Caregiver",
            Role = "Caregiver",
            Password = "hashed",
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow,
            EmailConfirmed = true
        });

        db.CaregiverPreferences.Add(new CaregiverPreference
        {
            Id = ObjectId.GenerateNewId(),
            CaregiverId = caregiverIdText,
            Data = new List<string>(),
            NotificationPreferences = new CaregiverNotificationPreferences
            {
                EmailNotifications = true,
                SmsNotifications = true,
                MarketingEmails = true,
                NewGig = true,
                CareRequestUpdates = false
            },
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        var emailService = CreateEmailService(db);

        // CareRequestUpdates is OFF: both types must be blocked before any send is attempted.
        var newMatchBlocked = await TryAttemptSend(emailService, "gate-caregiver@example.com", "Gate",
            caregiverIdText, NotificationTypes.CareRequestNewMatch);
        var hiredBlocked = await TryAttemptSend(emailService, "gate-caregiver@example.com", "Gate",
            caregiverIdText, NotificationTypes.CareRequestHired);

        Assert.False(newMatchBlocked.AttemptedSend);
        Assert.False(hiredBlocked.AttemptedSend);

        // Flip CareRequestUpdates ON: both types must now be attempted (real connection refused
        // by the unbound loopback port proves the gate let the send through).
        var preference = await db.CaregiverPreferences.FirstAsync(p => p.CaregiverId == caregiverIdText);
        preference.NotificationPreferences!.CareRequestUpdates = true;
        db.CaregiverPreferences.Update(preference);
        await db.SaveChangesAsync();

        var newMatchAllowed = await TryAttemptSend(emailService, "gate-caregiver@example.com", "Gate",
            caregiverIdText, NotificationTypes.CareRequestNewMatch);
        var hiredAllowed = await TryAttemptSend(emailService, "gate-caregiver@example.com", "Gate",
            caregiverIdText, NotificationTypes.CareRequestHired);

        Assert.True(newMatchAllowed.AttemptedSend);
        Assert.True(hiredAllowed.AttemptedSend);

        Console.WriteLine("CARE_REQUEST_CAREGIVER_GATE_EVIDENCE_BEGIN");
        Console.WriteLine($"newMatchAttemptedWhenOff={newMatchBlocked.AttemptedSend} hiredAttemptedWhenOff={hiredBlocked.AttemptedSend}");
        Console.WriteLine($"newMatchAttemptedWhenOn={newMatchAllowed.AttemptedSend} hiredAttemptedWhenOn={hiredAllowed.AttemptedSend}");
        Console.WriteLine("CARE_REQUEST_CAREGIVER_GATE_EVIDENCE_END");
    }

    [Fact]
    public async Task Client_MarketingConsent_Gates_NewResponder_Email()
    {
        using var db = CreateDb();

        var clientId = ObjectId.GenerateNewId();
        var clientIdText = clientId.ToString();

        db.AppUsers.Add(new AppUser
        {
            Id = ObjectId.GenerateNewId(),
            AppUserId = clientId,
            Email = "gate-client@example.com",
            FirstName = "Gate",
            LastName = "Client",
            Role = "Client",
            Password = "hashed",
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow,
            EmailConfirmed = true
        });

        db.ClientPreferences.Add(new ClientPreference
        {
            Id = ObjectId.GenerateNewId(),
            ClientId = clientIdText,
            Data = new List<string>(),
            NotificationPreferences = new NotificationPreferences
            {
                EmailNotifications = true,
                SmsNotifications = true,
                MarketingEmails = false,
                OrderUpdates = true,
                ServiceUpdates = true,
                Promotions = false
            },
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        var emailService = CreateEmailService(db);

        // care_request_new_responder notifies the CLIENT, not a caregiver — CareRequestUpdates
        // does not apply to this recipient at all; it falls through to general marketing consent.
        var blocked = await TryAttemptSend(emailService, "gate-client@example.com", "Gate",
            clientIdText, NotificationTypes.CareRequestNewResponder);
        Assert.False(blocked.AttemptedSend);

        var preference = await db.ClientPreferences.FirstAsync(p => p.ClientId == clientIdText);
        preference.NotificationPreferences!.MarketingEmails = true;
        db.ClientPreferences.Update(preference);
        await db.SaveChangesAsync();

        var allowed = await TryAttemptSend(emailService, "gate-client@example.com", "Gate",
            clientIdText, NotificationTypes.CareRequestNewResponder);
        Assert.True(allowed.AttemptedSend);

        Console.WriteLine("CARE_REQUEST_CLIENT_GATE_EVIDENCE_BEGIN");
        Console.WriteLine($"newResponderAttemptedWhenMarketingOff={blocked.AttemptedSend} newResponderAttemptedWhenMarketingOn={allowed.AttemptedSend}");
        Console.WriteLine("CARE_REQUEST_CLIENT_GATE_EVIDENCE_END");
    }

    /// <summary>
    /// The DB-driven tests above prove real preference records end up blocking/allowing a real
    /// send attempt, but that alone doesn't prove the branch is actually driven by
    /// ShouldSendEmailToUserAsync's return value specifically — an unrelated early return or a
    /// silently-swallowed exception upstream could produce the same "no attempt" observation.
    /// These tests close that gap with a Moq spy: the tracking service's return value is
    /// controlled directly (not via DB state), and we verify both that it was called with the
    /// exact recipient/type the send was made for, and that its return value — not something
    /// else — is what determines whether the send proceeds.
    /// </summary>
    [Fact]
    public async Task Gate_Calls_ShouldSendEmailToUserAsync_With_Expected_Args_And_Honors_False()
    {
        using var db = CreateDb();

        var trackingMock = new Mock<IEmailNotificationTrackingService>(MockBehavior.Strict);
        trackingMock
            .Setup(t => t.ShouldSendEmailToUserAsync("spy-user-id", NotificationTypes.CareRequestNewMatch))
            .ReturnsAsync(false);

        var emailService = CreateEmailServiceWithTracking(db, trackingMock.Object);

        var result = await TryAttemptSend(emailService, "spy-target@example.com", "Spy",
            "spy-user-id", NotificationTypes.CareRequestNewMatch);

        Assert.False(result.AttemptedSend);
        trackingMock.Verify(
            t => t.ShouldSendEmailToUserAsync("spy-user-id", NotificationTypes.CareRequestNewMatch),
            Times.Once);

        Console.WriteLine("CARE_REQUEST_GATE_SPY_EVIDENCE_BEGIN");
        Console.WriteLine("ShouldSendEmailToUserAsync(spy-user-id, care_request_new_match) returned false, called exactly once, send did not proceed");
        Console.WriteLine("CARE_REQUEST_GATE_SPY_EVIDENCE_END");
    }

    [Fact]
    public async Task Gate_Calls_ShouldSendEmailToUserAsync_With_Expected_Args_And_Honors_True()
    {
        using var db = CreateDb();

        var trackingMock = new Mock<IEmailNotificationTrackingService>(MockBehavior.Strict);
        trackingMock
            .Setup(t => t.ShouldSendEmailToUserAsync("spy-user-id", NotificationTypes.CareRequestHired))
            .ReturnsAsync(true);

        var emailService = CreateEmailServiceWithTracking(db, trackingMock.Object);

        var result = await TryAttemptSend(emailService, "spy-target@example.com", "Spy",
            "spy-user-id", NotificationTypes.CareRequestHired);

        Assert.True(result.AttemptedSend);
        trackingMock.Verify(
            t => t.ShouldSendEmailToUserAsync("spy-user-id", NotificationTypes.CareRequestHired),
            Times.Once);

        Console.WriteLine("CARE_REQUEST_GATE_SPY_EVIDENCE_BEGIN");
        Console.WriteLine("ShouldSendEmailToUserAsync(spy-user-id, care_request_hired) returned true, called exactly once, send proceeded to a real connection attempt");
        Console.WriteLine("CARE_REQUEST_GATE_SPY_EVIDENCE_END");
    }

    private static async Task<(bool AttemptedSend, Exception? Error)> TryAttemptSend(
        EmailService emailService, string toEmail, string firstName, string gateUserId, string gateNotificationType)
    {
        try
        {
            await emailService.SendGenericNotificationEmailAsync(
                toEmail, firstName, "Gate Test Subject", "Gate test content",
                includeUnsubscribeHeader: true,
                gateUserId: gateUserId,
                gateNotificationType: gateNotificationType);

            // No exception: either the send was skipped by the gate, or it somehow succeeded
            // against the unbound port (impossible), so "no exception" means "blocked".
            return (false, null);
        }
        catch (Exception ex)
        {
            // The gate let it through to a real SMTP connection attempt against an unbound
            // loopback port, which fails immediately — that failure is the send attempt itself.
            return (true, ex);
        }
    }

    private static EmailService CreateEmailService(CareProDbContext db)
    {
        var options = Options.Create(new MailSettings
        {
            FromName = "CarePro",
            FromEmail = "no-reply@example.com",
            SmtpServer = "127.0.0.1",
            SmtpPort = 1, // unbound loopback port: real connection attempts are refused instantly
            AppPassword = "not-used-in-this-test"
        });

        var tokenHandler = CreateTokenHandler();
        var trackingLogger = Mock.Of<ILogger<EmailNotificationTrackingService>>();
        var trackingService = new EmailNotificationTrackingService(db, trackingLogger);
        var emailLogger = Mock.Of<ILogger<EmailService>>();

        return new EmailService(options, db, tokenHandler, emailLogger, trackingService);
    }

    private static EmailService CreateEmailServiceWithTracking(CareProDbContext db, IEmailNotificationTrackingService trackingService)
    {
        var options = Options.Create(new MailSettings
        {
            FromName = "CarePro",
            FromEmail = "no-reply@example.com",
            SmtpServer = "127.0.0.1",
            SmtpPort = 1, // unbound loopback port: real connection attempts are refused instantly
            AppPassword = "not-used-in-this-test"
        });

        var tokenHandler = CreateTokenHandler();
        var emailLogger = Mock.Of<ILogger<EmailService>>();

        return new EmailService(options, db, tokenHandler, emailLogger, trackingService);
    }

    private static CareProDbContext CreateDb()
    {
        var dbName = $"care_request_email_gate_{Guid.NewGuid():N}";

        var dbOptions = new DbContextOptionsBuilder<CareProDbContext>()
            .UseMongoDB("mongodb://127.0.0.1:27018", dbName)
            .Options;

        return new CareProDbContext(dbOptions);
    }

    private static TokenHandler CreateTokenHandler()
    {
        var settings = new Dictionary<string, string?>
        {
            ["JwtSettings:Secret"] = "12345678901234567890123456789012",
            ["JwtSettings:Issuer"] = "carepro-tests",
            ["JwtSettings:Audience"] = "carepro-tests-audience"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        return new TokenHandler(configuration);
    }
}
