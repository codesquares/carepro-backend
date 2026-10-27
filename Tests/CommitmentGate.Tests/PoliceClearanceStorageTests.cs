using Infrastructure.Content.Helpers;
using Infrastructure.Content.Services;
using Application.Interfaces.Content;
using CloudinaryDotNet;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using System.Net;
using System.Text;
using Xunit;

namespace CommitmentGate.Tests;

/// <summary>Item 3: file validation (content type + magic bytes), settings parsing, and an opt-in live Cloudinary check.</summary>
public class PoliceClearanceStorageTests
{
    private static readonly PoliceClearanceSettings Settings = new();
    private static byte[] Pdf(string extra = "") => Encoding.Latin1.GetBytes("%PDF-1.4\n1 0 obj<<>>endobj\n" + extra + "\n%%EOF");
    private static byte[] Jpg() => new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46, 0x49, 0x46, 0, 1 };
    private static byte[] Png() => new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 };
    private static IncomingClearanceFile F(string name, string ct, byte[] b) => new(name, ct, b);

    [Fact]
    public void SinglePdf_Valid() =>
        Assert.True(PoliceClearanceFileValidator.Validate(new[] { F("c.pdf", "application/pdf", Pdf()) }, Settings).IsValid);

    [Fact]
    public void UpToFiveImages_Valid_SixRejected()
    {
        var five = Enumerable.Range(0, 5).Select(i => F($"{i}.jpg", "image/jpeg", Jpg())).ToList();
        Assert.True(PoliceClearanceFileValidator.Validate(five, Settings).IsValid);
        five.Add(F("6.png", "image/png", Png()));
        var r = PoliceClearanceFileValidator.Validate(five, Settings);
        Assert.False(r.IsValid);
        Assert.Contains("at most 5", r.Error);
    }

    [Fact]
    public void RenamedExecutable_AsPdf_Rejected_ByMagicBytes()
    {
        var exe = Encoding.ASCII.GetBytes("MZ\x90\0\x03\0\0\0 this is not a pdf");
        var r = PoliceClearanceFileValidator.Validate(new[] { F("clearance.pdf", "application/pdf", exe) }, Settings);
        Assert.False(r.IsValid);
        Assert.Contains("isn't a valid PDF", r.Error);
    }

    [Fact]
    public void DeclaredTypeMustMatchActualBytes()
    {
        // PNG bytes claimed as JPEG; PDF bytes claimed as PNG.
        Assert.False(PoliceClearanceFileValidator.Validate(new[] { F("a.jpg", "image/jpeg", Png()) }, Settings).IsValid);
        Assert.False(PoliceClearanceFileValidator.Validate(new[] { F("a.png", "image/png", Pdf()) }, Settings).IsValid);
    }

    [Theory]
    [InlineData("text/html")]
    [InlineData("application/x-msdownload")]
    [InlineData("")]
    [InlineData(null)]
    public void UnsupportedDeclaredType_Rejected(string? ct) =>
        Assert.False(PoliceClearanceFileValidator.Validate(new[] { F("a.pdf", ct, Pdf()) }, Settings).IsValid);

    [Fact]
    public void ExtensionIsNeverTrusted_PdfBytesNamedExe_StillAcceptedAsPdf_ButStoredWithPdfExtension()
    {
        var r = PoliceClearanceFileValidator.Validate(new[] { F("weird.exe", "application/pdf", Pdf()) }, Settings);
        Assert.True(r.IsValid);
        Assert.Equal(".pdf", r.Files[0].Extension);
    }

    [Fact]
    public void MixedPdfAndImage_OrTwoPdfs_Rejected()
    {
        Assert.False(PoliceClearanceFileValidator.Validate(new[] { F("a.pdf", "application/pdf", Pdf()), F("b.jpg", "image/jpeg", Jpg()) }, Settings).IsValid);
        Assert.False(PoliceClearanceFileValidator.Validate(new[] { F("a.pdf", "application/pdf", Pdf()), F("b.pdf", "application/pdf", Pdf()) }, Settings).IsValid);
    }

    [Fact]
    public void SizeLimits_PerFileAndTotal()
    {
        var small = new PoliceClearanceSettings { MaxFileSizeMB = 1, MaxTotalSizeMB = 2 };
        byte[] Big(int mb) { var b = new byte[mb * 1024 * 1024]; Jpg().CopyTo(b, 0); return b; }
        Assert.False(PoliceClearanceFileValidator.Validate(new[] { F("a.jpg", "image/jpeg", Big(2)) }, small).IsValid);   // one file too big
        var ok = Big(1); // exactly the per-file limit is allowed
        Assert.True(PoliceClearanceFileValidator.Validate(new[] { F("a.jpg", "image/jpeg", ok) }, small).IsValid);
        var three = new[] { F("a.jpg", "image/jpeg", ok), F("b.jpg", "image/jpeg", ok), F("c.jpg", "image/jpeg", ok) };
        var total = PoliceClearanceFileValidator.Validate(three, small);                                                    // 3 MB > 2 MB total
        Assert.False(total.IsValid);
        Assert.Contains("together", total.Error);
    }

    [Fact]
    public void EmptyFile_AndNoFiles_Rejected()
    {
        Assert.False(PoliceClearanceFileValidator.Validate(new[] { F("a.pdf", "application/pdf", Array.Empty<byte>()) }, Settings).IsValid);
        Assert.False(PoliceClearanceFileValidator.Validate(Array.Empty<IncomingClearanceFile>(), Settings).IsValid);
    }

    [Fact]
    public void PdfWithActiveContent_Rejected() =>
        Assert.False(PoliceClearanceFileValidator.Validate(new[] { F("a.pdf", "application/pdf", Pdf("/OpenAction << /S /JavaScript /JS (app.alert(1)) >>")) }, Settings).IsValid);

    [Fact]
    public void FileName_IsSanitised()
    {
        var r = PoliceClearanceFileValidator.Validate(new[] { F("../../etc/pass\0wd.pdf", "application/pdf", Pdf()) }, Settings);
        Assert.True(r.IsValid);
        Assert.DoesNotContain("/", r.Files[0].SafeFileName);
        Assert.False(r.Files[0].SafeFileName.Contains('\0'));
        Assert.Equal("passwd.pdf", r.Files[0].SafeFileName);
    }

    private static IConfiguration Cfg(params (string k, string? v)[] kv) =>
        new ConfigurationBuilder().AddInMemoryCollection(kv.ToDictionary(x => x.k, x => x.v)).Build();

    [Fact]
    public void Settings_Defaults_AndEnforcementFailOpen()
    {
        var d = PoliceClearanceSettings.Load(Cfg());
        Assert.Equal((6, 10, 20, 5), (d.MaxAgeMonths, d.MaxFileSizeMB, d.MaxTotalSizeMB, d.MaxFiles));
        Assert.False(d.IsEnforced(DateTime.UtcNow));                       // unset = not enforced
        Assert.False(PoliceClearanceSettings.Load(Cfg(("PoliceClearance:EnforcementDate", ""))).IsEnforced(DateTime.UtcNow));
        Assert.False(PoliceClearanceSettings.Load(Cfg(("PoliceClearance:EnforcementDate", "not a date"))).IsEnforced(DateTime.UtcNow)); // unparseable = not enforced
        Assert.False(PoliceClearanceSettings.Load(null).IsEnforced(DateTime.UtcNow));
    }

    [Fact]
    public void Settings_EnforcementDate_BoundaryIsInclusive_AndBadNumbersFallBack()
    {
        var c = PoliceClearanceSettings.Load(Cfg(("PoliceClearance:EnforcementDate", "2026-12-01T00:00:00Z"), ("PoliceClearance:MaxAgeMonths", "-3"), ("PoliceClearance:MaxFiles", "x")));
        Assert.False(c.IsEnforced(new DateTime(2026, 11, 30, 23, 59, 59, DateTimeKind.Utc)));
        Assert.True(c.IsEnforced(new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(6, c.MaxAgeMonths);
        Assert.Equal(5, c.MaxFiles);
    }

    // Opt-in live check against the real Cloudinary account configured in appsettings.Development.json:
    //   CLEARANCE_LIVE_CLOUDINARY=1 dotnet test --filter PoliceClearanceStorageTests.Live
    [Fact]
    public async Task Live_Cloudinary_AuthenticatedUpload_IsNotAnonymouslyFetchable_ButBackendCanDownloadAndDelete()
    {
        if (Environment.GetEnvironmentVariable("CLEARANCE_LIVE_CLOUDINARY") != "1") return;

        var dev = new ConfigurationBuilder().AddJsonFile(Path.Combine(AppContext.BaseDirectory, "../../../../../CarePro-Api/appsettings.Development.json")).Build();
        var cs = dev.GetSection("CloudinarySettings");
        var cloudinary = new Cloudinary(new Account(cs["CloudName"], cs["ApiKey"], cs["ApiSecret"])) { Api = { Secure = true } };
        var store = new CloudinaryPoliceClearanceFileStore(cloudinary, Cfg(("PoliceClearance:AssetTag", "CLEARANCETEST")), Mock.Of<ILogger<CloudinaryPoliceClearanceFileStore>>());

        var bytes = Pdf("CLEARANCETEST live fixture");
        var stored = await store.UploadAsync(bytes, ".pdf");
        try
        {
            // 1) Anonymous fetch of the would-be public delivery URLs must fail.
            using var http = new HttpClient();
            foreach (var kind in new[] { "authenticated", "upload" })
            {
                var url = $"https://res.cloudinary.com/{cs["CloudName"]}/raw/{kind}/{stored.PublicId}";
                var res = await http.GetAsync(url);
                Console.WriteLine($"[evidence] anonymous GET .../raw/{kind}/<publicId> -> {(int)res.StatusCode}");
                Assert.NotEqual(HttpStatusCode.OK, res.StatusCode);
            }
            // 2) The backend (signed, short-lived) can read it back byte-for-byte.
            var back = await store.DownloadAsync(stored.PublicId, stored.ResourceType);
            Assert.NotNull(back);
            Assert.Equal(bytes, back);
            Console.WriteLine($"[evidence] backend signed download -> {back!.Length} bytes, identical");
        }
        finally
        {
            var outcome = await store.DeleteAsync(stored.PublicId, stored.ResourceType);
            Console.WriteLine($"[evidence] delete outcome -> {outcome}");
            Assert.Equal(ClearanceFileDeleteOutcome.Deleted, outcome);
            Assert.Null(await store.DownloadAsync(stored.PublicId, stored.ResourceType));
        }
    }
}
