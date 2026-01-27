namespace Application.DTOs
{
    /// <summary>
    /// Request to initiate a secure payment
    /// </summary>
    public class InitiatePaymentRequest
    {
        /// <summary>
        /// The ID of the gig being purchased
        /// </summary>
        public string GigId { get; set; } = string.Empty;
        
        /// <summary>
        /// Service type: "one-time", "weekly", or "monthly"
        /// </summary>
        public string ServiceType { get; set; } = string.Empty;
        
        /// <summary>
        /// Frequency per week (1-7). Only relevant for weekly/monthly services.
        /// Default is 1.
        /// </summary>
        public int FrequencyPerWeek { get; set; } = 1;
        
        /// <summary>
        /// Customer email for Flutterwave
        /// </summary>
        public string Email { get; set; } = string.Empty;
        
        /// <summary>
        /// Where to redirect after payment completion
        /// </summary>
        public string RedirectUrl { get; set; } = string.Empty;
    }
    
    /// <summary>
    /// Response after initiating a payment
    /// </summary>
    public class PendingPaymentResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string TransactionReference { get; set; } = string.Empty;
        public string? PaymentLink { get; set; }
        public PaymentBreakdown Breakdown { get; set; } = new();
    }
    
    /// <summary>
    /// Detailed breakdown of payment amounts
    /// </summary>
    public class PaymentBreakdown
    {
        public decimal BasePrice { get; set; }
        public string ServiceType { get; set; } = string.Empty;
        public int FrequencyPerWeek { get; set; }
        public decimal OrderFee { get; set; }
        public decimal ServiceCharge { get; set; }
        public decimal FlutterwaveFees { get; set; }
        public decimal TotalAmount { get; set; }
        public string Currency { get; set; } = "NGN";
    }
    
    /// <summary>
    /// Response for payment status query
    /// </summary>
    public class PaymentStatusResponse
    {
        public bool Success { get; set; }
        public string Status { get; set; } = string.Empty;
        public string TransactionReference { get; set; } = string.Empty;
        public string? FlutterwaveTransactionId { get; set; }
        public DateTime? PaymentDate { get; set; }
        public string? ClientOrderId { get; set; }
        public PaymentBreakdown Breakdown { get; set; } = new();
        public string? ErrorMessage { get; set; }
    }
    
    /// <summary>
    /// Flutterwave v3 webhook payload - flat structure (no nested data object)
    /// </summary>
    public class FlutterwaveWebhookPayload
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public long Id { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("txRef")]
        public string? TxRef { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("flwRef")]
        public string? FlwRef { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("orderRef")]
        public string? OrderRef { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("status")]
        public string? Status { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("amount")]
        public decimal Amount { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("charged_amount")]
        public decimal ChargedAmount { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("currency")]
        public string? Currency { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("appfee")]
        public decimal AppFee { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("createdAt")]
        public DateTime? CreatedAt { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("IP")]
        public string? IP { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("customer")]
        public FlutterwaveCustomer? Customer { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("event.type")]
        public string? EventType { get; set; }
    }
    
    public class FlutterwaveCustomer
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public long Id { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("email")]
        public string? Email { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("fullName")]
        public string? FullName { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("phone")]
        public string? Phone { get; set; }
    }
    
    public class FlutterwavePaymentMethod
    {
        public string? Id { get; set; }
        public string? Type { get; set; }
        public string? Customer_id { get; set; }
    }
    
    public class FlutterwaveProcessorResponse
    {
        public string? Code { get; set; }
        public string? Type { get; set; }
    }
}
