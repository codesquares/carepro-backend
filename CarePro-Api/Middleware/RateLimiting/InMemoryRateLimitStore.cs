using System.Collections.Concurrent;

namespace CarePro_Api.Middleware.RateLimiting
{
    /// <summary>
    /// Sliding-window in-memory rate limit store. Suitable for single-instance deployments.
    /// State is process-local; resets on restart.
    /// </summary>
    public class InMemoryRateLimitStore : IRateLimitStore
    {
        private static readonly ConcurrentDictionary<string, Entry> _store = new();

        public Task<(bool IsLimited, int RetryAfterSeconds)> CheckAndRecordAsync(
            string key, RateLimitBucket bucket, CancellationToken ct = default)
        {
            var entry = _store.GetOrAdd(key, _ => new Entry());

            lock (entry)
            {
                var now = DateTime.UtcNow;

                // Currently blocked?
                if (entry.BlockedUntil.HasValue && entry.BlockedUntil > now)
                {
                    var retry = (int)Math.Ceiling((entry.BlockedUntil.Value - now).TotalSeconds);
                    return Task.FromResult((true, retry));
                }

                // Drop requests outside the window
                var windowStart = now.AddSeconds(-bucket.WindowSeconds);
                entry.Requests.RemoveAll(r => r < windowStart);

                if (entry.Requests.Count >= bucket.MaxRequests)
                {
                    entry.BlockedUntil = now.AddSeconds(bucket.BlockDurationSeconds);
                    return Task.FromResult((true, bucket.BlockDurationSeconds));
                }

                entry.Requests.Add(now);
                return Task.FromResult((false, 0));
            }
        }

        /// <summary>
        /// Removes entries that have no recent requests and are not currently blocked.
        /// Called periodically by the cleanup hosted service.
        /// </summary>
        public static int Cleanup()
        {
            var cutoff = DateTime.UtcNow.AddHours(-1);
            var removed = 0;
            foreach (var kvp in _store)
            {
                lock (kvp.Value)
                {
                    var hasRecent = kvp.Value.Requests.Any(r => r >= cutoff);
                    var isBlocked = kvp.Value.BlockedUntil.HasValue && kvp.Value.BlockedUntil > DateTime.UtcNow;
                    if (!hasRecent && !isBlocked)
                    {
                        if (_store.TryRemove(kvp.Key, out _)) removed++;
                    }
                }
            }
            return removed;
        }

        private sealed class Entry
        {
            public List<DateTime> Requests { get; } = new();
            public DateTime? BlockedUntil { get; set; }
        }
    }
}
