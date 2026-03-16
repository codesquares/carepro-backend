using Application.Interfaces.Content;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Infrastructure.Content.Services
{
    public class CareRequestMatchingProcessor : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<CareRequestMatchingProcessor> _logger;
        private readonly TimeSpan _pollingInterval = TimeSpan.FromMinutes(2);

        public CareRequestMatchingProcessor(
            IServiceScopeFactory scopeFactory,
            ILogger<CareRequestMatchingProcessor> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("CareRequestMatchingProcessor started. Polling every {Interval} minutes.", _pollingInterval.TotalMinutes);

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        await ProcessPendingCareRequestsAsync(stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error in CareRequestMatchingProcessor polling cycle");
                    }

                    await Task.Delay(_pollingInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown — do not propagate
            }

            _logger.LogInformation("CareRequestMatchingProcessor: Stopped");
        }

        private async Task ProcessPendingCareRequestsAsync(CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<CareProDbContext>();
            var matchingService = scope.ServiceProvider.GetRequiredService<ICareRequestMatchingService>();

            // Find care requests that need matching:
            // 1. New pending requests (created > 30s ago to avoid racing with creation response)
            // 2. Previously unmatched requests (retry after 24h cooldown)
            var cutoff = DateTime.UtcNow.AddSeconds(-30);
            var retryCutoff = DateTime.UtcNow.AddHours(-24);
            var pendingRequests = await dbContext.CareRequests
                .Where(cr =>
                    (cr.Status == "pending" && cr.CreatedAt < cutoff) ||
                    (cr.Status == "unmatched" && cr.UpdatedAt != null && cr.UpdatedAt < retryCutoff))
                .OrderBy(cr => cr.CreatedAt)
                .Take(20)
                .ToListAsync(stoppingToken);

            if (pendingRequests.Count == 0) return;

            _logger.LogInformation("Found {Count} pending care requests to match", pendingRequests.Count);

            foreach (var request in pendingRequests)
            {
                if (stoppingToken.IsCancellationRequested) break;

                try
                {
                    var result = await matchingService.FindMatchesForCareRequestAsync(request.Id.ToString());
                    _logger.LogInformation("Matched CareRequest {Id}: {Count} results, status: {Status}",
                        request.Id, result.TotalMatches, result.Status);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to match CareRequest {Id}", request.Id);
                }
            }
        }
    }
}
