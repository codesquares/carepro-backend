using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using CarePro_Api;
using Domain.Settings;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace CommitmentGate.Tests;

/// <summary>
/// Real end-to-end HTTP coverage for the server-side circuit breaker added to
/// <c>POST /api/booking-commitment/initiate</c>. Before this fix, <see cref="CommitmentFeeSettings.Enabled"/>
/// only gated the frontend "is payment required" check (<c>GetCommitmentStatusAsync</c>) — the endpoint
/// that actually calls Flutterwave and creates the ₦5,000 charge had no guard of its own and was reachable
/// regardless of the flag. These tests hit the real controller through the real ASP.NET Core pipeline
/// (no frontend involved, simulating a bookmarked link or direct API call) with the flag forced to each
/// value, proving: disabled -> rejected before the request ever reaches the payment logic; enabled ->
/// byte-for-byte the same behavior as before this change (same validation error, same status code).
/// </summary>
public sealed class BookingCommitmentGateHostFactory : WebApplicationFactory<Program>
{
    private readonly bool _commitmentFeeEnabled;
    public BookingCommitmentGateHostFactory(bool commitmentFeeEnabled) => _commitmentFeeEnabled = commitmentFeeEnabled;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = SingleClientAuthHandler.Scheme;
                options.DefaultChallengeScheme = SingleClientAuthHandler.Scheme;
            }).AddScheme<AuthenticationSchemeOptions, SingleClientAuthHandler>(SingleClientAuthHandler.Scheme, _ => { });

            // Registered after Program.cs's own Configure/PostConfigure calls for this options type,
            // so this wins — the point of the test is to force each value of the flag deterministically,
            // independent of appsettings*.json or the COMMITMENTFEESETTINGS__ENABLED env var.
            services.PostConfigure<CommitmentFeeSettings>(o => o.Enabled = _commitmentFeeEnabled);

            var hosted = services.Where(d => d.ServiceType == typeof(IHostedService)).ToList();
            foreach (var h in hosted) services.Remove(h);
        });
    }
}

/// <summary>Always authenticates as a fixed client principal — no role/id variation needed for this suite.</summary>
internal sealed class SingleClientAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string Scheme = "SingleClientTestAuth";

    public SingleClientAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override System.Threading.Tasks.Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "test-client-id"),
            new Claim(ClaimTypes.Role, "Client"),
        };
        var identity = new ClaimsIdentity(claims, Scheme, ClaimTypes.Name, ClaimTypes.Role);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme);
        return System.Threading.Tasks.Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

public class BookingCommitmentInitiateGateTests
{
    private static System.Net.Http.HttpClient ClientWith(bool commitmentFeeEnabled)
    {
        var factory = new BookingCommitmentGateHostFactory(commitmentFeeEnabled);
        return factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
    }

    // Deliberately empty/invalid — with the gate not short-circuiting, this fails the
    // service's own validation ("GigId is required.") before ever reaching Flutterwave.
    private static readonly object EmptyRequest = new { gigId = "", email = "", redirectUrl = "" };

    [Fact]
    public async Task Disabled_RejectsBeforeReachingPaymentLogic_403()
    {
        var res = await ClientWith(commitmentFeeEnabled: false)
            .PostAsJsonAsync("/api/booking-commitment/initiate", EmptyRequest);

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);

        var raw = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(raw);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        var message = doc.RootElement.GetProperty("message").GetString() ?? "";
        Assert.Contains("disabled", message, StringComparison.OrdinalIgnoreCase);

        // Proves the gate stopped the request before the pre-existing validation ran —
        // it must NOT be the old "GigId is required." message.
        Assert.DoesNotContain("GigId", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Enabled_BehaviorIsUnchanged_SameValidationErrorAsBeforeTheGuard()
    {
        var res = await ClientWith(commitmentFeeEnabled: true)
            .PostAsJsonAsync("/api/booking-commitment/initiate", EmptyRequest);

        // Not blocked by the new guard — falls through to the pre-existing service validation,
        // exactly as it did before this change (400, not 403).
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);

        var raw = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(raw);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        var message = doc.RootElement.GetProperty("message").GetString() ?? "";
        Assert.Contains("GigId is required", message);
    }
}
