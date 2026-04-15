using Application.DTOs;

namespace Application.Interfaces.Content
{
    public interface IOrderNegotiationService
    {
        /// <summary>
        /// Start a new negotiation session for an order. Either party can initiate.
        /// </summary>
        Task<OrderNegotiationDTO> CreateNegotiationAsync(string userId, CreateNegotiationDTO request);

        /// <summary>
        /// Get the active negotiation for an order, or null if none exists.
        /// </summary>
        Task<OrderNegotiationDTO?> GetNegotiationByOrderIdAsync(string orderId);

        /// <summary>
        /// Get a negotiation by its ID.
        /// </summary>
        Task<OrderNegotiationDTO?> GetNegotiationByIdAsync(string negotiationId);

        /// <summary>
        /// Client updates their proposed tasks, schedule, service details.
        /// Resets caregiverAgreed to false.
        /// </summary>
        Task<OrderNegotiationDTO> ClientUpdateAsync(string negotiationId, string clientId, ClientNegotiationUpdateDTO update);

        /// <summary>
        /// Caregiver updates their proposed tasks and schedule.
        /// Resets clientAgreed to false.
        /// </summary>
        Task<OrderNegotiationDTO> CaregiverUpdateAsync(string negotiationId, string caregiverId, CaregiverNegotiationUpdateDTO update);

        /// <summary>
        /// Client marks agreement with current state.
        /// If caregiver also agreed, status becomes BothAgreed.
        /// </summary>
        Task<OrderNegotiationDTO> ClientAgreeAsync(string negotiationId, string clientId);

        /// <summary>
        /// Caregiver marks agreement with current state.
        /// If client also agreed, status becomes BothAgreed.
        /// </summary>
        Task<OrderNegotiationDTO> CaregiverAgreeAsync(string negotiationId, string caregiverId);

        /// <summary>
        /// Either party abandons the negotiation.
        /// </summary>
        Task<OrderNegotiationDTO> AbandonAsync(string negotiationId, string userId, NegotiationAbandonDTO request);

        /// <summary>
        /// Convert a BothAgreed negotiation into a formal Contract.
        /// Creates the Contract via LLM and returns the contract ID.
        /// </summary>
        Task<OrderNegotiationDTO> ConvertToContractAsync(string negotiationId, string userId);

        /// <summary>
        /// Returns true if a non-terminal negotiation exists for the given order.
        /// Used to block old contract-generate endpoints.
        /// </summary>
        Task<bool> HasActiveNegotiationForOrderAsync(string orderId);
    }
}
