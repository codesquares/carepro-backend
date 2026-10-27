using System.IdentityModel.Tokens.Jwt;
using Application.DTOs;
using Application.DTOs.Authentication;
using Domain.Entities;
using Infrastructure.Content.Data;
using Infrastructure.Content.Services;
using Infrastructure.Content.Services.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using MongoDB.EntityFrameworkCore.Extensions;
using Xunit;

namespace CommitmentGate.Tests;

/// <summary>
/// Hardening 2: a newly created AdminUser must start PendingApproval and be
/// unable to log in until a SuperAdmin explicitly approves it - a second
/// line of defense behind the SuperAdmin-only creation endpoint. Runs the
/// real AdminUserService (creation + approval) and the real AuthService
/// login path against a real Mongo instance, not mocks.
/// </summary>
public class AdminApprovalGateTests
{
    [Fact]
    public async Task NewAdmin_StartsPendingApproval_CannotLoginUntilSuperAdminApproves_ThenLoginSucceeds()
    {
        using var db = CreateDb();
        var adminService = new AdminUserService(db);
        var authService = new AuthService(db, new TokenHandler(BuildJwtConfig()), BuildJwtConfig());

        const string email = "new.admin.hardening2@test.local";
        const string password = "StrongPass123";

        // A SuperAdmin creates a new Admin account via the real, existing
        // SuperAdmin-gated creation flow.
        var newAdminId = await adminService.CreateAdminUserAsync(new AddAdminUserRequest
        {
            FirstName = "Nia",
            LastName = "Newton",
            Email = email,
            Password = password,
            Role = "Admin",
            Department = "HR",
        });

        // It must start PendingApproval, not immediately active.
        var created = await adminService.GetAdminUserByIdAsync(newAdminId);
        Assert.Equal(AdminUserStatus.PendingApproval, created.Status);

        // Login must be rejected with a clear, specific message while pending.
        var pendingLoginEx = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            authService.AuthenticateUserLoginAsync(new LoginRequest { Email = email, Password = password }));
        Assert.Contains("pending approval", pendingLoginEx.Message, StringComparison.OrdinalIgnoreCase);

        // A SuperAdmin approves the pending account.
        var approved = await adminService.UpdateAdminApprovalStatusAsync(newAdminId, AdminUserStatus.Approved);
        Assert.Equal(AdminUserStatus.Approved, approved.Status);

        // Login now succeeds with real, working access (a valid JWT carrying the Admin role).
        var loginResponse = await authService.AuthenticateUserLoginAsync(new LoginRequest { Email = email, Password = password });
        Assert.False(string.IsNullOrWhiteSpace(loginResponse.Token));
        Assert.Equal("Admin", loginResponse.Role);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(loginResponse.Token);
        var roleClaim = jwt.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.Role)?.Value;
        Assert.Equal("Admin", roleClaim);
    }

    [Fact]
    public async Task RejectedAdmin_CannotLogin_EvenThoughPasswordIsCorrect()
    {
        using var db = CreateDb();
        var adminService = new AdminUserService(db);
        var authService = new AuthService(db, new TokenHandler(BuildJwtConfig()), BuildJwtConfig());

        const string email = "rejected.admin.hardening2@test.local";
        const string password = "StrongPass123";

        var newAdminId = await adminService.CreateAdminUserAsync(new AddAdminUserRequest
        {
            FirstName = "Rex",
            LastName = "Rejected",
            Email = email,
            Password = password,
            Role = "Admin",
            Department = "Finance",
        });

        await adminService.UpdateAdminApprovalStatusAsync(newAdminId, AdminUserStatus.Rejected);

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            authService.AuthenticateUserLoginAsync(new LoginRequest { Email = email, Password = password }));
        Assert.Contains("rejected", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LegacyAdmin_WithNoStatusField_DefaultsToApproved_IsNotLockedOut()
    {
        using var db = CreateDb();

        // Simulate an AdminUser created BEFORE this change: no Status set at all
        // (mirrors documents already in the database with no Status field).
        var legacyId = ObjectId.GenerateNewId();
        const string email = "legacy.admin.hardening2@test.local";
        const string password = "StrongPass123";
        var hashedPassword = BCrypt.Net.BCrypt.HashPassword(password);

        db.AdminUsers.Add(new AdminUser
        {
            Id = legacyId,
            FirstName = "Legacy",
            LastName = "Admin",
            Email = email,
            Password = hashedPassword,
            Role = "Admin",
            Department = "HR",
            Status = null, // no approval field existed when this account was created
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow,
        });

        db.AppUsers.Add(new AppUser
        {
            Id = ObjectId.GenerateNewId(),
            AppUserId = legacyId,
            Email = email,
            Password = hashedPassword,
            FirstName = "Legacy",
            LastName = "Admin",
            Role = "Admin",
            EmailConfirmed = true,
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow,
        });

        await db.SaveChangesAsync();

        var authService = new AuthService(db, new TokenHandler(BuildJwtConfig()), BuildJwtConfig());

        // Must NOT be locked out by the new approval gate.
        var loginResponse = await authService.AuthenticateUserLoginAsync(new LoginRequest { Email = email, Password = password });
        Assert.False(string.IsNullOrWhiteSpace(loginResponse.Token));
        Assert.Equal("Admin", loginResponse.Role);
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
