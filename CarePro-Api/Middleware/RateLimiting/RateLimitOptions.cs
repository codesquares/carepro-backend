namespace CarePro_Api.Middleware.RateLimiting
{
    /// <summary>
    /// Strongly-typed rate limit configuration. Bound from config + env vars.
    /// </summary>
    public class RateLimitOptions
    {
        /// <summary>Master toggle. Driven by ENABLE_RATE_LIMITING env var.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Optional Redis connection string. When empty, in-memory store is used.</summary>
        public string? RedisConnectionString { get; set; }

        public RateLimitBucket Auth { get; set; } = new()
        {
            MaxRequests = 5,
            WindowSeconds = 60,
            BlockDurationSeconds = 300
        };

        public RateLimitBucket PasswordReset { get; set; } = new()
        {
            MaxRequests = 3,
            WindowSeconds = 3600,
            BlockDurationSeconds = 3600
        };

        public RateLimitBucket Registration { get; set; } = new()
        {
            MaxRequests = 3,
            WindowSeconds = 3600,
            BlockDurationSeconds = 3600
        };

        /// <summary>Limit for unauthenticated general traffic (per IP).</summary>
        public RateLimitBucket General { get; set; } = new()
        {
            MaxRequests = 100,
            WindowSeconds = 60,
            BlockDurationSeconds = 60
        };

        /// <summary>Limit for authenticated traffic on general endpoints (per user).</summary>
        public RateLimitBucket Authenticated { get; set; } = new()
        {
            MaxRequests = 600,
            WindowSeconds = 60,
            BlockDurationSeconds = 60
        };
    }

    public class RateLimitBucket
    {
        public int MaxRequests { get; set; }
        public int WindowSeconds { get; set; }
        public int BlockDurationSeconds { get; set; }
    }
}
