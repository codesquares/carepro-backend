using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    /// <summary>
    /// Enqueues a caregiver or client for Brevo contact/list sync. Call from the same
    /// state-change hooks that already drive in-app notifications (readiness changes,
    /// care request status transitions, booking commitment status transitions) so
    /// marketing segmentation stays in step with the funnel state this system tracks.
    /// </summary>
    public interface IMarketingSyncService
    {
        ValueTask EnqueueCaregiverSyncAsync(string caregiverId);
        ValueTask EnqueueClientSyncAsync(string clientId);
    }
}
