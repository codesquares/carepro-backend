using Application.Interfaces.Authentication;
using Application.Interfaces.Email;
using Domain.Settings;
using Infrastructure.Content.Helpers;
using Infrastructure.Content.Services;
using Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Moq;
using Xunit;

namespace CommitmentGate.Tests;

/// <summary>Notice content (§4) and the local mail sink: rendered bodies, real line breaks, Lagos deadline, conditional clearance text.</summary>
public class VettingNoticeContentTests
{
    private static readonly DateTime Deadline = new(2026, 10, 18, 23, 0, 0, DateTimeKind.Utc); // = Mon 19 Oct 2026 00:00 WAT
    private const string Url = "http://localhost:5173/app/caregiver/vetting";
    private static VettingNoticeMessage Msg(string type, params string[] missing) =>
        VettingNoticeContent.Build(type, "Ngozi", missing, Deadline, null, 6, Url);

    [Fact]
    public void ThreeSteps_RenderedBodies_ListOnlyMissing_LagosDeadline_NoClearance()
    {
        var m = Msg("initial", VettingSteps.Classification, VettingSteps.Guarantors, VettingSteps.AddressHistory);
        Console.WriteLine("[evidence] SUBJECT: " + m.Subject + "\n[evidence] ---- TEXT ----\n" + m.Text + "[evidence] ---- HTML ----\n" + m.Html + "\n[evidence] ---- IN-APP ----\n" + m.InAppTitle + " :: " + m.InAppContent);
        Assert.Equal("Action needed: complete your CarePro vetting by 19 October 2026", m.Subject);
        Assert.Contains("Dear Ngozi,", m.Text);
        Assert.Contains("by Monday, 19 October 2026 at 12:00 AM WAT".Replace("by ", ""), m.Text);
        Assert.Contains("1. Set your caregiver type.", m.Text);
        Assert.Contains("2. Add two guarantors. They only need to be added now; their confirmation can come later.", m.Text);
        Assert.Contains("3. Complete your address history.", m.Text);
        Assert.DoesNotContain("police", m.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("police", m.Html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("only be able to open the vetting steps, your wallet and withdrawals, settings, messages and notifications", m.Text);
        Assert.Contains(Url, m.Text); Assert.Contains($"href=\"{Url}\"", m.Html);
        Assert.Contains("support@oncarepro.com", m.Text);
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(m.Html, "<a href=\"http").Count);   // exactly one link (+ the mailto)
        Assert.DoesNotContain("unsubscribe", m.Html + m.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("earnings will", m.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RealLineBreaks_NeverLiteralBackslashN()
    {
        var m = Msg("initial", VettingSteps.Classification, VettingSteps.Guarantors);
        Assert.Contains("\n", m.Text);
        foreach (var body in new[] { m.Text, m.Html, m.InAppContent }) Assert.DoesNotContain("\\n", body);
        Assert.DoesNotContain("\n", m.Html.Replace("\n", "") + "x");  // html is markup, no reliance on newlines
    }

    [Fact]
    public void Clearance_Mentioned_OnlyWhenMissing_WithAgeAndOwnDeadlineWhenDifferent()
    {
        var withClr = VettingNoticeContent.Build("initial", "Ngozi", new[] { VettingSteps.PoliceClearance }, Deadline, Deadline.AddDays(14), 6, Url);
        Assert.Contains("Submit your police clearance", withClr.Text);
        Assert.Contains("Nigeria Police Force", withClr.Text);
        Assert.Contains("within the last 6 months", withClr.Text);
        Assert.Contains("needed by", withClr.Text);
        var same = VettingNoticeContent.Build("initial", "Ngozi", new[] { VettingSteps.PoliceClearance }, Deadline, Deadline, 6, Url);
        Assert.DoesNotContain("needed by", same.Text);
    }

    [Fact]
    public void Reminders_HaveOwnSubjectAndIntro()
    {
        Assert.Equal("Reminder: 7 days left to complete your CarePro vetting", Msg("reminder_7d", VettingSteps.Guarantors).Subject);
        Assert.Contains("1 day left", Msg("reminder_1d", VettingSteps.Guarantors).Text);
    }

    [Fact]
    public void FirstName_IsHtmlEncoded_AndBlankFallsBack()
    {
        var m = VettingNoticeContent.Build("initial", "<b>x</b>", new[] { VettingSteps.Guarantors }, Deadline, null, 6, Url);
        Assert.DoesNotContain("<b>x</b>", m.Html); Assert.Contains("&lt;b&gt;", m.Html);
        Assert.Contains("Dear there,", VettingNoticeContent.Build("initial", " ", new[] { VettingSteps.Guarantors }, Deadline, null, 6, Url).Text);
    }

    [Fact]
    public async Task EmailService_WithSink_WritesMultipartEml_AndNeverTouchesSmtp()
    {
        var dir = Path.Combine(Path.GetTempPath(), "carepro-sink-" + Guid.NewGuid().ToString("N"));
        var settings = new MailSettings { FromEmail = "notifications@oncarepro.test", FromName = "CarePro", SmtpServer = "smtp.invalid", SmtpPort = 1, AppPassword = "x", SinkDirectory = dir }; // pragma: allowlist-secret
        var svc = new EmailService(Options.Create(settings), PoliceClearanceTestSupport.CreateDb(PoliceClearanceTestSupport.NewDbName()), Mock.Of<ITokenHandler>(),
            Mock.Of<ILogger<EmailService>>(), Mock.Of<IEmailNotificationTrackingService>());
        var m = Msg("initial", VettingSteps.Guarantors);
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        await svc.SendVettingNoticeEmailAsync("NOTICETEST@example.com", m.Subject, m.Html, m.Text);   // smtp.invalid:1 would throw if SMTP were used
        var eml = Directory.GetFiles(dir, "*.eml").Single();
        var parsed = MimeMessage.Load(eml);
        Assert.Equal("NOTICETEST@example.com", parsed.To.Mailboxes.Single().Address);
        Assert.Equal(m.Subject, parsed.Subject);
        Assert.Equal(m.Text.Replace("\n", "\r\n").TrimEnd(), parsed.TextBody.Replace("\n", "\r\n").TrimEnd().Replace("\r\r", "\r"), ignoreCase: false);
        Assert.Contains("Open my vetting steps", parsed.HtmlBody);
        Assert.Null(parsed.Headers["List-Unsubscribe"]);                                                // Always-Send: no unsubscribe header
        Directory.Delete(dir, true);
    }
}
