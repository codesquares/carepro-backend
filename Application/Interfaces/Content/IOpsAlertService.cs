using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    /// <summary>
    /// Alerts every (non-deleted) admin. Always in-app; optionally also by email for failures that
    /// need human eyes quickly. Never throws — an alerting failure must not break the flow that raised it.
    /// </summary>
    public interface IOpsAlertService
    {
        Task NotifyAdminsAsync(string type, string title, string content, string relatedEntityId, bool alsoEmail = false);
    }
}
