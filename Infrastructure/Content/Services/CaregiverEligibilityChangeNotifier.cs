using Application.Interfaces.Content;
using System.Threading.Tasks;

namespace Infrastructure.Content.Services
{
    public class CaregiverEligibilityChangeNotifier : ICaregiverEligibilityChangeNotifier
    {
        private readonly IMarketingSyncService _marketingSyncService;

        public CaregiverEligibilityChangeNotifier(IMarketingSyncService marketingSyncService)
        {
            _marketingSyncService = marketingSyncService;
        }

        public async Task NotifyIfActiveHireAffectedAsync(string caregiverId)
        {
            // A readiness change is itself marketing-relevant (e.g. nudge to redo
            // verification) independent of whether a hire happens to be in flight.
            await _marketingSyncService.EnqueueCaregiverSyncAsync(caregiverId);
        }
    }
}
