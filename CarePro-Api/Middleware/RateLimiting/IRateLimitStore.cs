namespace CarePro_Api.Middleware.RateLimiting
{
    /// <summary>
    /// Atomic check-and-record contract for rate limit counters.
    /// Implementations: in-memory (single instance) and Redis (distributed).
    /// </summary>
    public interface IRateLimitStore
    {
        /// <summary>
        /// Atomically records a request and reports whether the caller is currently rate-limited.
        /// </summary>
        /// <returns>(IsLimited, RetryAfterSeconds)</returns>
        Task<(bool IsLimited, int RetryAfterSeconds)> CheckAndRecordAsync(string key, RateLimitBucket bucket, CancellationToken ct = default);
    }
}
