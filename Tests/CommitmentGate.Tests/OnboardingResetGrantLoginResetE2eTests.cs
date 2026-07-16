using System.IdentityModel.Tokens.Jwt;
using Application.DTOs;
using Application.DTOs.Authentication;
using Domain.Entities;
using Domain.Settings;
using Infrastructure.Content.Data;
using Infrastructure.Content.Services;
using Infrastructure.Content.Services.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.EntityFrameworkCore.Extensions;
using Xunit;

namespace CommitmentGate.Tests;

public class OnboardingResetGrantLoginResetE2eTests
{
    [Fact]
    public async Task API_011_Grant_Login_Decode_Reset_GetState_EndToEnd()
    {
        using var db = CreateDb();

        var userId = ObjectId.GenerateNewId();
        var clientEmail = "qa.e2e.client@test.local";
        var plainPassword = "StrongPass123";
        var hashedPassword = BCrypt.Net.BCrypt.HashPassword(plainPassword);

        db.Clients.Add(new Client
        {
            Id = userId,
            FirstName = "QA",
            LastName = "Client",
            Email = clientEmail,
            Role = "Client",
            Password = hashedPassword,
            IsDeleted = false,
            Status = true,
            CreatedAt = DateTime.UtcNow
        });

        db.AppUsers.Add(new AppUser
        {
            Id = ObjectId.GenerateNewId(),
            AppUserId = userId,
            Email = clientEmail,
            FirstName = "QA",
            LastName = "Client",
            Role = "Client",
            Password = hashedPassword,
            EmailConfirmed = true,
            QaAccess = false,
            OnboardingResetAccess = false,
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        var adminService = new AdminUserService(db);
        var grantResponse = await adminService.GrantClientOnboardingResetAccessAsync(new GrantClientOnboardingResetAccessRequest
        {
            Email = clientEmail,
            QaAccess = true,
            OnboardingResetAccess = true
        });

        Assert.True(grantResponse.QaAccess);
        Assert.True(grantResponse.OnboardingResetAccess);

        var authService = new AuthService(
            db,
            new TokenHandler(BuildJwtConfig()),
            BuildJwtConfig());

        var loginResponse = await authService.AuthenticateUserLoginAsync(new LoginRequest
        {
            Email = clientEmail,
            Password = plainPassword
        });

        Assert.False(string.IsNullOrWhiteSpace(loginResponse.Token));

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(loginResponse.Token);
        var qaAccessClaim = jwt.Claims.FirstOrDefault(c => c.Type == "qa_access")?.Value;
        var resetAccessClaim = jwt.Claims.FirstOrDefault(c => c.Type == "onboarding_reset_access")?.Value;

        Assert.Equal("true", qaAccessClaim);
        Assert.Equal("true", resetAccessClaim);

        var onboardingService = new ClientOnboardingService(
            db,
            Options.Create(new ClientOnboardingSettings
            {
                CurrentContentVersion = "2026-07-15",
                SupportedContentVersions = ["2026-07-15"]
            }),
            LoggerFactory.Create(_ => { }).CreateLogger<ClientOnboardingService>());

        var started = await onboardingService.PatchWalkthroughAsync(userId.ToString(), new PatchClientOnboardingWalkthroughRequest
        {
            ContentVersion = "2026-07-15",
            Version = 0,
            Action = "start",
            StepKey = "step_1_dashboard_orientation"
        });

        Assert.Equal(WalkthroughStatuses.InProgress, started.State.Walkthrough.Status);

        var completed = await onboardingService.PatchWalkthroughAsync(userId.ToString(), new PatchClientOnboardingWalkthroughRequest
        {
            ContentVersion = "2026-07-15",
            Version = started.State.Walkthrough.Version,
            Action = "complete_sequence"
        });

        Assert.Equal(WalkthroughStatuses.Completed, completed.State.Walkthrough.Status);

        var reset = await onboardingService.ResetStateAsync(userId.ToString());

        Assert.Equal(WalkthroughStatuses.NotStarted, reset.Walkthrough.Status);
        Assert.Null(reset.Walkthrough.CurrentStep);
        Assert.Empty(reset.Walkthrough.StepStates);

        var finalState = await onboardingService.GetStateAsync(userId.ToString());
        Assert.Equal(WalkthroughStatuses.NotStarted, finalState.Walkthrough.Status);

        Console.WriteLine($"API-011_GRANT_RESPONSE {System.Text.Json.JsonSerializer.Serialize(grantResponse)}");
        Console.WriteLine($"API-011_LOGIN_ROLE {loginResponse.Role}");
        Console.WriteLine($"API-011_TOKEN_QA_ACCESS {qaAccessClaim}");
        Console.WriteLine($"API-011_TOKEN_ONBOARDING_RESET_ACCESS {resetAccessClaim}");
        Console.WriteLine($"API-011_RESET_STATE {System.Text.Json.JsonSerializer.Serialize(reset)}");
    }

    private static IConfiguration BuildJwtConfig()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "this_is_a_test_key_with_sufficient_length_12345",
                ["Jwt:Issuer"] = "carepro-tests",
                ["Jwt:Audience"] = "carepro-tests",
                ["Jwt:DurationInHours"] = "1",
                ["Jwt:RefreshTokenExpirationDays"] = "7"
            })
            .Build();
    }

    private static CareProDbContext CreateDb()
    {
        var databaseName = $"cpo_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<CareProDbContext>()
            .UseMongoDB("mongodb://127.0.0.1:27018", databaseName)
            .Options;

        return new TestCareProDbContext(options);
    }
}
