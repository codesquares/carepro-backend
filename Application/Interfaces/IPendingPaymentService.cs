using Application.DTOs;
using Domain.Entities;

namespace Application.Interfaces
{
    public interface IPendingPaymentService
    {
        /// <summary>
        /// Creates a pending payment with server-calculated amounts
        /// </summary>
        Task<Result<PendingPaymentResponse>> CreatePendingPaymentAsync(InitiatePaymentRequest request, string clientId);
        
        /// <summary>
        /// Gets a pending payment by transaction reference
        /// </summary>
        Task<PendingPayment?> GetByTransactionReferenceAsync(string transactionReference);
        
        /// <summary>
        /// Marks a payment as completed after Flutterwave webhook verification
        /// </summary>
        Task<Result<PendingPayment>> CompletePaymentAsync(string transactionReference, string flutterwaveTransactionId, decimal paidAmount);
        
        /// <summary>
        /// Marks a payment as failed
        /// </summary>
        Task<Result<PendingPayment>> FailPaymentAsync(string transactionReference, string errorMessage);
        
        /// <summary>
        /// Gets payment status with full breakdown
        /// </summary>
        Task<Result<PaymentStatusResponse>> GetPaymentStatusAsync(string transactionReference);
    }
}
