using Application.Interfaces.Common;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.Services.Common
{
    public class RateLimitingService : IRateLimitingService
    {
        private readonly IMemoryCache _cache;
        private readonly IConfiguration _config;

        public RateLimitingService(IMemoryCache cache, IConfiguration config)
        {
            _cache = cache;
            _config = config;
        }

        public bool CheckRateLimit(string clientIp)
        {
            var windowMs = _config.GetValue<int>("Webhook:RateLimitWindowMs", 300000);
            var maxRequests = _config.GetValue<int>("Webhook:RateLimitMaxRequests", 100);

            var key = $"webhook_rate_{clientIp}";
            var requests = _cache.Get<List<DateTime>>(key) ?? new List<DateTime>();

            var now = DateTime.UtcNow;
            var windowStart = now.AddMilliseconds(-windowMs);

            // Remove old requests outside the window
            requests = requests.Where(timestamp => timestamp > windowStart).ToList();

            if (requests.Count >= maxRequests)
                return false; // Rate limit exceeded

            requests.Add(now);
            _cache.Set(key, requests, TimeSpan.FromMilliseconds(windowMs));

            return true;
        }
    }
}