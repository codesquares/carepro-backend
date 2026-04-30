using StackExchange.Redis;

namespace CarePro_Api.Middleware.RateLimiting
{
    /// <summary>
    /// Distributed fixed-window rate limit store backed by Redis.
    /// Falls back to the in-memory store if Redis is unavailable so the API never 5xx's
    /// because of limiter infrastructure problems.
    /// </summary>
    public class RedisRateLimitStore : IRateLimitStore
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly InMemoryRateLimitStore _fallback;
        private readonly ILogger<RedisRateLimitStore> _logger;

        public RedisRateLimitStore(
            IConnectionMultiplexer redis,
            InMemoryRateLimitStore fallback,
            ILogger<RedisRateLimitStore> logger)
        {
            _redis = redis;
            _fallback = fallback;
            _logger = logger;
        }

        public async Task<(bool IsLimited, int RetryAfterSeconds)> CheckAndRecordAsync(
            string key, RateLimitBucket bucket, CancellationToken ct = default)
        {
            try
            {
                var db = _redis.GetDatabase();
                var blockKey = $"{key}:blocked";

                // 1. If currently blocked, short-circuit.
                var blockTtl = await db.KeyTimeToLiveAsync(blockKey);
                if (blockTtl.HasValue && blockTtl.Value > TimeSpan.Zero)
                {
                    return (true, (int)Math.Ceiling(blockTtl.Value.TotalSeconds));
                }

                // 2. Atomic counter for the current fixed window.
                var counterKey = $"{key}:count";
                var count = await db.StringIncrementAsync(counterKey);
                if (count == 1)
                {
                    // First hit in this window - set expiry equal to window length.
                    await db.KeyExpireAsync(counterKey, TimeSpan.FromSeconds(bucket.WindowSeconds));
                }

                if (count > bucket.MaxRequests)
                {
                    // Set block flag with TTL = block duration.
                    await db.StringSetAsync(
                        blockKey,
                        "1",
                        TimeSpan.FromSeconds(bucket.BlockDurationSeconds));
                    return (true, bucket.BlockDurationSeconds);
                }

                return (false, 0);
            }
            catch (Exception ex)
            {
                // Never let Redis problems take down request processing.
                _logger.LogWarning(ex, "Redis rate-limit check failed for key {Key}; falling back to in-memory store", key);
                return await _fallback.CheckAndRecordAsync(key, bucket, ct);
            }
        }
    }
}
