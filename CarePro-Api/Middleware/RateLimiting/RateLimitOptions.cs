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

        /// <summary>
        /// Login, per IP, ALL attempts counted — a coarse ceiling only (many users can share one carrier/NAT IP), so it is
        /// deliberately much higher than the old 5/min. Config: RateLimiting:LoginIp. Default 30 requests / 60 s, 60 s block.
        /// </summary>
        public RateLimitBucket LoginIp { get; set; } = new()
        {
            MaxRequests = 30,
            WindowSeconds = 60,
            BlockDurationSeconds = 60
        };

        /// <summary>
        /// Login, per submitted EMAIL, FAILED attempts only (success resets the counter). Config: RateLimiting:LoginFailures.
        /// Default: 5 failures within 60 s blocks that email for 300 s. MaxRequests here means "max failed attempts".
        /// </summary>
        public RateLimitBucket LoginFailures { get; set; } = new()
        {
            MaxRequests = 5,
            WindowSeconds = 60,
            BlockDurationSeconds = 300
        };

        /// <summary>Refresh-token calls: their own, more generous bucket (per IP). Config: RateLimiting:Refresh. Default 30 / 60 s, 60 s block.</summary>
        public RateLimitBucket Refresh { get; set; } = new()
        {
            MaxRequests = 30,
            WindowSeconds = 60,
            BlockDurationSeconds = 60
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
