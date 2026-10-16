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

        /// <summary>Is this key currently blocked? Records nothing (used for the per-email login failure limit).</summary>
        Task<(bool IsBlocked, int RetryAfterSeconds)> CheckBlockedAsync(string key, CancellationToken ct = default);

        /// <summary>Counts one FAILED attempt for this key; blocks it once <c>bucket.MaxRequests</c> failures occur within the window.</summary>
        Task<(bool IsBlocked, int RetryAfterSeconds)> RecordFailureAsync(string key, RateLimitBucket bucket, CancellationToken ct = default);

        /// <summary>Clears this key's failure counter and any block (called on a successful login).</summary>
        Task ResetAsync(string key, CancellationToken ct = default);
    }
}
