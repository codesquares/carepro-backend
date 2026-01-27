using MongoDB.Bson;
using System;

namespace Domain.Entities
{
    /// <summary>
    /// Stores server-calculated payment details before sending to Flutterwave.
    /// This ensures we can verify the paid amount matches what we expected.
    /// </summary>
    public class PendingPayment
    {
        public ObjectId Id { get; set; }
        
        /// <summary>
        /// Unique transaction reference sent to Flutterwave (tx_ref)
        /// </summary>
        public string TransactionReference { get; set; } = string.Empty;
        
        /// <summary>
        /// The gig being purchased
        /// </summary>
        public string GigId { get; set; } = string.Empty;
        
        /// <summary>
        /// The client making the payment
        /// </summary>
        public string ClientId { get; set; } = string.Empty;
        
        /// <summary>
        /// Customer email for Flutterwave
        /// </summary>
        public string Email { get; set; } = string.Empty;
        
        /// <summary>
        /// Service type: "one-time", "weekly", or "monthly"
        /// </summary>
        public string ServiceType { get; set; } = string.Empty;
        
        /// <summary>
        /// Frequency per week (1-7), relevant for weekly/monthly services
        /// </summary>
        public int FrequencyPerWeek { get; set; } = 1;
        
        /// <summary>
        /// Base price from the Gig record
        /// </summary>
        public decimal BasePrice { get; set; }
        
        /// <summary>
        /// Calculated order fee based on service type and frequency
        /// </summary>
        public decimal OrderFee { get; set; }
        
        /// <summary>
        /// Service charge (10% of order fee)
        /// </summary>
        public decimal ServiceCharge { get; set; }
        
        /// <summary>
        /// Estimated Flutterwave processing fees
        /// </summary>
        public decimal FlutterwaveFees { get; set; }
        
        /// <summary>
        /// Total amount to charge (orderFee + serviceCharge + flutterwaveFees)
        /// </summary>
        public decimal TotalAmount { get; set; }
        
        /// <summary>
        /// Currency code (default: NGN)
        /// </summary>
        public string Currency { get; set; } = "NGN";
        
        /// <summary>
        /// Redirect URL after payment
        /// </summary>
        public string RedirectUrl { get; set; } = string.Empty;
        
        /// <summary>
        /// Payment status: Pending, Completed, Failed, Expired
        /// </summary>
        public PendingPaymentStatus Status { get; set; } = PendingPaymentStatus.Pending;
        
        /// <summary>
        /// Flutterwave's transaction ID (populated after payment)
        /// </summary>
        public string? FlutterwaveTransactionId { get; set; }
        
        /// <summary>
        /// Flutterwave payment link
        /// </summary>
        public string? PaymentLink { get; set; }
        
        /// <summary>
        /// When the pending payment was created
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        /// <summary>
        /// When the payment was completed (if successful)
        /// </summary>
        public DateTime? CompletedAt { get; set; }
        
        /// <summary>
        /// The ClientOrder ID created after successful payment
        /// </summary>
        public string? ClientOrderId { get; set; }
        
        /// <summary>
        /// Any error message if payment failed
        /// </summary>
        public string? ErrorMessage { get; set; }
    }
    
    public enum PendingPaymentStatus
    {
        Pending,
        Completed,
        Failed,
        Expired,
        AmountMismatch  // Security flag for potential tampering
    }
}
