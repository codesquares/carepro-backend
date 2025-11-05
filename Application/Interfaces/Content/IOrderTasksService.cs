using Application.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    public interface IOrderTasksService
    {
        // Order Tasks Management
        Task<OrderTasksResponseDTO> CreateOrderTasksAsync(CreateOrderTasksRequestDTO request);
        Task<OrderTasksResponseDTO> UpdateOrderTasksAsync(UpdateOrderTasksRequestDTO request);
        Task<OrderTasksResponseDTO> GetOrderTasksByIdAsync(string orderTasksId);
        Task<List<OrderTasksResponseDTO>> GetOrderTasksByClientIdAsync(string clientId);
        Task<bool> DeleteOrderTasksAsync(string orderTasksId);

        // Pricing and Calculation
        Task<OrderTasksPricingDTO> CalculatePricingAsync(string orderTasksId);
        Task<OrderTasksPricingDTO> EstimatePricingAsync(CreateOrderTasksRequestDTO request);

        // Status Management
        Task<bool> MarkAsPendingPaymentAsync(string orderTasksId);
        Task<bool> MarkAsPaidAsync(string orderTasksId, string clientOrderId);
        Task<bool> MarkAsContractGeneratedAsync(string orderTasksId);
        Task<bool> MarkAsCompletedAsync(string orderTasksId);

        // Integration with Orders
        Task<OrderTasksResponseDTO?> GetOrderTasksByClientOrderIdAsync(string clientOrderId);
        Task<bool> LinkToClientOrderAsync(string orderTasksId, string clientOrderId);

        // Contract Data Preparation
        Task<ContractGenerationRequestDTO> PrepareContractDataAsync(string orderTasksId, string paymentTransactionId);
    }
}