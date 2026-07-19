using System;
using System.Collections.Generic;
using Application.DTOs;
using Application.Interfaces;
using Application.Interfaces.Authentication;
using Application.Interfaces.Common;
using Application.Interfaces.Content;
using Application.Interfaces.Email;
using CarePro_Api.Controllers.Content;
using CloudinaryDotNet;
using Domain.Entities;
using Infrastructure.Content.Data;
using Infrastructure.Content.Services;
using Infrastructure.Content.Services.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MongoDB.Bson;
using MongoDB.EntityFrameworkCore.Extensions;
using Xunit;

namespace CommitmentGate.Tests;

public class SignupConsentAndCaregiverPreferencesTests
{
    [Fact]
    public async Task Signup_MarketingConsent_Persists_For_Client_And_Caregiver()
    {
        using var db = CreateDb();

        var config = CreateTestConfiguration();
        var tokenHandler = new TokenHandler(config);
        var cloudinaryService = CreateCloudinaryService();

        var locationService = new Mock<ILocationService>();
        locationService
            .Setup(x => x.SetUserLocationAsync(It.IsAny<SetLocationRequest>()))
            .ReturnsAsync(new LocationDTO());

        var originValidation = new Mock<IOriginValidationService>();
        originValidation
            .Setup(x => x.IsFrontendOrigin(It.IsAny<string>()))
            .Returns(true);

        var googleSheets = new Mock<IGoogleSheetsService>();
        googleSheets
            .Setup(x => x.AppendSignupDataAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var emailService = Mock.Of<IEmailService>();

        var clientService = new ClientService(
            db,
            cloudinaryService,
            tokenHandler,
            emailService,
            config,
            originValidation.Object,
            locationService.Object,
            googleSheets.Object,
            Mock.Of<ILogger<ClientService>>());

        var caregiverService = new CareGiverService(
            db,
            cloudinaryService,
            tokenHandler,
            emailService,
            config,
            locationService.Object,
            originValidation.Object,
            googleSheets.Object,
            Mock.Of<ILogger<CareGiverService>>());

        var clientHttp = new DefaultHttpContext();
        clientHttp.Request.Headers["Origin"] = "https://web.oncarepro.test";

        var clientController = new ClientsController(
            clientService,
            Mock.Of<ILogger<ClientsController>>(),
            new HttpContextAccessor { HttpContext = clientHttp },
            Mock.Of<IGoogleAuthService>(),
            Mock.Of<IUserDeletionService>());

        var clientResult = await clientController.AddClientUserAsync(new AddClientUserRequest
        {
            FirstName = "Client",
            LastName = "Consent",
            Email = "client-consent@example.com",
            Password = "Password123",
            HomeAddress = "123 Main Street",
            MarketingConsent = true
        });

        var clientOk = Assert.IsType<OkObjectResult>(clientResult);
        var createdClient = Assert.IsType<ClientDTO>(clientOk.Value);
        Assert.False(string.IsNullOrWhiteSpace(createdClient.Id));

        var clientPreference = await db.ClientPreferences.FirstOrDefaultAsync(x => x.ClientId == createdClient.Id);
        Assert.NotNull(clientPreference);
        Assert.NotNull(clientPreference!.NotificationPreferences);
        Assert.True(clientPreference.NotificationPreferences!.MarketingEmails);
        Assert.True(clientPreference.NotificationPreferences.Promotions);

        var caregiverHttp = new DefaultHttpContext();
        caregiverHttp.Request.Headers["Origin"] = "https://web.oncarepro.test";

        var caregiverController = new CareGiversController(
            db,
            caregiverService,
            Mock.Of<ILogger<CareGiversController>>(),
            new HttpContextAccessor { HttpContext = caregiverHttp },
            Mock.Of<IGoogleAuthService>(),
            Mock.Of<IUserDeletionService>());

        var caregiverResult = await caregiverController.AddCaregiverUserAsync(new AddCaregiverRequest
        {
            FirstName = "Care",
            LastName = "Giver",
            Email = "caregiver-consent@example.com",
            Password = "Password123",
            Role = "Caregiver",
            HomeAddress = "456 Care Street"
            // MarketingConsent intentionally omitted to verify default opt-out.
        });

        var caregiverOk = Assert.IsType<OkObjectResult>(caregiverResult);
        var createdCaregiver = Assert.IsType<CaregiverDTO>(caregiverOk.Value);
        Assert.False(string.IsNullOrWhiteSpace(createdCaregiver.Id));

        var caregiverPreference = await db.CaregiverPreferences.FirstOrDefaultAsync(x => x.CaregiverId == createdCaregiver.Id);
        Assert.NotNull(caregiverPreference);
        Assert.NotNull(caregiverPreference!.NotificationPreferences);
        Assert.False(caregiverPreference.NotificationPreferences!.MarketingEmails);
        Assert.False(caregiverPreference.NotificationPreferences.Promotions);
        Assert.False(caregiverPreference.NotificationPreferences.NewGig);
        Assert.False(caregiverPreference.NotificationPreferences.CareRequestUpdates);

        Console.WriteLine("SIGNUP_CONSENT_EVIDENCE_BEGIN");
        Console.WriteLine($"clientMarketingEmails={clientPreference.NotificationPreferences.MarketingEmails} clientPromotions={clientPreference.NotificationPreferences.Promotions}");
        Console.WriteLine($"caregiverMarketingEmails={caregiverPreference.NotificationPreferences.MarketingEmails} caregiverPromotions={caregiverPreference.NotificationPreferences.Promotions} caregiverNewGig={caregiverPreference.NotificationPreferences.NewGig} caregiverCareRequestUpdates={caregiverPreference.NotificationPreferences.CareRequestUpdates}");
        Console.WriteLine("SIGNUP_CONSENT_EVIDENCE_END");
    }

    [Fact]
    public async Task CaregiverPreferences_Put_Get_ReadBack_And_Enforcement_Works()
    {
        using var db = CreateDb();

        var caregiverId = ObjectId.GenerateNewId();
        var caregiverIdText = caregiverId.ToString();

        db.CareGivers.Add(new Caregiver
        {
            Id = caregiverId,
            FirstName = "Gate",
            LastName = "Tester",
            Email = "gate-tester@example.com",
            Password = "hashed",
            Role = "Caregiver",
            Status = true,
            IsAvailable = true,
            IsDeleted = false,
            HomeAddress = "789 Preference Road",
            CreatedAt = DateTime.UtcNow
        });

        db.AppUsers.Add(new AppUser
        {
            Id = ObjectId.GenerateNewId(),
            AppUserId = caregiverId,
            Email = "gate-tester@example.com",
            FirstName = "Gate",
            LastName = "Tester",
            Role = "Caregiver",
            Password = "hashed",
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow,
            EmailConfirmed = true
        });

        await db.SaveChangesAsync();

        var preferenceService = new CaregiverPreferenceService(db);
        var controller = new CaregiverPreferencesController(
            preferenceService,
            Mock.Of<ILogger<CaregiverPreferencesController>>());

        var updateResponse = await controller.UpdateNotificationPreferencesAsync(
            caregiverIdText,
            new UpdateCaregiverNotificationPreferencesRequest
            {
                EmailNotifications = true,
                SmsNotifications = true,
                MarketingEmails = true,
                Promotions = true,
                NewGig = true,
                CareRequestUpdates = false
            });

        var updateOk = Assert.IsType<OkObjectResult>(updateResponse);
        var updateBody = Assert.IsType<CaregiverNotificationPreferencesResponse>(updateOk.Value);
        Assert.True(updateBody.Success);

        var readResponse = await controller.GetNotificationPreferencesAsync(caregiverIdText);
        var readOk = Assert.IsType<OkObjectResult>(readResponse);
        var readBody = Assert.IsType<CaregiverNotificationPreferencesResponse>(readOk.Value);
        Assert.True(readBody.Success);
        Assert.NotNull(readBody.Data);
        Assert.True(readBody.Data!.MarketingEmails);
        Assert.True(readBody.Data.NewGig);
        Assert.False(readBody.Data.CareRequestUpdates);

        var trackingService = new EmailNotificationTrackingService(db, Mock.Of<ILogger<EmailNotificationTrackingService>>());

        var newGigAllowed = await trackingService.ShouldSendEmailToUserAsync(caregiverIdText, NotificationTypes.NewGig);
        var careRequestBlocked = await trackingService.ShouldSendEmailToUserAsync(caregiverIdText, NotificationTypes.CareRequestNewMatch);
        var transactionalStillAllowed = await trackingService.ShouldSendEmailToUserAsync(caregiverIdText, NotificationTypes.PaymentConfirmed);

        Assert.True(newGigAllowed);
        Assert.False(careRequestBlocked);
        Assert.True(transactionalStillAllowed);

        await controller.UpdateNotificationPreferencesAsync(
            caregiverIdText,
            new UpdateCaregiverNotificationPreferencesRequest
            {
                EmailNotifications = true,
                SmsNotifications = true,
                MarketingEmails = true,
                Promotions = true,
                NewGig = false,
                CareRequestUpdates = true
            });

        var newGigBlockedAfterOptOut = await trackingService.ShouldSendEmailToUserAsync(caregiverIdText, NotificationTypes.NewGig);
        Assert.False(newGigBlockedAfterOptOut);

        Console.WriteLine("CAREGIVER_PREF_GATE_EVIDENCE_BEGIN");
        Console.WriteLine($"newGigAllowedBeforeOptOut={newGigAllowed} careRequestBlocked={careRequestBlocked} transactionalStillAllowed={transactionalStillAllowed}");
        Console.WriteLine($"newGigBlockedAfterOptOut={newGigBlockedAfterOptOut}");
        Console.WriteLine("CAREGIVER_PREF_GATE_EVIDENCE_END");
    }

    private static CareProDbContext CreateDb()
    {
        var dbName = $"signup_caregiver_pref_{Guid.NewGuid():N}";

        var options = new DbContextOptionsBuilder<CareProDbContext>()
            .UseMongoDB("mongodb://127.0.0.1:27018", dbName)
            .Options;

        return new CareProDbContext(options);
    }

    private static IConfiguration CreateTestConfiguration()
    {
        var settings = new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["Development:AutoConfirmEmail"] = "true",
            ["JwtSettings:Secret"] = "12345678901234567890123456789012",
            ["JwtSettings:Issuer"] = "carepro-tests",
            ["JwtSettings:Audience"] = "carepro-tests-audience"
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
    }

    private static CloudinaryService CreateCloudinaryService()
    {
        var cloudinary = new Cloudinary(new Account("default", "default", "default"));
        var cloudinaryOptions = Options.Create(new CloudinarySettings
        {
            CloudName = "default",
            ApiKey = "default",
            ApiSecret = "default"
        });

        return new CloudinaryService(cloudinary, cloudinaryOptions);
    }
}
