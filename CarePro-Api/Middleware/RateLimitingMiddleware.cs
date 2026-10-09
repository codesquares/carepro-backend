using System.Net;
using System.Security.Claims;
using System.Text.Json;
using CarePro_Api.Middleware.RateLimiting;
using Microsoft.Extensions.Options;

namespace CarePro_Api.Middleware
{
    /// <summary>
    /// Rate limiting middleware. Routes requests into one of four buckets:
    ///   auth, password-reset, registration, general (per-user when authenticated, per-IP otherwise).
    /// Authenticated callers are keyed by user id; anonymous callers by client IP
    /// (resolved from RemoteIpAddress after UseForwardedHeaders).
    /// </summary>
    public class RateLimitingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<RateLimitingMiddleware> _logger;
        private readonly IRateLimitStore _store;
        private readonly RateLimitOptions _options;

        // Strict, EXACT-match endpoint maps. Path must be lowercased before lookup.
        // Restricting to specific (method, path) pairs prevents read endpoints under the
        // same controller (e.g. GET /api/admins/certificates/pendingreview) from being
        // throttled as if they were registration POSTs.
        // RETIRED as a single shared bucket: login and refresh used to share Auth (5 requests/min per IP, 300 s block,
        // successes counted), so users behind one carrier IP locked each other out. Now: login = per-IP ceiling +
        // per-email FAILURE limit (HandleLoginAsync); refresh = its own bucket. Kept for reference:
        // private static readonly HashSet<string> _authPostEndpoints = { "/api/authentications/userlogin", "/api/authentications/refreshtoken" };
        private const string LoginPath = "/api/authentications/userlogin";
        private const string RefreshPath = "/api/authentications/refreshtoken";

        private static readonly HashSet<string> _passwordResetPostEndpoints = new(StringComparer.OrdinalIgnoreCase)
        {
            "/api/caregivers/requestpasswordreset",
            "/api/caregivers/resetpassword",
            "/api/caregivers/change-password",
            "/api/clients/requestpasswordreset",
            "/api/clients/resetpassword"
        };

        private static readonly HashSet<string> _registrationPostEndpoints = new(StringComparer.OrdinalIgnoreCase)
        {
            "/api/clients/addclientuser",
            "/api/clients/googlesignup",
            "/api/caregivers/addcaregiveruser",
            "/api/caregivers/googlesignup",
            "/api/admins"
        };

        public RateLimitingMiddleware(
            RequestDelegate next,
            ILogger<RateLimitingMiddleware> logger,
            IRateLimitStore store,
            IOptions<RateLimitOptions> options)
        {
            _next = next;
            _logger = logger;
            _store = store;
            _options = options.Value;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (!_options.Enabled)
            {
                await _next(context);
                return;
            }

            var method = context.Request.Method;
            var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";

            if (HttpMethods.IsPost(method) && string.Equals(path, LoginPath, StringComparison.OrdinalIgnoreCase))
            {
                await HandleLoginAsync(context);
                return;
            }

            var (category, bucket) = Classify(method, path, context);
            var clientKey = BuildClientKey(context, category);
            var rateLimitKey = $"rl:{category}:{clientKey}";

            var (limited, retryAfter) = await _store.CheckAndRecordAsync(rateLimitKey, bucket, context.RequestAborted);

            if (limited)
            {
                _logger.LogWarning(
                    "Rate limit exceeded. Category={Category}, Path={Path}, Method={Method}, Client={Client}, RetryAfter={RetryAfter}s",
                    category, path, method, MaskKey(clientKey), retryAfter);

                context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
                context.Response.ContentType = "application/json";
                context.Response.Headers["Retry-After"] = retryAfter.ToString();

                var payload = new
                {
                    success = false,
                    message = "Too many requests. Please try again later.",
                    retryAfterSeconds = retryAfter
                };

                await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
                return;
            }

            await _next(context);
        }

        /// <summary>
        /// Login limiting:
        ///  1. per-IP ceiling (all attempts, generous — shared carrier IPs are normal);
        ///  2. per-EMAIL failure limit: N failed logins within the window block THAT email only; a successful login resets it.
        /// The block response is identical for every email, whether or not an account exists, so it reveals nothing.
        /// The email comes from the request body (buffered and rewound so the controller still reads it).
        /// </summary>
        private async Task HandleLoginAsync(HttpContext context)
        {
            var ip = GetClientIp(context);
            var (ipLimited, ipRetry) = await _store.CheckAndRecordAsync($"rl:login-ip:ip:{ip}", _options.LoginIp, context.RequestAborted);
            if (ipLimited)
            {
                _logger.LogWarning("Login IP ceiling exceeded. Client={Client}, RetryAfter={RetryAfter}s", MaskKey($"ip:{ip}"), ipRetry);
                await WriteLimitedAsync(context, ipRetry);
                return;
            }

            var emailKey = await ReadEmailKeyAsync(context);
            if (emailKey != null)
            {
                var (blocked, retry) = await _store.CheckBlockedAsync(emailKey, context.RequestAborted);
                if (blocked)
                {
                    _logger.LogWarning("Login blocked for an email after repeated failures (RetryAfter={RetryAfter}s)", retry);
                    await WriteLimitedAsync(context, retry);
                    return;
                }
            }

            await _next(context);

            if (emailKey == null) return;
            var status = context.Response.StatusCode;
            if (status >= 200 && status < 300)
                await _store.ResetAsync(emailKey, context.RequestAborted);
            else if (status is 400 or 401 or 403 or 404)
                await _store.RecordFailureAsync(emailKey, _options.LoginFailures, context.RequestAborted);
        }

        private static async Task WriteLimitedAsync(HttpContext context, int retryAfter)
        {
            context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
            context.Response.ContentType = "application/json";
            context.Response.Headers["Retry-After"] = retryAfter.ToString();
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                success = false,
                message = "Too many requests. Please try again later.",
                retryAfterSeconds = retryAfter
            }));
        }

        /// <summary>"rl:login-email:&lt;sha256 prefix of the lower-cased email&gt;" or null when no email can be read. Raw emails are never stored.</summary>
        private static async Task<string?> ReadEmailKeyAsync(HttpContext context)
        {
            try
            {
                context.Request.EnableBuffering();
                using var reader = new StreamReader(context.Request.Body, System.Text.Encoding.UTF8, false, 1024, leaveOpen: true);
                var buffer = new char[8192];
                var read = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
                context.Request.Body.Position = 0;
                if (read == 0) return null;
                using var doc = JsonDocument.Parse(new string(buffer, 0, read));
                string? email = null;
                foreach (var prop in doc.RootElement.EnumerateObject())
                    if (string.Equals(prop.Name, "email", StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.String)
                        email = prop.Value.GetString();
                if (string.IsNullOrWhiteSpace(email)) return null;
                var norm = email.Trim().ToLowerInvariant();
                if (norm.Length > 254) norm = norm[..254];
                var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(norm)))[..16];
                return $"rl:login-email:{hash}";
            }
            catch
            {
                try { context.Request.Body.Position = 0; } catch { /* body not seekable */ }
                return null; // unreadable/oversized/invalid JSON: fall back to the IP ceiling only
            }
        }

        private (string category, RateLimitBucket bucket) Classify(string method, string path, HttpContext ctx)
        {
            if (HttpMethods.IsPost(method))
            {
                if (string.Equals(path, RefreshPath, StringComparison.OrdinalIgnoreCase))
                    return ("refresh", _options.Refresh);
                if (_passwordResetPostEndpoints.Contains(path))
                    return ("password-reset", _options.PasswordReset);
                if (_registrationPostEndpoints.Contains(path))
                    return ("registration", _options.Registration);
            }

            // Authenticated traffic gets a much higher per-user budget;
            // anonymous traffic gets the stricter per-IP general budget.
            if (ctx.User?.Identity?.IsAuthenticated == true)
            {
                return ("general-user", _options.Authenticated);
            }

            return ("general-ip", _options.General);
        }

        private static string BuildClientKey(HttpContext ctx, string category)
        {
            // For authenticated traffic, prefer user id so multiple admins behind the
            // same NAT/VPN don't share a bucket.
            if (category == "general-user")
            {
                var userId = ctx.User?.FindFirst("userId")?.Value
                             ?? ctx.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                             ?? ctx.User?.FindFirst("sub")?.Value;
                if (!string.IsNullOrWhiteSpace(userId))
                {
                    return $"u:{userId}";
                }
            }

            return $"ip:{GetClientIp(ctx)}";
        }

        private static string GetClientIp(HttpContext context)
        {
            // RemoteIpAddress is populated correctly by UseForwardedHeaders when the
            // request comes through the ALB. Falling back to X-Forwarded-For is only
            // useful for environments without forwarded-headers configured.
            var ip = context.Connection.RemoteIpAddress?.ToString();
            if (!string.IsNullOrWhiteSpace(ip)) return ip;

            var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrEmpty(forwardedFor))
            {
                return forwardedFor.Split(',')[0].Trim();
            }

            return "unknown";
        }

        private static string MaskKey(string key)
        {
            // Avoid logging full IPs / user ids verbatim.
            if (key.StartsWith("u:")) return "u:***";
            if (key.StartsWith("ip:"))
            {
                var ip = key[3..];
                var parts = ip.Split('.');
                return parts.Length == 4 ? $"ip:{parts[0]}.{parts[1]}.***" : "ip:***";
            }
            return "***";
        }
    }

    public static class RateLimitingMiddlewareExtensions
    {
        public static IApplicationBuilder UseRateLimiting(this IApplicationBuilder builder)
            => builder.UseMiddleware<RateLimitingMiddleware>();
    }
}
