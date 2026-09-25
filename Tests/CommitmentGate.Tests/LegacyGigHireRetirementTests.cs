using System.Net;
using System.Security.Claims;
using Application.DTOs;
using Application.Interfaces;
using Application.Interfaces.Content;
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
/// Locks in the retirement of the legacy "hire a specific caregiver" flow:
/// a client used to be able to pick a named caregiver's gig, pay via POST /payments/initiate and have a
/// legacy ClientOrder created with that caregiver (wallet credited, no assignment step). The pivot sells
/// packages and assigns caregivers internally, so that path must be closed for every caller, and the gig
/// detail endpoint must stop telling clients who is behind a gig.
/// </summary>
public class LegacyGigHireRetirementTests
{
    private const string OwnerCaregiverId = "caregiver-owner-1";
    private const string GigId = "gig-1";

    // ---------- POST /api/payments/initiate ----------

    private static (PaymentsController controller, Mock<IPendingPaymentService> pending) BuildPayments(string userId, string role)
    {
        var pending = new Mock<IPendingPaymentService>(MockBehavior.Strict); // any call = test failure
        var controller = new PaymentsController(
            pending.Object,
            Mock.Of<IBookingCommitmentService>(),
            Mock.Of<IPackagePaymentService>(),
            flutterwaveService: null!,   // never touched by the blocked endpoint
            Mock.Of<IReceiptPdfService>(),
            Mock.Of<ISubscriptionService>(),
            Mock.Of<ILogger<PaymentsController>>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = Principal(userId, role) } }
        };
        return (controller, pending);
    }

    [Theory]
    [InlineData("Client")]
    [InlineData("Caregiver")]
    [InlineData("Admin")]
    [InlineData("SuperAdmin")]
    public void InitiatePayment_IsRejected_ForEveryRole_AndNeverReachesPaymentService(string role)
    {
        var (controller, pending) = BuildPayments("some-user", role);

        var result = controller.InitiatePayment(new InitiatePaymentRequest
        {
            GigId = GigId,
            ServiceType = "one-time",
            FrequencyPerWeek = 1,
            Email = "client@example.com",
            RedirectUrl = "https://example.com/return"
        });

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, status.StatusCode);
        // Strict mock: reaching CreatePendingPaymentAsync would have thrown. Also assert explicitly.
        pending.Verify(p => p.CreatePendingPaymentAsync(It.IsAny<InitiatePaymentRequest>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void InitiatePayment_ResponseBody_HasSuccessFalse_AndPackageGuidance()
    {
        var (controller, _) = BuildPayments("client-1", "Client");

        var result = (ObjectResult)controller.InitiatePayment(new InitiatePaymentRequest { GigId = GigId });

        var json = System.Text.Json.JsonSerializer.Serialize(result.Value);
        Assert.Contains("\"success\":false", json);
        Assert.Contains("care package", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InitiatePayment_WithNullBody_StillRejectsCleanly()
    {
        var (controller, _) = BuildPayments("client-1", "Client");

        var result = controller.InitiatePayment(null!);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    // ---------- GET /api/Gigs/{id} : caregiver name must not reach clients ----------

    private static GigsController BuildGigs(string userId, string role, Mock<IGigServices>? gigs = null)
    {
        gigs ??= new Mock<IGigServices>();
        gigs.Setup(g => g.GetGigAsync(GigId)).ReturnsAsync(() => new GigDTO
        {
            Id = GigId,
            Title = "Registered nurse for elderly care",
            Price = 12000,
            Status = "Active",
            CaregiverId = OwnerCaregiverId,
            CaregiverName = "Ada Lovelace",
        });

        return new GigsController(gigs.Object, Mock.Of<ILogger<GigsController>>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = Principal(userId, role) } }
        };
    }

    private static async Task<GigDTO> GetGigBody(GigsController controller)
    {
        var ok = Assert.IsType<OkObjectResult>(await controller.GetGigAsync(GigId));
        return Assert.IsType<GigDTO>(ok.Value);
    }

    [Fact]
    public async Task GetGig_AsClient_CaregiverNameIsRemoved_ButGigFieldsRemain()
    {
        var body = await GetGigBody(BuildGigs("client-1", "Client"));

        Assert.Null(body.CaregiverName);
        Assert.Equal("Registered nurse for elderly care", body.Title);
        Assert.Equal(12000, body.Price);
    }

    [Fact]
    public async Task GetGig_AsClient_SerializedJson_HasNoCaregiverNameKeyAtAll()
    {
        var body = await GetGigBody(BuildGigs("client-1", "Client"));

        var json = System.Text.Json.JsonSerializer.Serialize(body);

        Assert.DoesNotContain("caregivername", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Ada", json);
    }

    [Fact]
    public async Task GetGig_AsOwner_SerializedJson_StillCarriesCaregiverName()
    {
        var body = await GetGigBody(BuildGigs(OwnerCaregiverId, "Caregiver"));

        var json = System.Text.Json.JsonSerializer.Serialize(body);

        Assert.Contains("Ada Lovelace", json);
    }

    [Fact]
    public async Task GetGig_AsDifferentCaregiver_CaregiverNameIsRemoved()
    {
        var body = await GetGigBody(BuildGigs("caregiver-someone-else", "Caregiver"));

        Assert.Null(body.CaregiverName);
    }

    [Fact]
    public async Task GetGig_AsOwningCaregiver_KeepsCaregiverName()
    {
        var body = await GetGigBody(BuildGigs(OwnerCaregiverId, "Caregiver"));

        Assert.Equal("Ada Lovelace", body.CaregiverName);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("SuperAdmin")]
    public async Task GetGig_AsAdmin_KeepsCaregiverName(string role)
    {
        var body = await GetGigBody(BuildGigs("admin-1", role));

        Assert.Equal("Ada Lovelace", body.CaregiverName);
    }

    // ---------- GET /api/Gigs/deleted : sibling route that also returned the caregiver's name ----------

    [Fact]
    public async Task DeletedGigs_ForAnotherCaregiversId_IsForbidden_AndNeverQueried()
    {
        var gigs = new Mock<IGigServices>(MockBehavior.Strict);
        var controller = new GigsController(gigs.Object, Mock.Of<ILogger<GigsController>>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = Principal("client-1", "Client") } }
        };

        var result = await controller.GetDeletedGigsByCaregiverAsync(OwnerCaregiverId);

        Assert.IsType<ForbidResult>(result);
        gigs.Verify(g => g.GetDeletedGigsByCaregiverAsync(It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData(OwnerCaregiverId, "Caregiver")]
    [InlineData("admin-1", "Admin")]
    public async Task DeletedGigs_ForOwnerOrAdmin_StillWorks(string userId, string role)
    {
        var gigs = new Mock<IGigServices>();
        gigs.Setup(g => g.GetDeletedGigsByCaregiverAsync(OwnerCaregiverId)).ReturnsAsync(new List<DeletedGigDTO>());
        var controller = new GigsController(gigs.Object, Mock.Of<ILogger<GigsController>>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = Principal(userId, role) } }
        };

        var result = await controller.GetDeletedGigsByCaregiverAsync(OwnerCaregiverId);

        Assert.IsType<OkObjectResult>(result);
    }

    // ---------- GET /api/Gigs (public list): never carries a caregiver name ----------

    [Fact]
    public void GigListMapping_NeverPopulatesCaregiverName()
    {
        // GetAllGigsAsync / GetAllGigsPaginatedAsync / caregiver-scoped lists build GigDTO without
        // CaregiverName; only GetGigAsync (single) and the deleted-gig lists set it. Pin that by source
        // so a future "enrich the list" change has to consciously break this test.
        var source = File.ReadAllText(FindRepoFile("Infrastructure/Content/Services/GigServices.cs"));
        var listRegion = Slice(source, "public async Task<IEnumerable<GigDTO>> GetAllGigsAsync()", "public async Task<GigDTO> GetGigAsync");
        Assert.DoesNotContain("CaregiverName", listRegion);
    }

    // ---------- real host, real JWT pipeline ----------

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

        [Fact]
        public async Task Initiate_WithoutToken_IsStill401_NotDowngradedToAnonymousSuccess()
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/payments/initiate")
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
            };
            using var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    // ---------- helpers ----------

    private static ClaimsPrincipal Principal(string userId, string role) =>
        new(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Role, role),
        }, "TestAuth", ClaimTypes.Name, ClaimTypes.Role));

    private static string FindRepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, relative)))
        {
            dir = dir.Parent;
        }
        return dir == null ? throw new FileNotFoundException(relative) : Path.Combine(dir.FullName, relative);
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "GigServices markers moved — update this test's region markers.");
        return source[start..end];
    }
}
