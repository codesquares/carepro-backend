using Application.Interfaces.Content;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Infrastructure.Content.Services
{
    /// <summary>
    /// Periodically records "agreed by non-cancellation" on package contracts whose cancellation window has
    /// ended without a cancellation. (GET contract also does this lazily, so a page view never shows stale state.)
    /// </summary>
    public class ContractCancellationWindowProcessor : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ContractCancellationWindowProcessor> _logger;
        private readonly TimeSpan _interval = TimeSpan.FromMinutes(10);

        public ContractCancellationWindowProcessor(IServiceScopeFactory scopeFactory, ILogger<ContractCancellationWindowProcessor> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var svc = scope.ServiceProvider.GetRequiredService<IPackageContractService>();
                        var n = await svc.FinalizeExpiredWindowsAsync();
                        if (n > 0) _logger.LogInformation("Recorded agreement-by-non-cancellation on {Count} contract(s)", n);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                    catch (Exception ex) { _logger.LogError(ex, "Cancellation-window sweep failed."); }

                    await Task.Delay(_interval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        }
    }
}
