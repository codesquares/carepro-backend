using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Application.Interfaces.Content;
using Application.Interfaces.Email;
using CarePro_Api;
using Infrastructure.Content.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MongoDB.EntityFrameworkCore.Extensions;

namespace CommitmentGate.Tests;

/// <summary>In-memory stand-in for private storage. Records every call so tests can prove files are created/deleted.</summary>
public sealed class FakeClearanceFileStore : IPoliceClearanceFileStore
{
    public ConcurrentDictionary<string, byte[]> Files { get; } = new();
    public List<string> Deleted { get; } = new();
    public bool FailUploads { get; set; }
    public bool FailDeletes { get; set; }

    public Task<StoredClearanceFile> UploadAsync(byte[] bytes, string extension)
    {
        if (FailUploads) throw new InvalidOperationException("storage down");
        var id = $"fake/{Guid.NewGuid():N}{extension}";
        Files[id] = bytes;
        return Task.FromResult(new StoredClearanceFile(id, "raw"));
    }

    public Task<byte[]?> DownloadAsync(string publicId, string? resourceType)
        => Task.FromResult(Files.TryGetValue(publicId, out var b) ? b : null);

    public Task<ClearanceFileDeleteOutcome> DeleteAsync(string publicId, string? resourceType)
    {
        if (FailDeletes) return Task.FromResult(ClearanceFileDeleteOutcome.Failed);
        Deleted.Add(publicId);
        return Task.FromResult(Files.TryRemove(publicId, out _) ? ClearanceFileDeleteOutcome.Deleted : ClearanceFileDeleteOutcome.NotFound);
    }
}

public sealed class FakeClock : TimeProvider
{
    public DateTime UtcNowValue { get; set; } = DateTime.UtcNow;
    public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(UtcNowValue, DateTimeKind.Utc));
}

/// <summary>
/// Header-driven principals: X-Test-Role = Caregiver | SuperAdmin | Admin:&lt;Department&gt;; X-Test-UserId / X-Test-Email choose the identity.
/// No X-Test-Role header = unauthenticated (401).
/// </summary>
internal sealed class ClearanceHeaderAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string Scheme = "ClearanceHeaderAuth";
    public ClearanceHeaderAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> o, ILoggerFactory l, UrlEncoder e) : base(o, l, e) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-Role", out var raw) || string.IsNullOrWhiteSpace(raw))
            return Task.FromResult(AuthenticateResult.NoResult());
        var value = raw.ToString();
        var userId = Request.Headers.TryGetValue("X-Test-UserId", out var uid) ? uid.ToString() : "test-user-id";
        var email = Request.Headers.TryGetValue("X-Test-Email", out var em) ? em.ToString() : "user@carepro.test";
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId), new("userId", userId), new(ClaimTypes.Email, email)
        };
        if (value.StartsWith("Admin:", StringComparison.Ordinal))
        {
            claims.Add(new Claim(ClaimTypes.Role, "Admin"));
            claims.Add(new Claim("department", value["Admin:".Length..]));
        }
        else claims.Add(new Claim(ClaimTypes.Role, value));
        var identity = new ClaimsIdentity(claims, Scheme, ClaimTypes.Name, ClaimTypes.Role);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme)));
    }
}

public sealed class ClearanceHostFactory : WebApplicationFactory<Program>
{
    public string DbName { get; } = $"carepro_clearance_http_{Guid.NewGuid():N}";
    public FakeClearanceFileStore Store { get; } = new();
    public Mock<IEmailService> Email { get; } = new();
    public Dictionary<string, string?> ExtraConfig { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(ExtraConfig));
        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(o =>
            {
                o.DefaultAuthenticateScheme = ClearanceHeaderAuthHandler.Scheme;
                o.DefaultChallengeScheme = ClearanceHeaderAuthHandler.Scheme;
            }).AddScheme<AuthenticationSchemeOptions, ClearanceHeaderAuthHandler>(ClearanceHeaderAuthHandler.Scheme, _ => { });

            services.RemoveAll<DbContextOptions<CareProDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<CareProDbContext>();
            services.AddDbContext<CareProDbContext>(o => o.UseMongoDB("mongodb://127.0.0.1:27018", DbName));

            services.RemoveAll<IPoliceClearanceFileStore>();
            services.AddSingleton<IPoliceClearanceFileStore>(Store);
            services.RemoveAll<IEmailService>();
            services.AddSingleton(Email.Object);

            foreach (var h in services.Where(d => d.ServiceType == typeof(IHostedService)).ToList()) services.Remove(h);
        });
    }

    public HttpClient Client(string? role = null, string? userId = null, string? email = null)
    {
        var c = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        if (role != null) c.DefaultRequestHeaders.Add("X-Test-Role", role);
        if (userId != null) c.DefaultRequestHeaders.Add("X-Test-UserId", userId);
        if (email != null) c.DefaultRequestHeaders.Add("X-Test-Email", email);
        return c;
    }

    public CareProDbContext Db() => PoliceClearanceTestSupport.CreateDb(DbName);

    // ── file builders ──
    public static byte[] PdfBytes(string extra = "") => System.Text.Encoding.Latin1.GetBytes("%PDF-1.4\n1 0 obj<<>>endobj\n" + extra + "\n%%EOF");
    public static byte[] JpgBytes() => new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46, 0x49, 0x46, 0, 1, 2, 3 };

    public static MultipartFormDataContent Form(string issueDate, string name = "CLEARANCETEST Subject", string reference = "NPF-12345",
        IEnumerable<(string fileName, string contentType, byte[] bytes)>? files = null, Dictionary<string, string>? extraFields = null)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(issueDate), "issueDate" },
            { new StringContent(name), "nameOnDocument" },
            { new StringContent(reference), "referenceNumber" }
        };
        foreach (var (fileName, contentType, bytes) in files ?? new[] { ("clearance.pdf", "application/pdf", PdfBytes()) })
        {
            var part = new ByteArrayContent(bytes);
            part.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            form.Add(part, "files", fileName);
        }
        foreach (var kv in extraFields ?? new()) form.Add(new StringContent(kv.Value), kv.Key);
        return form;
    }

    public static string DateAgo(int days) => DateTime.UtcNow.AddHours(1).Date.AddDays(-days).ToString("yyyy-MM-dd");
}
