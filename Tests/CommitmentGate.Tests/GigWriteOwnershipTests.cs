using System.Net;
using System.Security.Claims;
using Application.DTOs;
using Application.Interfaces;
using CarePro_Api;
using CarePro_Api.Controllers.Content;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace CommitmentGate.Tests;

/// <summary>
/// Gig WRITE routes used to trust a caregiverId taken from the request body/query, so any signed-in user (a plain
/// client included) could pause, edit (price too), delete and restore another caregiver's gig, and create gigs as
/// someone else. Ownership must come from the gig record + the caller's JWT; the request-supplied id is never trusted.
/// </summary>
public class GigWriteOwnershipTests
{
    private const string Owner = "caregiver-owner-1";
    private const string OtherCaregiver = "caregiver-other-2";
    private const string GigId = "gig-1";
    private const string MissingGigId = "gig-missing";

    private static ClaimsPrincipal Principal(string userId, string role) =>
        new(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Role, role),
        }, "TestAuth", ClaimTypes.Name, ClaimTypes.Role));

    private static Mock<IGigServices> Gigs()
    {
        var gigs = new Mock<IGigServices>();
        gigs.Setup(g => g.GetGigOwnerIdAsync(GigId)).ReturnsAsync(Owner);
        gigs.Setup(g => g.GetGigOwnerIdAsync(MissingGigId)).ReturnsAsync((string?)null);
        gigs.Setup(g => g.UpdateGigStatusToPauseAsync(It.IsAny<string>(), It.IsAny<UpdateGigStatusToPauseRequest>())).ReturnsAsync("ok");
        gigs.Setup(g => g.UpdateGigAsync(It.IsAny<string>(), It.IsAny<UpdateGigRequest>())).ReturnsAsync(new GigDTO { Id = GigId });
        gigs.Setup(g => g.SoftDeleteGigAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync("ok");
        gigs.Setup(g => g.RestoreGigAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync("ok");
        gigs.Setup(g => g.CreateGigAsync(It.IsAny<AddGigRequest>())).ReturnsAsync(new GigDTO { Id = GigId });
        return gigs;
    }

    private static GigsController Controller(Mock<IGigServices> gigs, string userId, string role) =>
        new(gigs.Object, Mock.Of<ILogger<GigsController>>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = Principal(userId, role) } }
        };

    // One entry per write action on an existing gig. `spoofed` is what an attacker puts in the body/query.
    private record WriteAction(
        string Name,
        Func<GigsController, string, string, Task<IActionResult?>> Call,
        Action<Mock<IGigServices>, string> VerifyServiceCalledWithOwner,
        Action<Mock<IGigServices>> VerifyNeverCalled);

    private static async Task<IActionResult?> Norm<T>(Task<ActionResult<T>> t)
    {
        var r = await t;
        return r.Result ?? new OkObjectResult(r.Value);
    }

    private static readonly WriteAction[] Actions =
    {
        new("pause",
            async (c, gigId, spoofed) => await Norm(c.UpdateGigStatusToPauseAsync(gigId, new UpdateGigStatusToPauseRequest { Status = "paused", CaregiverId = spoofed })),
            (g, expectedOwner) => g.Verify(x => x.UpdateGigStatusToPauseAsync(GigId, It.Is<UpdateGigStatusToPauseRequest>(r => r.CaregiverId == expectedOwner)), Times.Once),
            g => g.Verify(x => x.UpdateGigStatusToPauseAsync(It.IsAny<string>(), It.IsAny<UpdateGigStatusToPauseRequest>()), Times.Never)),
        new("edit",
            async (c, gigId, spoofed) => await Norm(c.UpdateGigAsync(gigId, new UpdateGigRequest { Price = 1, CaregiverId = spoofed })),
            (g, expectedOwner) => g.Verify(x => x.UpdateGigAsync(GigId, It.Is<UpdateGigRequest>(r => r.CaregiverId == expectedOwner)), Times.Once),
            g => g.Verify(x => x.UpdateGigAsync(It.IsAny<string>(), It.IsAny<UpdateGigRequest>()), Times.Never)),
        new("delete",
            async (c, gigId, spoofed) => await c.SoftDeleteGigAsync(gigId, spoofed),
            (g, expectedOwner) => g.Verify(x => x.SoftDeleteGigAsync(GigId, expectedOwner), Times.Once),
            g => g.Verify(x => x.SoftDeleteGigAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never)),
        new("restore",
            async (c, gigId, spoofed) => await c.RestoreGigAsync(gigId, spoofed),
            (g, expectedOwner) => g.Verify(x => x.RestoreGigAsync(GigId, expectedOwner), Times.Once),
            g => g.Verify(x => x.RestoreGigAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never)),
    };

    public static IEnumerable<object[]> ActionIndexes() => Enumerable.Range(0, 4).Select(i => new object[] { i });

    // ---------- non-owners are rejected (403), even when they spoof the owner's id ----------

    [Theory]
    [MemberData(nameof(ActionIndexes))]
    public async Task Write_ByClient_SpoofingOwnersId_IsForbidden_AndNothingIsMutated(int i)
    {
        var a = Actions[i];
        var gigs = Gigs();

        var result = await a.Call(Controller(gigs, "client-1", "Client"), GigId, Owner);

        Assert.IsType<ForbidResult>(result);
        a.VerifyNeverCalled(gigs);
    }

    [Theory]
    [MemberData(nameof(ActionIndexes))]
    public async Task Write_ByAnotherCaregiver_SpoofingOwnersId_IsForbidden_AndNothingIsMutated(int i)
    {
        var a = Actions[i];
        var gigs = Gigs();

        var result = await a.Call(Controller(gigs, OtherCaregiver, "Caregiver"), GigId, Owner);

        Assert.IsType<ForbidResult>(result);
        a.VerifyNeverCalled(gigs);
    }

    [Theory]
    [MemberData(nameof(ActionIndexes))]
    public async Task Write_OnUnknownGig_Is404_AndNothingIsMutated(int i)
    {
        var a = Actions[i];
        var gigs = Gigs();

        var result = await a.Call(Controller(gigs, Owner, "Caregiver"), MissingGigId, Owner);

        Assert.IsType<NotFoundObjectResult>(result);
        a.VerifyNeverCalled(gigs);
    }

    // ---------- the owner and admins still work, and the service only ever sees the REAL owner id ----------

    [Theory]
    [MemberData(nameof(ActionIndexes))]
    public async Task Write_ByOwner_Works_AndServiceReceivesOwnerId(int i)
    {
        var a = Actions[i];
        var gigs = Gigs();

        var result = await a.Call(Controller(gigs, Owner, "Caregiver"), GigId, Owner);

        Assert.IsType<OkObjectResult>(result);
        a.VerifyServiceCalledWithOwner(gigs, Owner);
    }

    [Theory]
    [MemberData(nameof(ActionIndexes))]
    public async Task Write_ByOwner_WhoSendsSomeoneElsesId_StillActsOnlyAsTheOwner(int i)
    {
        var a = Actions[i];
        var gigs = Gigs();

        var result = await a.Call(Controller(gigs, Owner, "Caregiver"), GigId, "someone-elses-id");

        Assert.IsType<OkObjectResult>(result);
        a.VerifyServiceCalledWithOwner(gigs, Owner); // the request-supplied id was discarded
    }

    [Theory]
    [MemberData(nameof(ActionIndexes))]
    public async Task Write_ByAdmin_Works_AndServiceReceivesTheGigsOwnerNotTheAdmin(int i)
    {
        var a = Actions[i];
        var gigs = Gigs();

        var result = await a.Call(Controller(gigs, "admin-1", "Admin"), GigId, "admin-1");

        Assert.IsType<OkObjectResult>(result);
        a.VerifyServiceCalledWithOwner(gigs, Owner);
    }

    // ---------- POST /Gigs (create) ----------

    private static AddGigRequest Draft(string? caregiverId) => new()
    {
        Title = "New gig",
        Category = "Adult Care",
        SubCategory = new List<string> { "Elderly care" },
        Status = "Draft",
        CaregiverId = caregiverId!,
    };

    [Theory]
    [InlineData("client-1", "Client", Owner)]           // client creating as someone else
    [InlineData("client-1", "Client", "client-1")]      // client creating as themselves (clients can't own gigs)
    [InlineData(OtherCaregiver, "Caregiver", Owner)]    // caregiver creating as another caregiver
    public async Task Create_ByNonOwner_IsForbidden_AndNothingIsCreated(string user, string role, string spoofedCaregiverId)
    {
        var gigs = Gigs();

        var result = await Controller(gigs, user, role).AddGigAsync(Draft(spoofedCaregiverId));

        Assert.IsType<ForbidResult>(result);
        gigs.Verify(g => g.CreateGigAsync(It.IsAny<AddGigRequest>()), Times.Never);
    }

    [Fact]
    public async Task Create_ByCaregiver_ForThemselves_Works()
    {
        var gigs = Gigs();

        var result = await Controller(gigs, Owner, "Caregiver").AddGigAsync(Draft(Owner));

        Assert.IsType<OkObjectResult>(result);
        gigs.Verify(g => g.CreateGigAsync(It.Is<AddGigRequest>(r => r.CaregiverId == Owner)), Times.Once);
    }

    [Fact]
    public async Task Create_ByCaregiver_WithBlankCaregiverId_UsesTheTokenIdentity()
    {
        var gigs = Gigs();

        var result = await Controller(gigs, Owner, "Caregiver").AddGigAsync(Draft(""));

        Assert.IsType<OkObjectResult>(result);
        gigs.Verify(g => g.CreateGigAsync(It.Is<AddGigRequest>(r => r.CaregiverId == Owner)), Times.Once);
    }

    [Fact]
    public async Task Create_ByAdmin_OnBehalfOfACaregiver_StillWorks()
    {
        var gigs = Gigs();

        var result = await Controller(gigs, "admin-1", "Admin").AddGigAsync(Draft(Owner));

        Assert.IsType<OkObjectResult>(result);
        gigs.Verify(g => g.CreateGigAsync(It.Is<AddGigRequest>(r => r.CaregiverId == Owner)), Times.Once);
    }

    // ---------- admin bulk delete: audit identity comes from the token ----------

    [Fact]
    public async Task BulkDelete_AuditIdentity_IsTheCallersToken_NotTheBodyClaim()
    {
        var gigs = Gigs();
        gigs.Setup(g => g.AdminBulkSoftDeleteGigsAsync(It.IsAny<List<string>?>(), It.IsAny<bool>(), It.IsAny<string>()))
            .ReturnsAsync(new AdminBulkDeleteResult());

        await Controller(gigs, "real-admin-7", "Admin").AdminBulkSoftDeleteGigsAsync(new AdminBulkDeleteGigsRequest
        {
            GigIds = new List<string> { GigId },
            AdminUserId = "spoofed-admin-id",
        });

        gigs.Verify(g => g.AdminBulkSoftDeleteGigsAsync(It.IsAny<List<string>?>(), false, "real-admin-7"), Times.Once);
        gigs.Verify(g => g.AdminBulkSoftDeleteGigsAsync(It.IsAny<List<string>?>(), It.IsAny<bool>(), "spoofed-admin-id"), Times.Never);
    }

    // ---------- real host: the pipeline still demands authentication first ----------

    [Collection("EnvironmentVariableIsolation")]
    public class Http : IClassFixture<CaregiverIdentityExposureHostFactory>
    {
        private readonly HttpClient _client;

        public Http(CaregiverIdentityExposureHostFactory factory) =>
            _client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });

        [Theory]
        [InlineData("POST", "/api/Gigs")]
        [InlineData("PUT", "/api/Gigs/UpdateGigStatusToPause/6aab7ec69517467f2ace5f47")]
        [InlineData("PUT", "/api/Gigs/UpdateGig/6aab7ec69517467f2ace5f47")]
        [InlineData("DELETE", "/api/Gigs/SoftDeleteGig/6aab7ec69517467f2ace5f47?caregiverId=x")]
        [InlineData("PUT", "/api/Gigs/RestoreGig/6aab7ec69517467f2ace5f47?caregiverId=x")]
        [InlineData("DELETE", "/api/Gigs/admin/BulkSoftDelete")]
        public async Task GigWriteRoutes_Anonymous_Is401(string method, string path)
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), path);
            using var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }
}
