using Application.Interfaces.Content;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Content.Services
{
    /// <summary>
    /// Re-alerts approved admins ONCE about "caregiver can't attend" notices that are still open when their day is within 24 hours.
    /// Safe to run on several API instances: the once-only claim lives in the database (VisitAbsenceNotice.EscalatedAt is a
    /// concurrency token), so only the instance whose update matches sends the alert.
    /// </summary>
    public class VisitAbsenceEscalationProcessor : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<VisitAbsenceEscalationProcessor> _logger;
        private readonly TimeSpan _interval = TimeSpan.FromMinutes(10);

        public VisitAbsenceEscalationProcessor(IServiceScopeFactory scopeFactory, ILogger<VisitAbsenceEscalationProcessor> logger)
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
                        var n = await scope.ServiceProvider.GetRequiredService<IVisitAbsenceService>().EscalateDueAsync();
                        if (n > 0) _logger.LogInformation("Re-alerted admins about {Count} still-open absence notice(s)", n);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                    catch (Exception ex) { _logger.LogError(ex, "Absence-notice escalation sweep failed."); }

                    await Task.Delay(_interval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        }
    }
}
