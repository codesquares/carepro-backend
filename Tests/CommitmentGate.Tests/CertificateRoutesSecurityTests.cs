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

/// <summary>Reviewer identity for certificate review comes from the JWT; an AdminId in the body is ignored.</summary>
public class CertificateReviewerIdentityTests : IClassFixture<CertificateHostFactory>
{
    private readonly CertificateHostFactory _f;
    public CertificateReviewerIdentityTests(CertificateHostFactory f) => _f = f;

    private static StringContent Json(object o) => new(JsonSerializer.Serialize(o), System.Text.Encoding.UTF8, "application/json");

    [Fact]
    public async Task Review_BodyAdminIdOfAnotherAdmin_IsIgnored_StoredReviewerIsTheJwtAdmin()
    {
        var cg = _f.SeedCaregiver().Id.ToString();
        var cert = _f.SeedCertificate(cg);
        var client = _f.Client("Admin:HR", "admin-jwt-id", "hr@carepro.test");

        var res = await client.PostAsync("/api/Admins/Certificates/Review",
            Json(new { certificateId = cert.Id.ToString(), adminId = "some-other-admin", approved = false, adminNotes = "Blurry scan." }));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var db = _f.Db();
        var stored = await db.Certifications.FirstAsync(c => c.Id == cert.Id);
        Assert.Equal("admin-jwt-id", stored.ReviewedByAdminId);
        Assert.NotNull(stored.ReviewedAt);
    }

    [Fact]
    public async Task Review_WithNoAdminIdInBody_Works()
    {
        var cg = _f.SeedCaregiver().Id.ToString();
        var cert = _f.SeedCertificate(cg);
        var res = await _f.Client("Admin:CareLeads", "admin-jwt-2").PostAsync("/api/Admins/Certificates/Review",
            Json(new { certificateId = cert.Id.ToString(), approved = true }));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var db = _f.Db();
        Assert.Equal("admin-jwt-2", (await db.Certifications.FirstAsync(c => c.Id == cert.Id)).ReviewedByAdminId);
    }

    [Theory]
    [InlineData("Admin:Finance", HttpStatusCode.Forbidden)]
    [InlineData("Caregiver", HttpStatusCode.Forbidden)]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    public async Task Review_OtherCallers_AreRejected(string? role, HttpStatusCode expected)
    {
        var cg = _f.SeedCaregiver().Id.ToString();
        var cert = _f.SeedCertificate(cg);
        var res = await _f.Client(role, "someone").PostAsync("/api/Admins/Certificates/Review",
            Json(new { certificateId = cert.Id.ToString(), adminId = "x", approved = true }));
        Assert.Equal(expected, res.StatusCode);
        using var db = _f.Db();
        Assert.Null((await db.Certifications.FirstAsync(c => c.Id == cert.Id)).ReviewedByAdminId);
    }
}

/// <summary>Every review path records reviewer / time / notes, returns the notes, and tells the caregiver WHY.</summary>
public class CertificateReviewNotesTests : IClassFixture<CertificateHostFactory>
{
    private readonly CertificateHostFactory _f;
    public CertificateReviewNotesTests(CertificateHostFactory f) => _f = f;

    private static StringContent Json(object o) => new(JsonSerializer.Serialize(o), System.Text.Encoding.UTF8, "application/json");
    private const string Reason = "Name <b>mismatch</b> & blurry\nSecond line";

    private (string cg, Certification cert) Seed()
    {
        var cg = _f.SeedCaregiver().Id.ToString();
        return (cg, _f.SeedCertificate(cg));
    }

    private async Task<Certification> Stored(ObjectId id) { using var db = _f.Db(); return await db.Certifications.FirstAsync(c => c.Id == id); }

    [Fact]
    public async Task ManualReject_Persists_Reviewer_Time_Reason()
    {
        var (_, cert) = Seed();
        var res = await _f.Client("Admin:HR", "hr-1", "hr@carepro.test")
            .PostAsync($"/api/Admins/Certificates/{cert.Id}/Reject", Json(new { adminId = "ignored", rejectionReason = Reason }));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var s = await Stored(cert.Id);
        Assert.Equal(DocumentVerificationStatus.Invalid, s.VerificationStatus);
        Assert.Equal("hr-1", s.ReviewedByAdminId);
        Assert.NotNull(s.ReviewedAt);
        Assert.Equal(Reason, s.AdminReviewNotes);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.Equal(Reason, doc.RootElement.GetProperty("certificate").GetProperty("adminReviewNotes").GetString());
    }

    [Fact]
    public async Task ManualApprove_Persists_Reviewer_Time_Notes()
    {
        var (_, cert) = Seed();
        var res = await _f.Client("Admin:CareLeads", "cl-1").PostAsync($"/api/Admins/Certificates/{cert.Id}/Approve",
            Json(new { adminId = "ignored", approvalNotes = "Checked with the issuer." }));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var s = await Stored(cert.Id);
        Assert.Equal(DocumentVerificationStatus.Verified, s.VerificationStatus);
        Assert.Equal("cl-1", s.ReviewedByAdminId);
        Assert.NotNull(s.ReviewedAt);
        Assert.Equal("Checked with the issuer.", s.AdminReviewNotes);
    }

    [Fact]
    public async Task Review_Persists_Reviewer_Time_Notes()
    {
        var (_, cert) = Seed();
        var res = await _f.Client("Admin:HR", "hr-2").PostAsync("/api/Admins/Certificates/Review",
            Json(new { certificateId = cert.Id.ToString(), approved = false, adminNotes = "Expired." }));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var s = await Stored(cert.Id);
        Assert.Equal(("hr-2", "Expired."), (s.ReviewedByAdminId, s.AdminReviewNotes));
        Assert.NotNull(s.ReviewedAt);
    }

    [Fact]
    public async Task Caregiver_SeesRejectionReason_OnList_AndGetById_ButNotApprovalNotes()
    {
        var (cg, rejected) = Seed();
        var approved = _f.SeedCertificate(cg, "Dementia Care Certificate");
        var hr = _f.Client("Admin:HR", "hr-3");
        await hr.PostAsync($"/api/Admins/Certificates/{rejected.Id}/Reject", Json(new { rejectionReason = "Photo is cropped." }));
        await hr.PostAsync($"/api/Admins/Certificates/{approved.Id}/Approve", Json(new { approvalNotes = "internal: called the issuer" }));

        var caregiver = _f.Client("Caregiver", cg);
        using var list = JsonDocument.Parse(await caregiver.GetStringAsync($"/api/Certificates?caregiverId={cg}"));
        var rows = list.RootElement.GetProperty("data").EnumerateArray().ToList();
        Assert.Equal("Photo is cropped.", rows.Single(r => r.GetProperty("id").GetString() == rejected.Id.ToString()).GetProperty("adminReviewNotes").GetString());
        Assert.Equal(JsonValueKind.Null, rows.Single(r => r.GetProperty("id").GetString() == approved.Id.ToString()).GetProperty("adminReviewNotes").ValueKind);

        using var one = JsonDocument.Parse(await caregiver.GetStringAsync($"/api/Certificates/{rejected.Id}"));
        Assert.Equal("Photo is cropped.", one.RootElement.GetProperty("data").GetProperty("adminReviewNotes").GetString());
    }

    [Fact]
    public async Task Admin_Details_ReturnsNotes_ForApprovalToo()
    {
        var (_, cert) = Seed();
        var hr = _f.Client("Admin:HR", "hr-4");
        await hr.PostAsync($"/api/Admins/Certificates/{cert.Id}/Approve", Json(new { approvalNotes = "internal note" }));
        using var doc = JsonDocument.Parse(await hr.GetStringAsync($"/api/Admins/Certificates/{cert.Id}/Details"));
        Assert.Equal("internal note", doc.RootElement.GetProperty("data").GetProperty("adminReviewNotes").GetString());
    }

    [Fact]
    public async Task Rejection_InAppNotification_CarriesTheReason_AndEmailUsesBrAndEncodesIt()
    {
        var (cg, cert) = Seed();
        var sent = new List<string>();
        _f.Email.Setup(e => e.SendGenericNotificationEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Callback<string, string, string, string, bool, string?, string?>((_, _, _, content, _, _, _) => { lock (sent) sent.Add(content); })
            .Returns(Task.CompletedTask);

        await _f.Client("Admin:HR", "hr-5").PostAsync($"/api/Admins/Certificates/{cert.Id}/Reject", Json(new { rejectionReason = Reason }));

        using var db = _f.Db();
        var note = await db.Notifications.FirstAsync(n => n.RecipientId == cg && n.RelatedEntityId == cert.Id.ToString());
        Assert.Contains("Reason: " + Reason, note.Content);

        var email = sent.Last();
        Assert.Contains("<br />", email);
        Assert.DoesNotContain("\\n", email);
        Assert.DoesNotContain("\n", email);
        Assert.Contains("Name &lt;b&gt;mismatch&lt;/b&gt; &amp; blurry<br />Second line", email);
        Assert.DoesNotContain("<b>mismatch</b>", email);
    }

    [Fact]
    public async Task ReviewPath_RejectionEmail_HasNoLiteralBackslashN_AndCarriesTheReason()
    {
        var (cg, cert) = Seed();
        var sent = new List<string>();
        _f.Email.Setup(e => e.SendGenericNotificationEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Callback<string, string, string, string, bool, string?, string?>((_, _, _, content, _, _, _) => { lock (sent) sent.Add(content); })
            .Returns(Task.CompletedTask);

        await _f.Client("Admin:HR", "hr-6").PostAsync("/api/Admins/Certificates/Review",
            Json(new { certificateId = cert.Id.ToString(), approved = false, adminNotes = "Certificate is for a different person." }));

        var email = sent.Last();
        Assert.DoesNotContain("\\n", email);
        Assert.Contains("Certificate is for a different person.", email);
        using var db = _f.Db();
        var note = await db.Notifications.FirstAsync(n => n.RecipientId == cg && n.RelatedEntityId == cert.Id.ToString());
        Assert.Contains("Reason: Certificate is for a different person.", note.Content);
    }
}
