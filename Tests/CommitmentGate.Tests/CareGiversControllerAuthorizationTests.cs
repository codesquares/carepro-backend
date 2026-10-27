using System.Security.Claims;
using Application.DTOs;
using Application.Interfaces.Authentication;
using Application.Interfaces.Content;
using CarePro_Api.Controllers.Content;
using Infrastructure.Content.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace CommitmentGate.Tests;

/// <summary>
/// Covers the self-or-admin IDOR guard added to CareGiversController's write endpoints,
/// which previously had no [Authorize] at all and let any caller mutate any caregiver's
/// profile by guessing an ID.
/// </summary>
public class CareGiversControllerAuthorizationTests
{
    [Fact]
    public async Task UpdateCaregiverAvailability_CallerIsDifferentCaregiver_ReturnsForbid()
    {
        var controller = CreateController(callerUserId: "caregiver-A", callerRole: "Caregiver");

        var result = await controller.UpdateCaregiverAvailabilityAsync(
            "caregiver-B",
            new UpdateCaregiverAvailabilityRequest { IsAvailable = true });

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task UpdateCaregiverAvailability_CallerIsSameCaregiver_IsNotForbidden()
    {
        var caregiverServiceMock = new Mock<ICareGiverService>();
        caregiverServiceMock
            .Setup(s => s.UpdateCaregiverAvailabilityAsync("caregiver-A", It.IsAny<UpdateCaregiverAvailabilityRequest>()))
            .ReturnsAsync("Availability updated.");

        var controller = CreateController(callerUserId: "caregiver-A", callerRole: "Caregiver", caregiverServiceMock);

        var result = await controller.UpdateCaregiverAvailabilityAsync(
            "caregiver-A",
            new UpdateCaregiverAvailabilityRequest { IsAvailable = true });

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task UpdateCaregiverAvailability_CallerIsAdmin_IsNotForbidden_EvenForOtherCaregiver()
    {
        var caregiverServiceMock = new Mock<ICareGiverService>();
        caregiverServiceMock
            .Setup(s => s.UpdateCaregiverAvailabilityAsync("caregiver-B", It.IsAny<UpdateCaregiverAvailabilityRequest>()))
            .ReturnsAsync("Availability updated.");

        var controller = CreateController(callerUserId: "admin-1", callerRole: "Admin", caregiverServiceMock);

        var result = await controller.UpdateCaregiverAvailabilityAsync(
            "caregiver-B",
            new UpdateCaregiverAvailabilityRequest { IsAvailable = true });

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task UpdateCaregiverLocation_CallerIsDifferentCaregiver_ReturnsForbid()
    {
        var controller = CreateController(callerUserId: "caregiver-A", callerRole: "Caregiver");

        var result = await controller.UpdateCaregiverLocationAsync(
            "caregiver-B",
            new UpdateCaregiverLocationRequest { Address = "1 Test Street" });

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task UpdateProfilePicture_CallerIsDifferentCaregiver_ReturnsForbid()
    {
        var controller = CreateController(callerUserId: "caregiver-A", callerRole: "Caregiver");

        var result = await controller.UpdateProfilePictureAsync(
            "caregiver-B",
            new UpdateProfilePictureRequest());

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task UpdateCaregiverAdditionalInfo_CallerIsDifferentCaregiver_ReturnsForbid()
    {
        var controller = CreateController(callerUserId: "caregiver-A", callerRole: "Caregiver");

        var result = await controller.UpdateCaregiverAdditionalInfoAsync(
            "caregiver-B",
            new UpdateCaregiverAdditionalInfoRequest());

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task UpdateCaregiverAboutMe_CallerIsDifferentCaregiver_ReturnsForbid()
    {
        var controller = CreateController(callerUserId: "caregiver-A", callerRole: "Caregiver");

        var result = await controller.UpdateCaregiverAboutMeAsync(
            "caregiver-B",
            new UpdateCaregiverAdditionalInfoRequest());

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task SoftDeleteCaregiver_CallerIsDifferentCaregiver_ReturnsForbid()
    {
        var controller = CreateController(callerUserId: "caregiver-A", callerRole: "Caregiver");

        var result = await controller.SoftDeleteCaregiverAsync("caregiver-B");

        Assert.IsType<ForbidResult>(result);
    }

    private static CareGiversController CreateController(
        string callerUserId,
        string callerRole,
        Mock<ICareGiverService>? caregiverServiceMock = null)
    {
        caregiverServiceMock ??= new Mock<ICareGiverService>();

        var controller = new CareGiversController(
            careProDbContext: null!,
            careGiverService: caregiverServiceMock.Object,
            logger: LoggerFactory.Create(_ => { }).CreateLogger<CareGiversController>(),
            httpContextAccessor: Mock.Of<IHttpContextAccessor>(),
            googleAuthService: Mock.Of<IGoogleAuthService>(),
            userDeletionService: Mock.Of<IUserDeletionService>());

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, callerUserId),
            new Claim(ClaimTypes.Role, callerRole)
        };

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
            }
        };

        return controller;
    }
}
