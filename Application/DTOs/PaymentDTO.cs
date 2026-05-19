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
        /// <summary>
        /// Amount deducted from a prior booking commitment fee (₦5,000 or 0)
        /// </summary>
        public decimal CommitmentFeeDeducted { get; set; } = 0m;
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
    /// Flutterwave v3 webhook envelope — wraps the payload for charge.completed events.
    /// Structure: { "event": "charge.completed", "data": { ... } }
    /// </summary>
    public class FlutterwaveWebhookEnvelope
    {
        [System.Text.Json.Serialization.JsonPropertyName("event")]
        public string? Event { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("data")]
        public FlutterwaveWebhookPayload? Data { get; set; }
    }

    /// <summary>
    /// Flutterwave v3 webhook payload — fields nested under "data" in the envelope.
    /// Handles both snake_case (production: tx_ref, flw_ref) and camelCase (dev/test: txRef, flwRef).
    /// </summary>
    public class FlutterwaveWebhookPayload
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public long Id { get; set; }

        // snake_case (production) and camelCase (dev/test) variants for TxRef
        [System.Text.Json.Serialization.JsonPropertyName("tx_ref")]
        public string? TxRefSnake { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("txRef")]
        public string? TxRefCamel { get; set; }
        [System.Text.Json.Serialization.JsonIgnore]
        public string? TxRef => TxRefSnake ?? TxRefCamel;

        // snake_case (production) and camelCase (dev/test) variants for FlwRef
        [System.Text.Json.Serialization.JsonPropertyName("flw_ref")]
        public string? FlwRefSnake { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("flwRef")]
        public string? FlwRefCamel { get; set; }
        [System.Text.Json.Serialization.JsonIgnore]
        public string? FlwRef => FlwRefSnake ?? FlwRefCamel;

        // snake_case (production) and camelCase (dev/test) variants for OrderRef
        [System.Text.Json.Serialization.JsonPropertyName("order_ref")]
        public string? OrderRefSnake { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("orderRef")]
        public string? OrderRefCamel { get; set; }
        [System.Text.Json.Serialization.JsonIgnore]
        public string? OrderRef => OrderRefSnake ?? OrderRefCamel;
        
        [System.Text.Json.Serialization.JsonPropertyName("status")]
        public string? Status { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("amount")]
        public decimal Amount { get; set; }

        // snake_case (production: charged_amount) and no-separator (dev/test: charged_amount same)
        [System.Text.Json.Serialization.JsonPropertyName("charged_amount")]
        public decimal ChargedAmountSnake { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("chargedAmount")]
        public decimal ChargedAmountCamel { get; set; }
        [System.Text.Json.Serialization.JsonIgnore]
        public decimal ChargedAmount => ChargedAmountSnake > 0 ? ChargedAmountSnake : ChargedAmountCamel;
        
        [System.Text.Json.Serialization.JsonPropertyName("currency")]
        public string? Currency { get; set; }

        // app_fee (production) and appfee (dev/test, no underscore)
        [System.Text.Json.Serialization.JsonPropertyName("app_fee")]
        public decimal AppFeeSnake { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("appfee")]
        public decimal AppFeeCamel { get; set; }
        [System.Text.Json.Serialization.JsonIgnore]
        public decimal AppFee => AppFeeSnake > 0 ? AppFeeSnake : AppFeeCamel;

        // created_at (production) and createdAt (dev/test)
        [System.Text.Json.Serialization.JsonPropertyName("created_at")]
        public DateTime? CreatedAtSnake { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("createdAt")]
        public DateTime? CreatedAtCamel { get; set; }
        [System.Text.Json.Serialization.JsonIgnore]
        public DateTime? CreatedAt => CreatedAtSnake ?? CreatedAtCamel;
        
        [System.Text.Json.Serialization.JsonPropertyName("ip")]
        public string? IP { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("customer")]
        public FlutterwaveCustomer? Customer { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("payment_type")]
        public string? PaymentType { get; set; }
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
