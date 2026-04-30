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
        private static readonly HashSet<string> _authPostEndpoints = new(StringComparer.OrdinalIgnoreCase)
        {
            "/api/authentications/userlogin",
            "/api/authentications/refreshtoken"
        };

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

        private (string category, RateLimitBucket bucket) Classify(string method, string path, HttpContext ctx)
        {
            if (HttpMethods.IsPost(method))
            {
                if (_authPostEndpoints.Contains(path))
                    return ("auth", _options.Auth);
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
