namespace CarePro_Api.Middleware.RateLimiting
{
    /// <summary>
    /// Periodically prunes expired entries from the in-memory rate limit store.
    /// Redis entries expire automatically via TTL and need no cleanup.
    /// </summary>
    public class RateLimitCleanupService : BackgroundService
    {
        private readonly ILogger<RateLimitCleanupService> _logger;
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

        public RateLimitCleanupService(ILogger<RateLimitCleanupService> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var removed = InMemoryRateLimitStore.Cleanup();
                    if (removed > 0)
                    {
                        _logger.LogDebug("Rate limit cleanup removed {Count} stale entries", removed);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Rate limit cleanup iteration failed");
                }

                try
                {
                    await Task.Delay(Interval, stoppingToken);
                }
                catch (TaskCanceledException) { /* shutting down */ }
            }
        }
    }
}
