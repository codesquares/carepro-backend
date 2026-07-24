using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Application.Interfaces.Authentication;
using Domain.Entities;
using Domain.Settings;
using Infrastructure.Content.Data;
using Infrastructure.Content.Services;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Moq;
using MongoDB.Bson;
using MongoDB.EntityFrameworkCore.Extensions;
using Xunit;

namespace CommitmentGate.Tests;

public class EmailUnsubscribeHeaderTests
{
    [Fact]
    public async Task ApplyUnsubscribeMetadata_Adds_ListUnsubscribe_And_OneClickPost_Headers()
    {
        using var db = CreateDb();

        var appUserId = ObjectId.GenerateNewId();
        db.AppUsers.Add(new AppUser
        {
            Id = ObjectId.GenerateNewId(),
            AppUserId = appUserId,
            Email = "header-proof@example.com",
            FirstName = "Header",
            LastName = "Proof",
            Role = "Client",
            Password = "hashed",
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow,
            EmailConfirmed = true
        });
        await db.SaveChangesAsync();

        var tokenHandler = CreateTokenHandler();
        var options = Options.Create(new MailSettings
        {
            FromName = "CarePro",
            FromEmail = "no-reply@example.com",
            SmtpServer = "localhost",
            SmtpPort = 1025,
            AppPassword = "not-used-in-this-test"
        });

        var logger = Mock.Of<ILogger<EmailService>>();
        var trackingService = new EmailNotificationTrackingService(db, Mock.Of<ILogger<EmailNotificationTrackingService>>());
        var service = new EmailService(options, db, tokenHandler, logger, trackingService);

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("CarePro", "no-reply@example.com"));
        message.To.Add(MailboxAddress.Parse("header-proof@example.com"));
        message.Subject = "Lifecycle Message";
        message.Body = new BodyBuilder
        {
            HtmlBody = "<p>Hello lifecycle user</p>"
        }.ToMessageBody();

        var method = typeof(EmailService).GetMethod(
            "ApplyUnsubscribeMetadataAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var task = (Task?)method!.Invoke(service, new object[]
        {
            message,
            "header-proof@example.com",
            "lifecycle_engagement"
        });

        Assert.NotNull(task);
        await task!;

        var listUnsubscribe = message.Headers["List-Unsubscribe"];
        var listUnsubscribePost = message.Headers["List-Unsubscribe-Post"];

        Assert.False(string.IsNullOrWhiteSpace(listUnsubscribe));
        Assert.False(string.IsNullOrWhiteSpace(listUnsubscribePost));
        Assert.Equal("List-Unsubscribe=One-Click", listUnsubscribePost);
        Assert.Contains("/api/ClientPreferences/unsubscribe?token=", listUnsubscribe);

        Console.WriteLine("HEADER_EVIDENCE_BEGIN");
        Console.WriteLine($"List-Unsubscribe: {listUnsubscribe}");
        Console.WriteLine($"List-Unsubscribe-Post: {listUnsubscribePost}");
        Console.WriteLine("HEADER_EVIDENCE_END");
    }

    private static CareProDbContext CreateDb()
    {
        var dbName = $"email_header_proof_{Guid.NewGuid():N}";

        var options = new DbContextOptionsBuilder<CareProDbContext>()
            .UseMongoDB("mongodb://127.0.0.1:27018", dbName)
            .Options;

        return new CareProDbContext(options);
    }

    private static ITokenHandler CreateTokenHandler()
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

        return new Infrastructure.Content.Services.Authentication.TokenHandler(configuration);
    }
}
