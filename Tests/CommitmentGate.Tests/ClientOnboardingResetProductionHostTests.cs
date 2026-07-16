using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using CarePro_Api;
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

[Collection("EnvironmentVariableIsolation")]
public class ClientOnboardingResetProductionHostTests
{
    [Fact]
    public async Task API_010_ResetEndpoint_ProductionEnvironmentVariable_RealHost_Returns404()
    {
        var previousAspNetcoreEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

        try
        {
            // Match deployment behavior: host environment comes from ASPNETCORE_ENVIRONMENT.
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");

            await using var factory = new ProductionHostFactory();
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });

            using var response = await client.PostAsync("/api/client-onboarding/state/reset", content: null);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Console.WriteLine($"API-010_RESET_PROD_ENV_HTTP_STATUS {(int)response.StatusCode}");
            Console.WriteLine($"API-010_RESET_PROD_ENV_EFFECTIVE_ENV {factory.EffectiveEnvironmentName}");
        }
        finally
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", previousAspNetcoreEnvironment);
        }
    }

    private sealed class ProductionHostFactory : WebApplicationFactory<Program>
    {
        public string? EffectiveEnvironmentName { get; private set; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthHandler.AuthenticationScheme;
                    options.DefaultChallengeScheme = TestAuthHandler.AuthenticationScheme;
                }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.AuthenticationScheme, _ => { });

                // Prevent long-running background services from starting in API integration tests.
                var hostedServices = services
                    .Where(d => d.ServiceType == typeof(IHostedService))
                    .ToList();

                foreach (var hostedService in hostedServices)
                {
                    services.Remove(hostedService);
                }
            });
        }

        protected override IHost CreateHost(IHostBuilder builder)
        {
            var host = base.CreateHost(builder);
            EffectiveEnvironmentName = host.Services.GetRequiredService<IHostEnvironment>().EnvironmentName;
            return host;
        }
    }

    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string AuthenticationScheme = "IntegrationTestAuth";

        public TestAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "integration-test-client"),
                new Claim(ClaimTypes.Role, "Client"),
                new Claim(ClaimTypes.Role, "QA"),
                new Claim("qa_access", "true")
            };

            var identity = new ClaimsIdentity(claims, AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, AuthenticationScheme);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}

[CollectionDefinition("EnvironmentVariableIsolation", DisableParallelization = true)]
public sealed class EnvironmentVariableIsolationCollection
{
}
