using System;
using System.Collections.Generic;
using Application.DTOs;
using Application.Interfaces.Content;
using Infrastructure.Content.Data;
using Infrastructure.Content.Services;
using Infrastructure.Content.Services.Authentication;
using CarePro_Api.Controllers.Content;
using Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using MongoDB.Bson;
using MongoDB.EntityFrameworkCore.Extensions;
using Xunit;

namespace CommitmentGate.Tests;

public class EmailCompliancePreferenceTests
{
    [Fact]
    public async Task Unsubscribe_Link_Immediately_Blocks_Lifecycle_But_Allows_Transactional()
    {
        using var db = CreateDb();

        var userObjectId = ObjectId.GenerateNewId();
        var userId = userObjectId.ToString();
        var email = "optout-user@example.com";

        db.AppUsers.Add(new AppUser
        {
            Id = ObjectId.GenerateNewId(),
            AppUserId = userObjectId,
            Email = email,
            FirstName = "OptOut",
            LastName = "User",
            Role = "Client",
            Password = "hashed",
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow,
            EmailConfirmed = true
        });

        db.ClientPreferences.Add(new ClientPreference
        {
            Id = ObjectId.GenerateNewId(),
            ClientId = userId,
            Data = new List<string>(),
            NotificationPreferences = new NotificationPreferences
            {
                EmailNotifications = true,
                SmsNotifications = true,
                MarketingEmails = true,
                OrderUpdates = true,
                ServiceUpdates = true,
                Promotions = true
            },
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        var loggerTracking = new Mock<ILogger<EmailNotificationTrackingService>>();
        var trackingService = new EmailNotificationTrackingService(db, loggerTracking.Object);

        // Before unsubscribe: lifecycle should be allowed.
        var beforeLifecycle = await trackingService.ShouldSendEmailToUserAsync(userId, NotificationTypes.NewGig);
        Assert.True(beforeLifecycle);

        // Build a signed unsubscribe token for this recipient.
        var tokenHandler = CreateTokenHandler();
        var token = tokenHandler.GenerateEmailUnsubscribeToken(userId, email, "lifecycle_engagement");

        var controller = new ClientPreferencesController(
            Mock.Of<IClientPreferenceService>(),
            Mock.Of<IClientService>(),
            tokenHandler,
            db,
            Mock.Of<ILogger<ClientPreferencesController>>());

        var unsubscribeResult = await controller.Unsubscribe(token);
        var contentResult = Assert.IsType<ContentResult>(unsubscribeResult);
        Assert.Equal("text/html", contentResult.ContentType);

        // After unsubscribe: lifecycle/engagement should be blocked.
        var afterLifecycle = await trackingService.ShouldSendEmailToUserAsync(userId, NotificationTypes.NewGig);
        Assert.False(afterLifecycle);

        // Transactional notification should still be allowed.
        var transactionalStillAllowed = await trackingService.ShouldSendEmailToUserAsync(userId, NotificationTypes.PaymentConfirmed);
        Assert.True(transactionalStillAllowed);

        var storedPreference = await db.ClientPreferences.FirstOrDefaultAsync(x => x.ClientId == userId);
        Assert.NotNull(storedPreference);
        Assert.NotNull(storedPreference!.NotificationPreferences);
        Assert.False(storedPreference.NotificationPreferences!.MarketingEmails);
        Assert.False(storedPreference.NotificationPreferences!.Promotions);

        Console.WriteLine($"EMAIL_COMPLIANCE_EVIDENCE beforeLifecycle={beforeLifecycle} afterLifecycle={afterLifecycle} transactionalStillAllowed={transactionalStillAllowed}");
    }

    private static CareProDbContext CreateDb()
    {
        var dbName = $"email_compliance_{Guid.NewGuid():N}";

        var options = new DbContextOptionsBuilder<CareProDbContext>()
            .UseMongoDB("mongodb://127.0.0.1:27018", dbName)
            .Options;

        return new CareProDbContext(options);
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
