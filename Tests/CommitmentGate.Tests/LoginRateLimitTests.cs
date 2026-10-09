using System.Net;
using System.Text;
using CarePro_Api.Middleware;
using CarePro_Api.Middleware.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace CommitmentGate.Tests;

/// <summary>Login limiting: per-IP ceiling + per-email FAILURE limit; success resets; refresh has its own bucket.</summary>
public class LoginRateLimitTests
{
    // Each test uses unique IPs/emails because the in-memory store is process-wide (static).
    private static string Uniq() => Guid.NewGuid().ToString("N")[..8];

    private static RateLimitingMiddleware Build(Func<HttpContext, int> statusFor, out List<string> bodiesSeen, RateLimitOptions? opts = null)
    {
        var seen = new List<string>(); bodiesSeen = seen;
        RequestDelegate next = async ctx =>
        {
            // the controller must still be able to read the body after the middleware peeked at it
            using var r = new StreamReader(ctx.Request.Body, Encoding.UTF8, false, 1024, leaveOpen: true);
            seen.Add(await r.ReadToEndAsync());
            ctx.Response.StatusCode = statusFor(ctx);
        };
        return new RateLimitingMiddleware(next, Mock.Of<ILogger<RateLimitingMiddleware>>(), new InMemoryRateLimitStore(), Options.Create(opts ?? new RateLimitOptions()));
    }

    private static async Task<int> Login(RateLimitingMiddleware mw, string ip, string email, string path = "/api/Authentications/UserLogin")
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "POST"; ctx.Request.Path = path;
        ctx.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes($"{{\"Email\":\"{email}\",\"Password\":\"x\"}}"));
        ctx.Response.Body = new MemoryStream();
        await mw.InvokeAsync(ctx);
        return ctx.Response.StatusCode;
    }

    private static string Ip(int a) => $"10.{Random.Shared.Next(1, 250)}.{Random.Shared.Next(1, 250)}.{a}";

    [Fact]
    public async Task FiveFailedAttemptsForOneEmail_BlockOnlyThatEmail_OtherEmailsFromTheSameIpStillWork()
    {
        var mw = Build(_ => 401, out _);
        var ip = Ip(1); var a = $"a{Uniq()}@x.com"; var b = $"b{Uniq()}@x.com";
        for (int i = 0; i < 5; i++) Assert.Equal(401, await Login(mw, ip, a));     // five real failures reach the controller
        Assert.Equal(429, await Login(mw, ip, a));                                  // sixth: blocked
        Assert.Equal(401, await Login(mw, ip, b));                                  // same IP, different email: not blocked
    }

    [Fact]
    public async Task EmailBlock_FollowsTheEmail_NotTheIp_AndIsCaseInsensitive()
    {
        var mw = Build(_ => 401, out _);
        var e = $"user{Uniq()}@x.com";
        for (int i = 0; i < 5; i++) await Login(mw, Ip(3), e);
        Assert.Equal(429, await Login(mw, Ip(4), e.ToUpperInvariant()));            // other IP, different case: still blocked
    }

    [Fact]
    public async Task SuccessfulLogin_ResetsTheCounter()
    {
        var statuses = new Queue<int>(new[] { 401, 401, 401, 401, 200, 401, 401, 401, 401, 401 });
        var mw = Build(_ => statuses.Dequeue(), out _);
        var ip = Ip(5); var e = $"reset{Uniq()}@x.com";
        for (int i = 0; i < 4; i++) Assert.Equal(401, await Login(mw, ip, e));
        Assert.Equal(200, await Login(mw, ip, e));                                   // success resets
        for (int i = 0; i < 4; i++) Assert.Equal(401, await Login(mw, ip, e));       // 4 more failures: still not blocked
        Assert.Equal(401, await Login(mw, ip, e));                                   // 5th failure after the reset reaches the controller...
        Assert.Equal(429, await Login(mw, ip, e));                                   // ...and now the email is blocked
    }

    [Fact]
    public async Task SuccessesDoNotCountTowardsTheEmailLimit()
    {
        var mw = Build(_ => 200, out _);
        var ip = Ip(6); var e = $"ok{Uniq()}@x.com";
        for (int i = 0; i < 20; i++) Assert.Equal(200, await Login(mw, ip, e));
    }

    [Fact]
    public async Task IpCeiling_Is30PerMinute_AcrossDifferentEmails()
    {
        var mw = Build(_ => 200, out _);
        var ip = Ip(7);
        for (int i = 0; i < 30; i++) Assert.Equal(200, await Login(mw, ip, $"u{i}{Uniq()}@x.com"));
        Assert.Equal(429, await Login(mw, ip, $"extra{Uniq()}@x.com"));
        Assert.Equal(200, await Login(mw, Ip(8), $"other{Uniq()}@x.com"));            // another IP unaffected
    }

    [Fact]
    public async Task LockoutResponse_IsIdenticalForExistingAndNonExistingEmails()
    {
        string? body1 = null, body2 = null;
        var mw = Build(_ => 401, out _);
        var e1 = $"exists{Uniq()}@x.com"; var e2 = $"ghost{Uniq()}@x.com";
        foreach (var e in new[] { e1, e2 }) for (int i = 0; i < 5; i++) await Login(mw, Ip(9), e);
        foreach (var (e, set) in new[] { (e1, 1), (e2, 2) })
        {
            var ctx = new DefaultHttpContext(); ctx.Request.Method = "POST"; ctx.Request.Path = "/api/Authentications/UserLogin";
            ctx.Connection.RemoteIpAddress = IPAddress.Parse(Ip(10));
            ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes($"{{\"Email\":\"{e}\",\"Password\":\"x\"}}")); ctx.Response.Body = new MemoryStream();
            await mw.InvokeAsync(ctx);
            ctx.Response.Body.Position = 0; var text = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
            Assert.Equal(429, ctx.Response.StatusCode);
            if (set == 1) body1 = text; else body2 = text;
        }
        Assert.Equal(body1, body2);
    }

    [Fact]
    public async Task Body_IsStillReadableByTheController()
    {
        var mw = Build(_ => 200, out var seen);
        await Login(mw, Ip(11), $"body{Uniq()}@x.com");
        Assert.Contains("\"Password\":\"x\"", seen[0]);
    }

    [Fact]
    public async Task Refresh_HasItsOwnMoreGenerousBucket_AndDoesNotConsumeLoginBudget()
    {
        var mw = Build(_ => 200, out _);
        var ip = Ip(12);
        for (int i = 0; i < 30; i++) Assert.Equal(200, await Login(mw, ip, "r@x.com", "/api/Authentications/RefreshToken"));
        Assert.Equal(429, await Login(mw, ip, "r@x.com", "/api/Authentications/RefreshToken"));
        Assert.Equal(200, await Login(mw, ip, $"fresh{Uniq()}@x.com"));              // login on the same IP still has its own budget
    }

    [Fact]
    public async Task Thresholds_AreConfigurable()
    {
        var opts = new RateLimitOptions { LoginFailures = new RateLimitBucket { MaxRequests = 2, WindowSeconds = 60, BlockDurationSeconds = 10 } };
        var mw = Build(_ => 401, out _, opts);
        var e = $"cfg{Uniq()}@x.com"; var ip = Ip(13);
        await Login(mw, ip, e); await Login(mw, ip, e);
        Assert.Equal(429, await Login(mw, ip, e));
    }
}
