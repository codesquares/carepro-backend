using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using CarePro_Api;
using CloudinaryDotNet;
using Domain.Entities;
using Application.DTOs;
using Application.Interfaces.Email;
using Infrastructure.Content.Data;
using Infrastructure.Content.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using MongoDB.Bson;
using MongoDB.EntityFrameworkCore.Extensions;
using Xunit;

namespace CommitmentGate.Tests;

/// <summary>Records every Cloudinary call certificates make; never talks to the network.</summary>
public sealed class FakeCertCloudinary : CloudinaryService
{
    public FakeCertCloudinary() : base(new Cloudinary(new Account("c", "k", "s")),
        Options.Create(new CloudinarySettings { CloudName = "c", ApiKey = "k", ApiSecret = "s" })) { }

    public List<string> Uploaded { get; } = new();
    public List<string> Deleted { get; } = new();

    public override Task<(string Url, string PublicId)> UploadCertificateAsync(byte[] imageBytes, string fileName)
    {
        var id = $"certificates/fake-{Guid.NewGuid():N}";
        Uploaded.Add(id);
        return Task.FromResult(($"https://res.cloudinary.com/c/image/upload/v1/{id}.png", id));
    }

    public override Task<bool> DeleteFileAsync(string publicId)
    {
        Deleted.Add(publicId);
        return Task.FromResult(true);
    }
}

public sealed class CertificateHostFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"carepro_cert_http_{Guid.NewGuid():N}";
    public FakeCertCloudinary Cloud { get; } = new();
    public Mock<IEmailService> Email { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
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
            services.AddDbContext<CareProDbContext>(o => o.UseMongoDB("mongodb://127.0.0.1:27018", _dbName));

            services.RemoveAll<CloudinaryService>();
            services.AddSingleton<CloudinaryService>(Cloud);
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

    public CareProDbContext Db() => PoliceClearanceTestSupport.CreateDb(_dbName);

    public Caregiver SeedCaregiver()
    {
        using var db = Db();
        return PoliceClearanceTestSupport.SeedCaregiver(db, "CERTTEST", "Owner");
    }

    public Certification SeedCertificate(string caregiverId, string name = "CPR Certification", string publicId = "certificates/seed",
        DocumentVerificationStatus status = DocumentVerificationStatus.ManualReviewRequired, string? url = null)
    {
        using var db = Db();
        var cert = new Certification
        {
            Id = ObjectId.GenerateNewId(), CaregiverId = caregiverId, CertificateName = name, CertificateIssuer = "x",
            CloudinaryPublicId = publicId, CloudinaryUrl = url ?? $"https://res.cloudinary.com/c/image/upload/v1/{publicId}.png",
            VerificationStatus = status, SubmittedOn = DateTime.UtcNow, YearObtained = new DateTime(2024, 1, 1)
        };
        db.Certifications.Add(cert);
        db.SaveChanges();
        return cert;
    }

    public static readonly byte[] PngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D, 1, 2, 3, 4 };

    public static MultipartFormDataContent UploadForm(string? caregiverId, byte[]? bytes = null, string fileName = "c.png",
        string contentType = "image/png", string certName = "First Aid Training Certificate")
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(certName), "CertificateName" },
            { new StringContent("Any Issuer"), "CertificateIssuer" },
            { new StringContent("2024-01-01T00:00:00Z"), "YearObtained" },
        };
        if (caregiverId != null) form.Add(new StringContent(caregiverId), "CaregiverId");
        var part = new ByteArrayContent(bytes ?? PngBytes);
        part.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(part, "Certificate", fileName);
        return form;
    }
}

/// <summary>
/// api/Certificates used to trust a caregiverId from the body/query and let any caregiver or any admin read or
/// delete any certificate. Identity now comes from the JWT; admins need the Operations policy (HR, ComplianceAndLegal,
/// CareLeads, SuperAdmin) the vetting/review routes already use.
/// </summary>
public class CertificateRoutesOwnershipTests : IClassFixture<CertificateHostFactory>
{
    private readonly CertificateHostFactory _f;
    public CertificateRoutesOwnershipTests(CertificateHostFactory f) => _f = f;

    private (string ownerId, string otherId, string certId) Seed()
    {
        var owner = _f.SeedCaregiver().Id.ToString();
        var other = _f.SeedCaregiver().Id.ToString();
        var cert = _f.SeedCertificate(owner).Id.ToString();
        return (owner, other, cert);
    }

    // ---------- GET list ----------
    [Theory]
    [InlineData("owner", HttpStatusCode.OK)]
    [InlineData("other", HttpStatusCode.Forbidden)]
    [InlineData("hr", HttpStatusCode.OK)]
    [InlineData("careleads", HttpStatusCode.OK)]
    [InlineData("compliance", HttpStatusCode.OK)]
    [InlineData("finance", HttpStatusCode.Forbidden)]
    [InlineData("marketing", HttpStatusCode.Forbidden)]
    [InlineData("anon", HttpStatusCode.Unauthorized)]
    public async Task List_Matrix(string who, HttpStatusCode expected)
    {
        var (owner, other, _) = Seed();
        var res = await As(who, owner, other).GetAsync($"/api/Certificates?caregiverId={owner}");
        Assert.Equal(expected, res.StatusCode);
    }

    [Fact]
    public async Task List_CaregiverOmittingId_GetsOwnOnly()
    {
        var (owner, other, _) = Seed();
        _f.SeedCertificate(other, "Dementia Care Certificate");
        var res = await As("owner", owner, other).GetAsync("/api/Certificates");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.All(doc.RootElement.GetProperty("data").EnumerateArray(), e => Assert.Equal(owner, e.GetProperty("caregiverId").GetString()));
    }

    // ---------- GET by id ----------
    [Theory]
    [InlineData("owner", HttpStatusCode.OK)]
    [InlineData("other", HttpStatusCode.NotFound)]   // same as a missing id: existence is not revealed
    [InlineData("hr", HttpStatusCode.OK)]
    [InlineData("finance", HttpStatusCode.Forbidden)]
    [InlineData("anon", HttpStatusCode.Unauthorized)]
    public async Task GetById_Matrix(string who, HttpStatusCode expected)
    {
        var (owner, other, cert) = Seed();
        var res = await As(who, owner, other).GetAsync($"/api/Certificates/{cert}");
        Assert.Equal(expected, res.StatusCode);
    }

    [Fact]
    public async Task GetById_NonOwnerBodyIsIdenticalToMissingId()
    {
        var (owner, other, cert) = Seed();
        var missing = ObjectId.GenerateNewId().ToString();
        var a = await (await As("other", owner, other).GetAsync($"/api/Certificates/{cert}")).Content.ReadAsStringAsync();
        var b = await (await As("other", owner, other).GetAsync($"/api/Certificates/{missing}")).Content.ReadAsStringAsync();
        Assert.Equal(a.Replace(cert, "X"), b.Replace(missing, "X"));
    }

    // ---------- DELETE ----------
    [Theory]
    [InlineData("owner", HttpStatusCode.OK, true)]
    [InlineData("other", HttpStatusCode.NotFound, false)]
    [InlineData("hr", HttpStatusCode.OK, true)]
    [InlineData("finance", HttpStatusCode.Forbidden, false)]
    [InlineData("anon", HttpStatusCode.Unauthorized, false)]
    public async Task Delete_Matrix(string who, HttpStatusCode expected, bool shouldBeGone)
    {
        var (owner, other, cert) = Seed();
        var res = await As(who, owner, other).DeleteAsync($"/api/Certificates/{cert}");
        Assert.Equal(expected, res.StatusCode);
        using var db = _f.Db();
        Assert.Equal(shouldBeGone, !await db.Certifications.AnyAsync(c => c.Id == ObjectId.Parse(cert)));
    }

    // ---------- POST ----------
    [Theory]
    [InlineData("owner", HttpStatusCode.OK)]
    [InlineData("hr", HttpStatusCode.Forbidden)]       // admins do not upload through the caregiver route
    [InlineData("finance", HttpStatusCode.Forbidden)]
    [InlineData("anon", HttpStatusCode.Unauthorized)]
    public async Task Post_Matrix(string who, HttpStatusCode expected)
    {
        var (owner, other, _) = Seed();
        var res = await As(who, owner, other).PostAsync("/api/Certificates", CertificateHostFactory.UploadForm(owner));
        Assert.Equal(expected, res.StatusCode);
    }

    [Fact]
    public async Task Post_BodyIdOfAnotherCaregiver_Is403_AndNothingIsStored()
    {
        var (owner, other, _) = Seed();
        var before = _f.Cloud.Uploaded.Count;
        var res = await As("other", owner, other).PostAsync("/api/Certificates", CertificateHostFactory.UploadForm(owner));
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal(before, _f.Cloud.Uploaded.Count);
        using var db = _f.Db();
        Assert.False(await db.Certifications.AnyAsync(c => c.CaregiverId == owner && c.CertificateName == "First Aid Training Certificate"));
    }

    [Fact]
    public async Task Post_OmittedBodyId_StoresUnderTheJwtCaregiver()
    {
        var (owner, other, _) = Seed();
        var res = await As("owner", owner, other).PostAsync("/api/Certificates", CertificateHostFactory.UploadForm(null));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var db = _f.Db();
        Assert.True(await db.Certifications.AnyAsync(c => c.CaregiverId == owner && c.CertificateName == "First Aid Training Certificate"));
    }

    [Fact]
    public async Task Post_JsonBodyIdOfAnotherCaregiver_Is403()
    {
        var (owner, other, _) = Seed();
        var res = await As("other", owner, other).PostAsync("/api/Certificates",
            JsonBody(new { CaregiverId = owner, CertificateName = "First Aid Training Certificate", CertificateIssuer = "x", Certificate = "AAAA" }));
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    private static StringContent JsonBody(object o) => new(JsonSerializer.Serialize(o), System.Text.Encoding.UTF8, "application/json");

    private HttpClient As(string who, string ownerId, string otherId) => who switch
    {
        "owner" => _f.Client("Caregiver", ownerId),
        "other" => _f.Client("Caregiver", otherId),
        "hr" => _f.Client("Admin:HR", "admin-hr"),
        "careleads" => _f.Client("Admin:CareLeads", "admin-cl"),
        "compliance" => _f.Client("Admin:ComplianceAndLegal", "admin-cal"),
        "finance" => _f.Client("Admin:Finance", "admin-fin"),
        "marketing" => _f.Client("Admin:MarketingAndSales", "admin-mkt"),
        _ => _f.Client(),
    };
}
