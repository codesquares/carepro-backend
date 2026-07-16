using System.IdentityModel.Tokens.Jwt;
using Application.DTOs;
using Application.Interfaces.Content;
using Application.Interfaces.Email;
using CarePro_Api.Controllers.Content;
using Infrastructure.Content.Services.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace CommitmentGate.Tests;

public class OnboardingResetQaAccessTests
{
    [Fact]
    public async Task TokenHandler_CreateTokenAsync_IncludesQaClaims_WhenFlagsEnabled()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "this_is_a_test_key_with_sufficient_length_12345",
                ["Jwt:Issuer"] = "carepro-tests",
                ["Jwt:Audience"] = "carepro-tests",
                ["Jwt:DurationInHours"] = "1"
            })
            .Build();

        var tokenHandler = new TokenHandler(config);

        var token = await tokenHandler.CreateTokenAsync(new AppUserDTO
        {
            AppUserId = "client-qa-1",
            Email = "qa.client@test.local",
            Role = "Client",
            QaAccess = true,
            OnboardingResetAccess = true
        });

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.Contains(jwt.Claims, c => c.Type == "qa_access" && c.Value == "true");
        Assert.Contains(jwt.Claims, c => c.Type == "onboarding_reset_access" && c.Value == "true");
        Assert.Contains(jwt.Claims, c => c.Type == "http://schemas.microsoft.com/ws/2008/06/identity/claims/role" && c.Value == "Client");
    }

    [Fact]
    public async Task GrantClientOnboardingResetAccess_Production_ReturnsNotFound()
    {
        var controller = CreateAdminsController(
            hostEnvironmentName: "Production",
            grantResponse: new GrantClientOnboardingResetAccessResponse());

        var result = await controller.GrantClientOnboardingResetAccess(new GrantClientOnboardingResetAccessRequest
        {
            Email = "qa.client@test.local"
        });

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GrantClientOnboardingResetAccess_NonProduction_ReturnsUpdatedFlags()
    {
        var expected = new GrantClientOnboardingResetAccessResponse
        {
            AppUserId = "66b67b5555f2f7ec7cc01010",
            Email = "qa.client@test.local",
            Role = "Client",
            QaAccess = true,
            OnboardingResetAccess = true,
            Message = "Access flags updated. User must login again (or refresh token) to receive new claims."
        };

        var controller = CreateAdminsController(
            hostEnvironmentName: "Staging",
            grantResponse: expected);

        var result = await controller.GrantClientOnboardingResetAccess(new GrantClientOnboardingResetAccessRequest
        {
            Email = expected.Email
        });

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<GrantClientOnboardingResetAccessResponse>(ok.Value);

        Assert.Equal(expected.Email, body.Email);
        Assert.True(body.QaAccess);
        Assert.True(body.OnboardingResetAccess);
    }

    [Fact]
    public async Task RevokeClientOnboardingResetAccess_NonProduction_ReturnsFlagsFalse()
    {
        var expected = new GrantClientOnboardingResetAccessResponse
        {
            AppUserId = "66b67b5555f2f7ec7cc01010",
            Email = "qa.client@test.local",
            Role = "Client",
            QaAccess = false,
            OnboardingResetAccess = false,
            Message = "Access flags updated. User must login again (or refresh token) to receive new claims."
        };

        var controller = CreateAdminsController(
            hostEnvironmentName: "Staging",
            grantResponse: expected);

        var result = await controller.RevokeClientOnboardingResetAccess(new GrantClientOnboardingResetAccessRequest
        {
            Email = expected.Email,
            QaAccess = true,
            OnboardingResetAccess = true
        });

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<GrantClientOnboardingResetAccessResponse>(ok.Value);

        Assert.False(body.QaAccess);
        Assert.False(body.OnboardingResetAccess);
    }

    private static AdminsController CreateAdminsController(
        string hostEnvironmentName,
        GrantClientOnboardingResetAccessResponse grantResponse)
    {
        var adminService = new Mock<IAdminUserService>();
        adminService
            .Setup(s => s.GrantClientOnboardingResetAccessAsync(It.IsAny<GrantClientOnboardingResetAccessRequest>()))
            .ReturnsAsync(grantResponse);

        var hostEnvironment = new Mock<IHostEnvironment>();
        hostEnvironment.SetupGet(x => x.EnvironmentName).Returns(hostEnvironmentName);

        return new AdminsController(
            adminService.Object,
            Mock.Of<IClientOrderService>(),
            Mock.Of<ICareGiverService>(),
            Mock.Of<IClientService>(),
            Mock.Of<IEmailService>(),
            Mock.Of<ICertificationService>(),
            Mock.Of<IDefaultAddressCleanupService>(),
            Mock.Of<ILogger<AdminsController>>(),
            hostEnvironment.Object);
    }
}
