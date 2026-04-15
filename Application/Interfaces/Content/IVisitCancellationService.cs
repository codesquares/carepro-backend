using Application.DTOs;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    public interface IVisitCancellationService
    {
        /// <summary>
        /// Cancels an upcoming visit (task sheet) on behalf of the client.
        /// Applies 3-tier refund policy: 24h+ = 100% client, 12-24h = 50/50, &lt;12h = 100% caregiver.
        /// Credits the client's wallet accordingly.
        /// Notifies the caregiver via in-app notification and email.
        /// </summary>
        Task<CancelVisitResponse> CancelVisitAsync(string orderId, CancelVisitRequest request, string clientId);

        /// <summary>
        /// Caregiver requests cancellation of an upcoming visit.
        /// Notifies the client so they can cancel through the platform.
        /// The caregiver must provide at least 24 hours notice.
        /// </summary>
        Task<CancelVisitResponse> CaregiverRequestCancellationAsync(string orderId, CaregiverCancelVisitRequest request, string caregiverId);
    }
}
