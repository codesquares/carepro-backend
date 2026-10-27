using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Application.DTOs;
using Application.Interfaces;
using CarePro_Api;
using CarePro_Api.Controllers.Content;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace CommitmentGate.Tests;

/// <summary>
/// Follow-up to <see cref="LegacyGigHireRetirementTests"/>: closes the rest of the gig-identity exposure
/// (GET /Gigs/{id} leaking the caregiver's id / intro video / professional history / non-public gigs, and the four
/// caregiver-scoped listing routes having no ownership check) and retires the admin Care Matching endpoint and the
/// anonymous gig share page.
/// </summary>
public class GigIdentityHardeningTests
{
    private const string Owner = "caregiver-owner-1";
    private const string OtherCaregiver = "caregiver-other-2";
    private const string GigId = "gig-1";

    private static ClaimsPrincipal Principal(string userId, string role) =>
        new(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Role, role),
        }, "TestAuth", ClaimTypes.Name, ClaimTypes.Role));

    private static GigsController Controller(Mock<IGigServices> gigs, string userId, string role) =>
        new(gigs.Object, Mock.Of<ILogger<GigsController>>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = Principal(userId, role) } }
        };

    private static Mock<IGigServices> GigWithStatus(string status)
    {
        var gigs = new Mock<IGigServices>();
        gigs.Setup(g => g.GetGigAsync(GigId)).ReturnsAsync(() => new GigDTO
        {
            Id = GigId,
            Title = "Registered nurse for elderly care",
            Price = 12000,
            Status = status,
            CaregiverId = Owner,
            CaregiverName = "Ada Lovelace",
            VideoURL = "https://cdn.example/intro-video.mp4",
            CaregiverEducation = new() { new CaregiverEducationResponse { SchoolName = "University of Lagos" } },
            CaregiverCertifications = new() { new CaregiverQualificationResponse { CertificationName = "RN Licence" } },
            CaregiverWorkExperience = new() { new CaregiverWorkExperienceResponse { OrganisationName = "Lagos General Hospital" } },
        });
        return gigs;
    }

    private static async Task<string> BodyJson(GigsController c)
    {
        var ok = Assert.IsType<OkObjectResult>(await c.GetGigAsync(GigId));
        return JsonSerializer.Serialize(ok.Value);
    }

    // ---------- GET /Gigs/{id}: identity-adjacent fields ----------

    [Theory]
    [InlineData("client-1", "Client")]
    [InlineData(OtherCaregiver, "Caregiver")]
    public async Task GetGig_ForNonOwnerNonAdmin_OmitsEveryIdentityAdjacentField_ButKeepsGigBasics(string user, string role)
    {
        var json = await BodyJson(Controller(GigWithStatus("Published"), user, role));

        foreach (var leaked in new[] { "caregiverName", "caregiverId", "videoURL", "caregiverEducation", "caregiverCertifications", "caregiverWorkExperience" })
            Assert.DoesNotContain(leaked, json, StringComparison.OrdinalIgnoreCase);
        foreach (var value in new[] { "Ada Lovelace", Owner, "intro-video", "University of Lagos", "RN Licence", "Lagos General Hospital" })
            Assert.DoesNotContain(value, json);

        Assert.Contains("Registered nurse for elderly care", json);
        Assert.Contains("12000", json);
    }

    [Theory]
    [InlineData(Owner, "Caregiver")]
    [InlineData("admin-1", "Admin")]
    [InlineData("super-1", "SuperAdmin")]
    public async Task GetGig_ForOwnerOrAdmin_StillReturnsEverything(string user, string role)
    {
        var json = await BodyJson(Controller(GigWithStatus("Published"), user, role));

        foreach (var expected in new[] { "Ada Lovelace", Owner, "intro-video", "University of Lagos", "RN Licence", "Lagos General Hospital" })
            Assert.Contains(expected, json);
    }

    // ---------- GET /Gigs/{id}: non-public gigs ----------

    [Theory]
    [InlineData("Draft", "client-1", "Client")]
    [InlineData("Draft", OtherCaregiver, "Caregiver")]
    [InlineData("draft", "client-1", "Client")]
    [InlineData("Paused", "client-1", "Client")]
    [InlineData("Paused", OtherCaregiver, "Caregiver")]
    [InlineData("SomethingNew", "client-1", "Client")]
    public async Task GetGig_NonPublicGig_IsNotFound_ForNonOwnerNonAdmin_AndViewIsNotTracked(string status, string user, string role)
    {
        var gigs = GigWithStatus(status);
        var result = await Controller(gigs, user, role).GetGigAsync(GigId);

        Assert.IsType<NotFoundObjectResult>(result);
        gigs.Verify(g => g.TrackGigViewAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Never);
    }

    [Theory]
    [InlineData("Draft", Owner, "Caregiver")]
    [InlineData("Draft", "admin-1", "Admin")]
    [InlineData("Paused", Owner, "Caregiver")]
    [InlineData("Paused", "super-1", "SuperAdmin")]
    public async Task GetGig_NonPublicGig_IsVisible_ToOwnerAndAdmin(string status, string user, string role)
    {
        var result = await Controller(GigWithStatus(status), user, role).GetGigAsync(GigId);

        Assert.IsType<OkObjectResult>(result);
    }

    [Theory]
    [InlineData("Published")]
    [InlineData("Active")]
    [InlineData("active")]
    public async Task GetGig_PublishedOrActive_IsStillVisible_ToAnyAuthenticatedUser(string status)
    {
        var result = await Controller(GigWithStatus(status), "client-1", "Client").GetGigAsync(GigId);

        Assert.IsType<OkObjectResult>(result);
    }

    // ---------- the four caregiver-scoped routes ----------

    private static (string name, Func<GigsController, Task<IActionResult>> call, Action<Mock<IGigServices>> setup, Action<Mock<IGigServices>> neverCalled)[] Routes() => new (string, Func<GigsController, Task<IActionResult>>, Action<Mock<IGigServices>>, Action<Mock<IGigServices>>)[]
    {
        ("caregiver/{id}", c => c.GetAllCaregiverGigsAsync(Owner),
            g => g.Setup(x => x.GetAllCaregiverGigsAsync(Owner)).ReturnsAsync(new List<GigDTO>()),
            g => g.Verify(x => x.GetAllCaregiverGigsAsync(It.IsAny<string>()), Times.Never)),
        ("service/{id}", c => c.GetAllCaregiverGigsServicesAsync(Owner),
            g => g.Setup(x => x.GetAllSubCategoriesForCaregiverAsync(Owner)).ReturnsAsync(new List<string>()),
            g => g.Verify(x => x.GetAllSubCategoriesForCaregiverAsync(It.IsAny<string>()), Times.Never)),
        ("{id}/paused", c => c.GetAllCaregiverPausedGigsAsync(Owner),
            g => g.Setup(x => x.GetAllCaregiverPausedGigsAsync(Owner)).ReturnsAsync(new List<GigDTO>()),
            g => g.Verify(x => x.GetAllCaregiverPausedGigsAsync(It.IsAny<string>()), Times.Never)),
        ("{id}/draft", c => c.GetAllCaregiverDraftGigsAsync(Owner),
            g => g.Setup(x => x.GetAllCaregiverDraftGigsAsync(Owner)).ReturnsAsync(new List<GigDTO>()),
            g => g.Verify(x => x.GetAllCaregiverDraftGigsAsync(It.IsAny<string>()), Times.Never)),
    };

    public static IEnumerable<object[]> RouteIndexes() => Enumerable.Range(0, 4).Select(i => new object[] { i });

    [Theory]
    [MemberData(nameof(RouteIndexes))]
    public async Task CaregiverScopedRoute_RejectsNonOwner_Client_AndNeverQueries(int i)
    {
        var (_, call, _, neverCalled) = Routes()[i];
        var gigs = new Mock<IGigServices>();

        var result = await call(Controller(gigs, "client-1", "Client"));

        Assert.IsType<ForbidResult>(result);
        neverCalled(gigs);
    }

    [Theory]
    [MemberData(nameof(RouteIndexes))]
    public async Task CaregiverScopedRoute_RejectsAnotherCaregiver_AndNeverQueries(int i)
    {
        var (_, call, _, neverCalled) = Routes()[i];
        var gigs = new Mock<IGigServices>();

        var result = await call(Controller(gigs, OtherCaregiver, "Caregiver"));

        Assert.IsType<ForbidResult>(result);
        neverCalled(gigs);
    }

    [Theory]
    [MemberData(nameof(RouteIndexes))]
    public async Task CaregiverScopedRoute_AllowsOwner(int i)
    {
        var (_, call, setup, _) = Routes()[i];
        var gigs = new Mock<IGigServices>();
        setup(gigs);

        var result = await call(Controller(gigs, Owner, "Caregiver"));

        Assert.IsType<OkObjectResult>(result);
    }

    [Theory]
    [MemberData(nameof(RouteIndexes))]
    public async Task CaregiverScopedRoute_AllowsAdmin(int i)
    {
        var (_, call, setup, _) = Routes()[i];
        var gigs = new Mock<IGigServices>();
        setup(gigs);

        var result = await call(Controller(gigs, "admin-1", "Admin"));

        Assert.IsType<OkObjectResult>(result);
    }

    // ---------- real host: retired surfaces ----------

    [Collection("EnvironmentVariableIsolation")]
    public class RetiredSurfaces : IClassFixture<CaregiverIdentityExposureHostFactory>
    {
        private readonly CaregiverIdentityExposureHostFactory _factory;
        private readonly HttpClient _client;

        public RetiredSurfaces(CaregiverIdentityExposureHostFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });
        }

        [Fact]
        public void CareMatching_RecommendGig_NoLongerExistsInTheRouteTable()
        {
            var endpoints = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();

            Assert.NotEmpty(endpoints); // guard: the table is really populated
            Assert.DoesNotContain(endpoints, e => e.RoutePattern.RawText!.Contains("RecommendGig", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(endpoints, e => e.DisplayName!.Contains("RecommendGig", StringComparison.OrdinalIgnoreCase));
        }

        [Theory]
        [InlineData("/api/Admins/RecommendGig")]
        [InlineData("/api/admins/recommendgig")]
        public async Task CareMatching_RecommendGig_Post_NeverReachesAnAction(string path)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = new StringContent("{\"ClientId\":\"c\",\"GigId\":\"g\"}", System.Text.Encoding.UTF8, "application/json")
            };
            using var response = await _client.SendAsync(request);

            // No POST action exists any more. Depending on which layer answers first it is 404 (no match),
            // 405 (the GET Admins/{id} wildcard matches the path but not the verb) or 401 (auth first) — but
            // never 200/400, which would mean the old action ran. The route-table test above is the hard proof.
            Assert.True(
                response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed or HttpStatusCode.Unauthorized,
                response.StatusCode.ToString());
        }

        [Theory]
        [InlineData("some-gig-id")]
        [InlineData("6aab7ec69517467f2ace5f47")]
        public async Task ShareGigPage_IsGone_410_ForAnyId_Anonymously_AndAdvertisesNothing(string id)
        {
            using var response = await _client.GetAsync($"/api/Share/gig/{id}");
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
            Assert.DoesNotContain("og:title", body);
            Assert.DoesNotContain("/service/", body);
            Assert.DoesNotContain("<html", body, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("/api/Gigs/caregiver/x")]
        [InlineData("/api/Gigs/service/x")]
        [InlineData("/api/Gigs/x/paused")]
        [InlineData("/api/Gigs/x/draft")]
        public async Task CaregiverScopedRoutes_StillRequireAuthentication(string path)
        {
            using var response = await _client.GetAsync(path);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }
}
