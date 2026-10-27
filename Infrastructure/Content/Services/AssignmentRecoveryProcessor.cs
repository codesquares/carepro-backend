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
    /// Finishes accept/decline/cancel transitions that were saved but never fully completed (e.g. the process died or a
    /// downstream step threw and nobody retried). Same pattern as <see cref="ContractCancellationWindowProcessor"/>: no leader
    /// election — running on several instances is safe because every side effect behind it is an atomic claim.
    /// </summary>
    public class AssignmentRecoveryProcessor : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<AssignmentRecoveryProcessor> _logger;
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan GracePeriod = TimeSpan.FromMinutes(5);   // leave live requests alone

        public AssignmentRecoveryProcessor(IServiceScopeFactory scopeFactory, ILogger<AssignmentRecoveryProcessor> logger)
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
                        var svc = scope.ServiceProvider.GetRequiredService<IAssignmentService>();
                        var n = await svc.RecoverIncompleteTransitionsAsync(GracePeriod);
                        if (n > 0) _logger.LogWarning("Assignment recovery completed {Count} half-done transition(s)", n);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                    catch (Exception ex) { _logger.LogError(ex, "Assignment recovery sweep failed."); }

                    await Task.Delay(Interval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        }
    }
}
