using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;

namespace CarePro_Api.Middleware
{
    /// <summary>
    /// Rate limiting middleware that protects endpoints from brute-force attacks.
    /// Applies stricter limits to authentication endpoints.
    /// </summary>
    public class RateLimitingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<RateLimitingMiddleware> _logger;
        
        // Thread-safe storage for rate limit tracking
        private static readonly ConcurrentDictionary<string, RateLimitEntry> _rateLimitStore = new();
        
        // Configuration
        private static readonly RateLimitConfig _authConfig = new()
        {
            MaxRequests = 5,
            WindowSeconds = 60,  // 5 requests per minute for auth endpoints
            BlockDurationSeconds = 300  // 5 minute block after exceeding limit
        };
        
        private static readonly RateLimitConfig _passwordResetConfig = new()
        {
            MaxRequests = 3,
            WindowSeconds = 3600,  // 3 requests per hour for password reset
            BlockDurationSeconds = 3600
        };

        private static readonly RateLimitConfig _registrationConfig = new()
        {
            MaxRequests = 3,
            WindowSeconds = 3600,  // 3 registrations per hour per IP
            BlockDurationSeconds = 3600  // 1 hour block after exceeding
        };

        private static readonly RateLimitConfig _generalConfig = new()
        {
            MaxRequests = 100,
            WindowSeconds = 60,  // 100 requests per minute for general endpoints
            BlockDurationSeconds = 60
        };

        // Endpoints that need strict rate limiting
        private static readonly string[] _authEndpoints = 
        {
            "/api/authentications/userlogin",
            "/api/authentications/refreshtoken"
        };
        
        private static readonly string[] _passwordResetEndpoints = 
        {
            "/api/caregivers/requestpasswordreset",
            "/api/caregivers/resetpassword",
            "/api/caregivers/change-password",
            "/api/clients/requestpasswordreset",
            "/api/clients/resetpassword"
        };

        private static readonly string[] _registrationEndpoints =
        {
            "/api/clients/addclientuser",
            "/api/clients/googlesignup",
            "/api/caregivers/addcaregiveruser",
            "/api/caregivers/googlesignup",
            "/api/admins"
        };

        public RateLimitingMiddleware(RequestDelegate next, ILogger<RateLimitingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var clientIp = GetClientIpAddress(context);
            var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";
            
            // Determine which rate limit config to use
            var config = GetRateLimitConfig(path);
            var rateLimitKey = $"{clientIp}:{GetEndpointCategory(path)}";
            
            // Check rate limit
            if (IsRateLimited(rateLimitKey, config, out var retryAfter))
            {
                _logger.LogWarning(
                    "Rate limit exceeded for IP: {ClientIp}, Path: {Path}, RetryAfter: {RetryAfter}s",
                    MaskIpAddress(clientIp), path, retryAfter);
                
                context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
                context.Response.ContentType = "application/json";
                context.Response.Headers["Retry-After"] = retryAfter.ToString();
                
                var response = new
                {
                    success = false,
                    message = "Too many requests. Please try again later.",
                    retryAfterSeconds = retryAfter
                };
                
                await context.Response.WriteAsync(JsonSerializer.Serialize(response));
                return;
            }
            
            // Record this request
            RecordRequest(rateLimitKey, config);
            
            await _next(context);
        }

        private static string GetClientIpAddress(HttpContext context)
        {
            // Check for forwarded IP (behind proxy/load balancer)
            var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrEmpty(forwardedFor))
            {
                return forwardedFor.Split(',')[0].Trim();
            }
            
            return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        }

        private static RateLimitConfig GetRateLimitConfig(string path)
        {
            if (_authEndpoints.Any(e => path.Contains(e)))
                return _authConfig;
            
            if (_passwordResetEndpoints.Any(e => path.Contains(e)))
                return _passwordResetConfig;

            if (_registrationEndpoints.Any(e => path.Contains(e)))
                return _registrationConfig;
            
            return _generalConfig;
        }

        private static string GetEndpointCategory(string path)
        {
            if (_authEndpoints.Any(e => path.Contains(e)))
                return "auth";
            
            if (_passwordResetEndpoints.Any(e => path.Contains(e)))
                return "password-reset";

            if (_registrationEndpoints.Any(e => path.Contains(e)))
                return "registration";
            
            return "general";
        }

        private static bool IsRateLimited(string key, RateLimitConfig config, out int retryAfter)
        {
            retryAfter = 0;
            
            if (!_rateLimitStore.TryGetValue(key, out var entry))
                return false;
            
            // Check if blocked
            if (entry.BlockedUntil.HasValue && entry.BlockedUntil > DateTime.UtcNow)
            {
                retryAfter = (int)(entry.BlockedUntil.Value - DateTime.UtcNow).TotalSeconds;
                return true;
            }
            
            // Clean up old entries outside the window
            var windowStart = DateTime.UtcNow.AddSeconds(-config.WindowSeconds);
            entry.Requests.RemoveAll(r => r < windowStart);
            
            // Check if over limit
            if (entry.Requests.Count >= config.MaxRequests)
            {
                entry.BlockedUntil = DateTime.UtcNow.AddSeconds(config.BlockDurationSeconds);
                retryAfter = config.BlockDurationSeconds;
                return true;
            }
            
            return false;
        }

        private static void RecordRequest(string key, RateLimitConfig config)
        {
            var entry = _rateLimitStore.GetOrAdd(key, _ => new RateLimitEntry());
            
            // Clean old entries
            var windowStart = DateTime.UtcNow.AddSeconds(-config.WindowSeconds);
            entry.Requests.RemoveAll(r => r < windowStart);
            
            entry.Requests.Add(DateTime.UtcNow);
        }

        private static string MaskIpAddress(string ip)
        {
            // Mask IP for logging privacy: 192.168.1.100 -> 192.168.***
            var parts = ip.Split('.');
            if (parts.Length == 4)
                return $"{parts[0]}.{parts[1]}.***";
            return "***";
        }

        // Cleanup old entries periodically (call from background service if needed)
        public static void CleanupOldEntries()
        {
            var cutoff = DateTime.UtcNow.AddHours(-1);
            var keysToRemove = _rateLimitStore
                .Where(kvp => kvp.Value.Requests.All(r => r < cutoff) && 
                              (!kvp.Value.BlockedUntil.HasValue || kvp.Value.BlockedUntil < DateTime.UtcNow))
                .Select(kvp => kvp.Key)
                .ToList();
            
            foreach (var key in keysToRemove)
            {
                _rateLimitStore.TryRemove(key, out _);
            }
        }
    }

    public class RateLimitEntry
    {
        public List<DateTime> Requests { get; } = new();
        public DateTime? BlockedUntil { get; set; }
    }

    public class RateLimitConfig
    {
        public int MaxRequests { get; set; }
        public int WindowSeconds { get; set; }
        public int BlockDurationSeconds { get; set; }
    }

    public static class RateLimitingMiddlewareExtensions
    {
        public static IApplicationBuilder UseRateLimiting(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<RateLimitingMiddleware>();
        }
    }
}
