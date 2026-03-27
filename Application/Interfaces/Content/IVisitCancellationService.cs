using Application.DTOs;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    public interface IVisitCancellationService
    {
        /// <summary>
        /// Cancels an upcoming visit (task sheet) on behalf of the client.
        /// Enforces 24-hour advance notice.
        /// Credits the client's wallet with the per-visit amount.
        /// Notifies the caregiver via in-app notification and email.
        /// </summary>
        Task<CancelVisitResponse> CancelVisitAsync(string orderId, CancelVisitRequest request, string clientId);
    }
}
