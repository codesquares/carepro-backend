using System.Net;
using CarePro_Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CommitmentGate.Tests;

/// <summary>
/// Boots the real host with its real JWT bearer authentication (no test auth handler
/// override), so a request with no Authorization header goes through the actual
/// [Authorize]/[AllowAnonymous] pipeline instead of a stubbed-out one. Shared across all
/// cases in the fixture below since a full host boot is expensive.
/// </summary>
public sealed class CaregiverIdentityExposureHostFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            // Prevent long-running background services (and their startup-time Mongo
            // calls) from running in this integration test.
            var hostedServices = services
                .Where(d => d.ServiceType == typeof(IHostedService))
                .ToList();

            foreach (var hostedService in hostedServices)
            {
                services.Remove(hostedService);
            }
        });
    }
}

/// <summary>
/// Real end-to-end HTTP coverage (no mocked auth) for the caregiver-identity-exposure fix:
/// endpoints that previously returned identifiable caregiver data with zero authentication
/// must now be unreachable anonymously.
/// </summary>
[Collection("EnvironmentVariableIsolation")]
public class CaregiverIdentityExposureHttpTests : IClassFixture<CaregiverIdentityExposureHostFactory>
{
    private readonly HttpClient _client;

    public CaregiverIdentityExposureHttpTests(CaregiverIdentityExposureHostFactory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
    }

    [Theory]
    // Removed entirely - route no longer exists.
    [InlineData("GET", "/api/CareGivers/AllCaregivers", HttpStatusCode.NotFound)]
    [InlineData("GET", "/api/CareGivers/some-caregiver-id", HttpStatusCode.NotFound)]
    [InlineData("GET", "/api/Searchs/search-caregivers?searchTerm=x", HttpStatusCode.NotFound)]
    // [AllowAnonymous] removed - now requires authentication.
    [InlineData("GET", "/api/Gigs/some-gig-id", HttpStatusCode.Unauthorized)]
    [InlineData("GET", "/api/Reviews?gigId=some-gig-id", HttpStatusCode.Unauthorized)]
    [InlineData("GET", "/api/Reviews/some-review-id", HttpStatusCode.Unauthorized)]
    [InlineData("GET", "/api/Reviews/caregiver/some-caregiver-id", HttpStatusCode.Unauthorized)]
    // Previously had no [Authorize] at all on the mutation endpoints - now requires authentication.
    [InlineData("PUT", "/api/CareGivers/UpdateCaregiverInfo/some-caregiver-id", HttpStatusCode.Unauthorized)]
    [InlineData("PUT", "/api/CareGivers/UpdateProfilePicture/some-caregiver-id", HttpStatusCode.Unauthorized)]
    [InlineData("PUT", "/api/CareGivers/UpdateCaregiverAboutMeInfo/some-caregiver-id", HttpStatusCode.Unauthorized)]
    [InlineData("PUT", "/api/CareGivers/UpdateCaregiverAvailability/some-caregiver-id", HttpStatusCode.Unauthorized)]
    [InlineData("PUT", "/api/CareGivers/UpdateCaregiverLocation/some-caregiver-id", HttpStatusCode.Unauthorized)]
    [InlineData("PUT", "/api/CareGivers/SoftDeleteCaregiver/some-caregiver-id", HttpStatusCode.Unauthorized)]
    public async Task UnauthenticatedRequest_ToFormerlyOpenCaregiverEndpoint_NoLongerSucceeds(
        string method, string path, HttpStatusCode expectedStatus)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        using var response = await _client.SendAsync(request);

        Assert.Equal(expectedStatus, response.StatusCode);
        Console.WriteLine($"{method} {path} -> {(int)response.StatusCode} {response.StatusCode}");
    }
}
