using Application.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    public interface IBillingRecordService
    {
        /// <summary>
        /// Creates a billing record for a payment (initial or recurring).
        /// </summary>
        Task<BillingRecordDTO> CreateBillingRecordAsync(
            string orderId, string clientId, string caregiverId, string gigId,
            string serviceType, int frequencyPerWeek,
            decimal amountPaid, decimal orderFee, decimal serviceCharge, decimal gatewayFees,
            string paymentTransactionId,
            string? subscriptionId = null, string? contractId = null,
            int billingCycleNumber = 1,
            DateTime? periodStart = null, DateTime? periodEnd = null, DateTime? nextChargeDate = null);

        /// <summary>
        /// Gets a billing record by ID.
        /// </summary>
        Task<BillingRecordDTO?> GetBillingRecordByIdAsync(string id);

        /// <summary>
        /// Gets all billing records for a specific order.
        /// </summary>
        Task<BillingRecordDTO?> GetBillingRecordByOrderIdAsync(string orderId);

        /// <summary>
        /// Gets all billing records for a subscription (all cycles).
        /// </summary>
        Task<List<BillingRecordDTO>> GetBillingRecordsBySubscriptionIdAsync(string subscriptionId);

        /// <summary>
        /// Gets all billing records for a client.
        /// </summary>
        Task<List<BillingRecordDTO>> GetClientBillingRecordsAsync(string clientId);

        /// <summary>
        /// Gets all billing records for a caregiver.
        /// </summary>
        Task<List<BillingRecordDTO>> GetCaregiverBillingRecordsAsync(string caregiverId);

        /// <summary>
        /// Marks a billing record as refunded.
        /// </summary>
        Task<bool> MarkAsRefundedAsync(string billingRecordId);

        /// <summary>
        /// Marks a billing record as disputed.
        /// </summary>
        Task<bool> MarkAsDisputedAsync(string billingRecordId);
    }
}
