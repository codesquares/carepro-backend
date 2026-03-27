using Application.DTOs;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    public interface IClientWalletService
    {
        /// <summary>
        /// Gets the wallet for a client, creating one if it doesn't exist.
        /// </summary>
        Task<ClientWalletDTO> GetOrCreateWalletAsync(string clientId);

        /// <summary>
        /// Credits the client's wallet (e.g., visit cancellation refund).
        /// </summary>
        Task CreditAsync(string clientId, decimal amount, string description, string? orderId = null, string? taskSheetId = null);
    }
}
