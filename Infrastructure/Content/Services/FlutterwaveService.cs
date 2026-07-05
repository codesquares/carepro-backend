using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using RestSharp;

/// <summary>
/// Flutterwave v3 API Service
/// </summary>
public class FlutterwaveService
{
    private readonly string _publicKey;
    private readonly string _secretKey;
    private readonly string _encryptionKey;
    private readonly string _webhookSecretHash;
    private readonly string _baseUrl = "https://api.flutterwave.com";
    private readonly string _frontendUrl;
    private readonly ILogger<FlutterwaveService> _logger;

    public FlutterwaveService(IConfiguration configuration, ILogger<FlutterwaveService> logger)
    {
        _logger = logger;
        
        _publicKey = configuration["Flutterwave:PublicKey"] 
            ?? Environment.GetEnvironmentVariable("FLUTTERWAVE_PUBLIC_KEY")
            ?? throw new InvalidOperationException("Flutterwave PublicKey is not configured");
        
        _secretKey = configuration["Flutterwave:SecretKey"]
            ?? Environment.GetEnvironmentVariable("FLUTTERWAVE_SECRET_KEY")
            ?? throw new InvalidOperationException("Flutterwave SecretKey is not configured");
        
        _encryptionKey = configuration["Flutterwave:EncryptionKey"]
            ?? Environment.GetEnvironmentVariable("FLUTTERWAVE_ENCRYPTION_KEY")
            ?? string.Empty;
        
        _webhookSecretHash = configuration["Flutterwave:WebhookSecretHash"]
            ?? Environment.GetEnvironmentVariable("FLUTTERWAVE_WEBHOOK_SECRET_HASH")
            ?? string.Empty;

        _frontendUrl = configuration["FrontendUrl"]
            ?? Environment.GetEnvironmentVariable("FRONTEND_URL")
            ?? "https://oncarepro.com";
        
        _logger.LogInformation("FlutterwaveService initialized with PublicKey: {PublicKey}", 
            _publicKey.Substring(0, Math.Min(20, _publicKey.Length)) + "...");
    }

    public async Task<string> InitiatePayment(decimal amount, string email, string currency, string txRef, string redirectUrl, string? paymentOptions = null)
    {
        var client = new RestClient(_baseUrl);
        var request = new RestRequest("/v3/payments", Method.Post);
        request.AddHeader("Authorization", $"Bearer {_secretKey}");
        request.AddHeader("Content-Type", "application/json");

        if (!string.IsNullOrEmpty(paymentOptions))
        {
            request.AddJsonBody(new
            {
                tx_ref = txRef,
                amount = amount,
                currency = currency,
                redirect_url = redirectUrl,
                payment_options = paymentOptions,
                customer = new { email = email }
            });
        }
        else
        {
            request.AddJsonBody(new
            {
                tx_ref = txRef,
                amount = amount,
                currency = currency,
                redirect_url = redirectUrl,
                customer = new { email = email }
            });
        }
        
        _logger.LogInformation("Initiating Flutterwave payment: TxRef={TxRef}, Amount={Amount} {Currency}", 
            txRef, amount, currency);
        
        var response = await client.ExecuteAsync(request);

        _logger.LogInformation("Flutterwave InitiatePayment response: {StatusCode}, Content: {Content}", 
            response.StatusCode, response.Content?.Substring(0, Math.Min(500, response.Content?.Length ?? 0)));
        
        return response.Content ?? string.Empty;
    }

    public async Task<string> VerifyPayment(string transactionId)
    {
        var client = new RestClient(_baseUrl);
        var request = new RestRequest($"/v3/transactions/{transactionId}/verify", Method.Get);
        request.AddHeader("Authorization", $"Bearer {_secretKey}");

        var response = await client.ExecuteAsync(request);
        return response.Content ?? string.Empty;
    }

    /// <summary>
    /// Verifies the webhook signature from Flutterwave v3
    /// v3 uses verif-hash header with the webhook secret hash
    /// </summary>
    public bool VerifyWebhookSignature(string? rawBody, string? signatureHeader)
    {
        if (string.IsNullOrEmpty(_webhookSecretHash))
        {
            _logger.LogCritical("SECURITY: Webhook secret hash not configured. Rejecting webhook to prevent unsigned payloads.");
            return false; // FAIL-CLOSED: never accept unverified webhooks
        }

        if (string.IsNullOrEmpty(signatureHeader))
        {
            _logger.LogWarning("SECURITY: Missing verif-hash header in webhook request.");
            return false;
        }

        // Constant-time comparison to prevent timing attacks
        var isValid = CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(signatureHeader),
            System.Text.Encoding.UTF8.GetBytes(_webhookSecretHash));
        
        if (!isValid)
        {
            _logger.LogWarning("SECURITY: Webhook signature mismatch. Received: {Received}", signatureHeader);
        }

        return isValid;
    }

    /// <summary>
    /// Charges a card using a saved Flutterwave payment token (for recurring billing).
    /// Uses Flutterwave v3 tokenized charge endpoint.
    /// </summary>
    public async Task<FlutterwaveChargeResult?> ChargeWithToken(
        string token, decimal amount, string currency, string email, string txRef)
    {
        try
        {
            var client = new RestClient(_baseUrl);
            var request = new RestRequest("/v3/tokenized-charges", Method.Post);
            request.AddHeader("Authorization", $"Bearer {_secretKey}");
            request.AddHeader("Content-Type", "application/json");

            var body = new
            {
                token = token,
                currency = currency,
                amount = amount,
                email = email,
                tx_ref = txRef,
                narration = $"CarePro Recurring Service - {txRef}",
                redirect_url = $"{_frontendUrl}/subscription/payment-confirmed"
            };

            request.AddJsonBody(body);

            _logger.LogInformation(
                "Initiating tokenized charge: TxRef={TxRef}, Amount={Amount} {Currency}",
                txRef, amount, currency);

            var response = await client.ExecuteAsync(request);

            if (string.IsNullOrEmpty(response.Content))
            {
                _logger.LogError("Empty response from Flutterwave tokenized charge");
                return new FlutterwaveChargeResult { Success = false, ErrorMessage = "Empty response from payment provider" };
            }

            var result = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(response.Content);

            if (result.TryGetProperty("status", out var status) && status.GetString() == "success" &&
                result.TryGetProperty("data", out var data))
            {
                var chargeStatus = data.GetProperty("status").GetString() ?? string.Empty;
                if (chargeStatus.ToLower() == "successful")
                {
                    return new FlutterwaveChargeResult
                    {
                        Success = true,
                        TransactionId = data.GetProperty("id").GetInt64().ToString(),
                        Status = chargeStatus,
                        Amount = data.GetProperty("amount").GetDecimal()
                    };
                }
                else if (chargeStatus.ToLower() == "pending")
                {
                    // v3 tokenization pending responses commonly return either
                    // data.redirect_url or data.meta.authorization.redirect.
                    var authUrl = ResolveTokenizedAuthUrl(data);
                    _logger.LogInformation(
                        "Tokenized charge requires 3DS for TxRef {TxRef}. AuthUrl present: {HasAuthUrl}",
                        txRef, authUrl != null);
                    return new FlutterwaveChargeResult
                    {
                        Success = false,
                        IsPending = true,
                        Status = chargeStatus,
                        AuthUrl = authUrl,
                        ErrorMessage = "Transaction is pending authentication"
                    };
                }
                else
                {
                    var processorResponse = data.TryGetProperty("processor_response", out var pr)
                        ? pr.GetString() : "Charge not successful";
                    return new FlutterwaveChargeResult
                    {
                        Success = false,
                        Status = chargeStatus,
                        ErrorMessage = processorResponse
                    };
                }
            }

            var errorMsg = result.TryGetProperty("message", out var msg) ? msg.GetString() : "Unknown error";
            return new FlutterwaveChargeResult { Success = false, ErrorMessage = errorMsg };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error charging token for TxRef {TxRef}", txRef);
            return new FlutterwaveChargeResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    /// <summary>
    /// Charges a customer using Flutterwave tokenization contract:
    /// customer_id + payment_method_id + recurring=true.
    /// </summary>
    public async Task<FlutterwaveChargeResult?> ChargeRecurringWithPaymentMethod(
        string customerId,
        string paymentMethodId,
        decimal amount,
        string currency,
        string txRef,
        string? redirectUrl = null)
    {
        try
        {
            var client = new RestClient(_baseUrl);
            var request = new RestRequest("/v3/charges", Method.Post);
            request.AddHeader("Authorization", $"Bearer {_secretKey}");
            request.AddHeader("Content-Type", "application/json");

            var body = new
            {
                tx_ref = txRef,
                amount = amount,
                currency = currency,
                customer_id = customerId,
                payment_method_id = paymentMethodId,
                recurring = true,
                redirect_url = redirectUrl ?? $"{_frontendUrl}/subscription/payment-confirmed"
            };

            request.AddJsonBody(body);

            _logger.LogInformation(
                "Initiating recurring charge via payment_method_id: TxRef={TxRef}, Amount={Amount} {Currency}, CustomerId={CustomerId}, PaymentMethodId={PaymentMethodId}",
                txRef,
                amount,
                currency,
                customerId,
                paymentMethodId);

            var response = await client.ExecuteAsync(request);
            if (string.IsNullOrEmpty(response.Content))
            {
                _logger.LogError("Empty response from Flutterwave recurring charge-by-payment-method for TxRef={TxRef}", txRef);
                return new FlutterwaveChargeResult { Success = false, ErrorMessage = "Empty response from payment provider" };
            }

            var root = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(response.Content);
            if (!root.TryGetProperty("status", out var rootStatus) || rootStatus.GetString() != "success" ||
                !root.TryGetProperty("data", out var data))
            {
                var message = root.TryGetProperty("message", out var msg)
                    ? msg.GetString()
                    : "Recurring charge request failed";
                return new FlutterwaveChargeResult { Success = false, ErrorMessage = message };
            }

            var chargeStatus = ResolveChargeStatus(data);
            var statusLower = (chargeStatus ?? string.Empty).Trim().ToLowerInvariant();
            var transactionId = ResolveTransactionId(data);
            var resolvedAmount = ResolveAmount(data, amount);

            if (statusLower is "successful" or "succeeded")
            {
                return new FlutterwaveChargeResult
                {
                    Success = true,
                    Status = chargeStatus,
                    TransactionId = transactionId,
                    Amount = resolvedAmount
                };
            }

            if (statusLower == "pending")
            {
                var authUrl = ResolveAuthUrl(data);
                _logger.LogInformation(
                    "Recurring charge pending authorization for TxRef={TxRef}. AuthUrlPresent={HasAuthUrl}",
                    txRef,
                    !string.IsNullOrWhiteSpace(authUrl));

                return new FlutterwaveChargeResult
                {
                    Success = false,
                    IsPending = true,
                    Status = chargeStatus,
                    AuthUrl = authUrl,
                    ErrorMessage = "Transaction is pending authentication"
                };
            }

            var processorResponse = ResolveProcessorResponse(data) ?? "Charge not successful";
            return new FlutterwaveChargeResult
            {
                Success = false,
                Status = chargeStatus,
                ErrorMessage = processorResponse
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error initiating recurring charge via payment_method_id for TxRef={TxRef}, CustomerId={CustomerId}, PaymentMethodId={PaymentMethodId}",
                txRef,
                customerId,
                paymentMethodId);
            return new FlutterwaveChargeResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    /// <summary>
    /// Extracts tokenization data from a successful payment verification.
    /// Call after initial payment to get the token for recurring charges.
    /// </summary>
    public async Task<FlutterwaveVerificationResult?> VerifyAndExtractTokenAsync(string transactionId)
    {
        var result = await VerifyTransactionAsync(transactionId);
        if (result == null || !result.Success) return result;

        // Try to extract card token from the verification response
        try
        {
            var response = await VerifyPayment(transactionId);
            var json = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(response);
            System.Text.Json.JsonElement data = default;
            var hasData = json.TryGetProperty("data", out data);

            if (hasData &&
                data.TryGetProperty("card", out var card))
            {
                result.PaymentToken = card.TryGetProperty("token", out var token) ? token.GetString() : null;
                result.CardLastFour = card.TryGetProperty("last_4digits", out var last4) ? last4.GetString() : null;
                result.CardBrand = card.TryGetProperty("type", out var type) ? type.GetString() : null;
                result.CardExpiry = card.TryGetProperty("expiry", out var expiry) ? expiry.GetString() : null;
            }

            if (hasData)
            {
                result.CustomerId = ResolveCustomerId(data);
                result.PaymentMethodId = ResolvePaymentMethodId(data);
            }

            _logger.LogInformation(
                "VerifyAndExtractTokenAsync extracted recurring identity for TxId={TransactionId}. CustomerId={CustomerId}, PaymentMethodId={PaymentMethodId}, HasToken={HasToken}",
                transactionId,
                result.CustomerId ?? "<null>",
                result.PaymentMethodId ?? "<null>",
                !string.IsNullOrWhiteSpace(result.PaymentToken));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not extract card token from transaction {TransactionId}. Subscription will need manual payment method setup.", transactionId);
        }

        return result;
    }

    /// <summary>
    /// Issues a full or partial refund for a transaction via Flutterwave v3.
    /// Calls POST /v3/transactions/{id}/refund.
    /// </summary>
    public async Task<FlutterwaveRefundResult> RefundTransactionAsync(string transactionId, decimal? amount = null)
    {
        try
        {
            var client = new RestClient(_baseUrl);
            var request = new RestRequest($"/v3/transactions/{transactionId}/refund", Method.Post);
            request.AddHeader("Authorization", $"Bearer {_secretKey}");
            request.AddHeader("Content-Type", "application/json");

            // Flutterwave accepts an optional amount for partial refunds.
            // When amount is omitted the full transaction amount is refunded.
            if (amount.HasValue)
            {
                request.AddJsonBody(new { amount = amount.Value });
            }

            _logger.LogInformation(
                "Initiating Flutterwave refund: TransactionId={TransactionId}, Amount={Amount}",
                transactionId, amount?.ToString() ?? "full");

            var response = await client.ExecuteAsync(request);

            if (string.IsNullOrEmpty(response.Content))
            {
                _logger.LogError("Empty response from Flutterwave refund for TransactionId={TransactionId}", transactionId);
                return new FlutterwaveRefundResult { Success = false, ErrorMessage = "Empty response from payment provider" };
            }

            _logger.LogInformation(
                "Flutterwave refund response: {StatusCode}, Content: {Content}",
                response.StatusCode,
                response.Content.Substring(0, Math.Min(500, response.Content.Length)));

            var result = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(response.Content);

            if (result.TryGetProperty("status", out var status) && status.GetString() == "success" &&
                result.TryGetProperty("data", out var data))
            {
                return new FlutterwaveRefundResult
                {
                    Success = true,
                    RefundId = data.TryGetProperty("id", out var id) ? id.ToString() : string.Empty,
                    Status = data.TryGetProperty("status", out var s) ? s.GetString() ?? string.Empty : string.Empty,
                    AmountRefunded = data.TryGetProperty("amount_refunded", out var ar) ? ar.GetDecimal() : (amount ?? 0),
                    TransactionId = transactionId
                };
            }

            var errorMsg = result.TryGetProperty("message", out var msg) ? msg.GetString() : "Refund request failed";
            return new FlutterwaveRefundResult { Success = false, ErrorMessage = errorMsg, TransactionId = transactionId };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error issuing refund for TransactionId={TransactionId}", transactionId);
            return new FlutterwaveRefundResult { Success = false, ErrorMessage = ex.Message, TransactionId = transactionId };
        }
    }

    /// <summary>
    /// Verifies a transaction directly with Flutterwave API
    /// </summary>
    public async Task<FlutterwaveVerificationResult?> VerifyTransactionAsync(string transactionId)
    {
        try
        {
            var response = await VerifyPayment(transactionId);
            var result = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(response);
            
            if (result.TryGetProperty("status", out var status) && status.GetString() == "success" &&
                result.TryGetProperty("data", out var data))
            {
                var resolvedStatus = ResolveChargeStatus(data);
                var resolvedTxRef = ResolveTxRef(data);
                var resolvedTransactionId = ResolveTransactionId(data);

                return new FlutterwaveVerificationResult
                {
                    Success = true,
                    Status = resolvedStatus,
                    TxRef = resolvedTxRef,
                    Amount = ResolveAmount(data),
                    Currency = data.TryGetProperty("currency", out var c) ? c.GetString() ?? string.Empty : string.Empty,
                    TransactionId = resolvedTransactionId,
                    CustomerId = ResolveCustomerId(data),
                    PaymentMethodId = ResolvePaymentMethodId(data)
                };
            }
            
            return new FlutterwaveVerificationResult { Success = false };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying transaction {TransactionId}", transactionId);
            return null;
        }
    }

    /// <summary>
    /// Verifies a transaction using the tx_ref (our internal reference) rather than
    /// the numeric Flutterwave transaction ID. Calls GET /v3/transactions/verify_by_reference.
    /// </summary>
    public async Task<FlutterwaveVerificationResult?> VerifyByTxRefAsync(string txRef)
    {
        try
        {
            var client = new RestClient(_baseUrl);
            var request = new RestRequest("/v3/transactions/verify_by_reference", Method.Get);
            request.AddHeader("Authorization", $"Bearer {_secretKey}");
            request.AddQueryParameter("tx_ref", txRef);

            _logger.LogInformation("Verifying transaction by tx_ref: {TxRef}", txRef);

            var response = await client.ExecuteAsync(request);

            if (string.IsNullOrEmpty(response.Content))
            {
                _logger.LogError("Empty response verifying by tx_ref={TxRef}", txRef);
                return null;
            }

            var result = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(response.Content);

            if (result.TryGetProperty("status", out var status) && status.GetString() == "success" &&
                result.TryGetProperty("data", out var data))
            {
                return new FlutterwaveVerificationResult
                {
                    Success = true,
                    Status = ResolveChargeStatus(data),
                    TxRef = ResolveTxRef(data),
                    Amount = ResolveAmount(data),
                    Currency = data.TryGetProperty("currency", out var c) ? c.GetString() ?? string.Empty : string.Empty,
                    TransactionId = ResolveTransactionId(data),
                    CustomerId = ResolveCustomerId(data),
                    PaymentMethodId = ResolvePaymentMethodId(data)
                };
            }

            var flwMessage = result.TryGetProperty("message", out var msg) ? msg.GetString() : null;
            _logger.LogWarning("Flutterwave verify_by_reference returned non-success for tx_ref={TxRef}. Message: {Message}. Content: {Content}",
                txRef, flwMessage, response.Content.Substring(0, Math.Min(300, response.Content.Length)));
            return new FlutterwaveVerificationResult { Success = false, ErrorMessage = flwMessage };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying transaction by tx_ref={TxRef}", txRef);
            return null;
        }
    }

    private static string ResolveChargeStatus(System.Text.Json.JsonElement data)
    {
        if (data.TryGetProperty("status", out var s))
        {
            return s.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static string ResolveTxRef(System.Text.Json.JsonElement data)
    {
        if (data.TryGetProperty("tx_ref", out var txRef))
        {
            return txRef.GetString() ?? string.Empty;
        }

        if (data.TryGetProperty("reference", out var reference))
        {
            return reference.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static string ResolveTransactionId(System.Text.Json.JsonElement data)
    {
        if (data.TryGetProperty("id", out var id))
        {
            return id.ValueKind switch
            {
                System.Text.Json.JsonValueKind.String => id.GetString() ?? string.Empty,
                System.Text.Json.JsonValueKind.Number => id.GetInt64().ToString(),
                _ => id.ToString()
            };
        }

        return string.Empty;
    }

    private static decimal ResolveAmount(System.Text.Json.JsonElement data, decimal fallback = 0m)
    {
        if (data.TryGetProperty("amount", out var amount))
        {
            if (amount.ValueKind == System.Text.Json.JsonValueKind.Number)
            {
                return amount.GetDecimal();
            }

            if (amount.ValueKind == System.Text.Json.JsonValueKind.String &&
                decimal.TryParse(amount.GetString(), out var parsedAmount))
            {
                return parsedAmount;
            }
        }

        return fallback;
    }

    private static string? ResolveTokenizedAuthUrl(System.Text.Json.JsonElement data)
    {
        // v3 tokenized charge: data.redirect_url is typically the user challenge URL.
        if (data.TryGetProperty("redirect_url", out var redirectUrl) &&
            redirectUrl.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            var directUrl = redirectUrl.GetString();
            if (!string.IsNullOrWhiteSpace(directUrl))
            {
                return directUrl;
            }
        }

        // v3 tokenized charge can also return data.meta.authorization.redirect.
        if (data.TryGetProperty("meta", out var meta) &&
            meta.ValueKind == System.Text.Json.JsonValueKind.Object &&
            meta.TryGetProperty("authorization", out var authorization) &&
            authorization.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            if (authorization.TryGetProperty("redirect", out var redirect) &&
                redirect.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                var authRedirect = redirect.GetString();
                if (!string.IsNullOrWhiteSpace(authRedirect))
                {
                    return authRedirect;
                }
            }
        }

        // Legacy/alternate field.
        if (data.TryGetProperty("auth_url", out var authUrl) &&
            authUrl.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            return authUrl.GetString();
        }

        return null;
    }

    private static string? ResolveCustomerId(System.Text.Json.JsonElement data)
    {
        if (data.TryGetProperty("customer", out var customer))
        {
            if (customer.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                if (customer.TryGetProperty("id", out var customerIdObj))
                {
                    return customerIdObj.ValueKind switch
                    {
                        System.Text.Json.JsonValueKind.String => customerIdObj.GetString(),
                        System.Text.Json.JsonValueKind.Number => customerIdObj.GetInt64().ToString(),
                        _ => customerIdObj.ToString()
                    };
                }
            }
            else if (customer.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                return customer.GetString();
            }
        }

        if (data.TryGetProperty("customer_id", out var customerId))
        {
            return customerId.ValueKind switch
            {
                System.Text.Json.JsonValueKind.String => customerId.GetString(),
                System.Text.Json.JsonValueKind.Number => customerId.GetInt64().ToString(),
                _ => customerId.ToString()
            };
        }

        return null;
    }

    private static string? ResolvePaymentMethodId(System.Text.Json.JsonElement data)
    {
        if (data.TryGetProperty("payment_method", out var paymentMethod))
        {
            if (paymentMethod.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                if (paymentMethod.TryGetProperty("id", out var paymentMethodIdObj))
                {
                    return paymentMethodIdObj.ValueKind switch
                    {
                        System.Text.Json.JsonValueKind.String => paymentMethodIdObj.GetString(),
                        System.Text.Json.JsonValueKind.Number => paymentMethodIdObj.GetInt64().ToString(),
                        _ => paymentMethodIdObj.ToString()
                    };
                }
            }
            else if (paymentMethod.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                return paymentMethod.GetString();
            }
        }

        if (data.TryGetProperty("payment_method_id", out var paymentMethodId))
        {
            return paymentMethodId.ValueKind switch
            {
                System.Text.Json.JsonValueKind.String => paymentMethodId.GetString(),
                System.Text.Json.JsonValueKind.Number => paymentMethodId.GetInt64().ToString(),
                _ => paymentMethodId.ToString()
            };
        }

        return null;
    }

    private static string? ResolveProcessorResponse(System.Text.Json.JsonElement data)
    {
        if (data.TryGetProperty("processor_response", out var processorResponse))
        {
            if (processorResponse.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                return processorResponse.GetString();
            }

            if (processorResponse.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                if (processorResponse.TryGetProperty("type", out var t))
                {
                    return t.GetString();
                }

                return processorResponse.ToString();
            }
        }

        return null;
    }

    private static string? ResolveAuthUrl(System.Text.Json.JsonElement data)
    {
        if (data.TryGetProperty("auth_url", out var authUrl))
        {
            return authUrl.GetString();
        }

        if (data.TryGetProperty("next_action", out var nextAction) &&
            nextAction.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            if (nextAction.TryGetProperty("redirect_url", out var redirectUrl))
            {
                if (redirectUrl.ValueKind == System.Text.Json.JsonValueKind.Object &&
                    redirectUrl.TryGetProperty("url", out var url))
                {
                    return url.GetString();
                }

                if (redirectUrl.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    return redirectUrl.GetString();
                }
            }
        }

        return null;
    }
}

public class FlutterwaveVerificationResult
{
    public bool Success { get; set; }
    public string Status { get; set; } = string.Empty;
    public string TxRef { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string TransactionId { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }

    // Tokenization contract identifiers for recurring charges
    public string? CustomerId { get; set; }
    public string? PaymentMethodId { get; set; }

    // Tokenization fields for recurring payments
    public string? PaymentToken { get; set; }
    public string? CardLastFour { get; set; }
    public string? CardBrand { get; set; }
    public string? CardExpiry { get; set; }
}

/// <summary>
/// Result from a tokenized charge attempt
/// </summary>
public class FlutterwaveChargeResult
{
    public bool Success { get; set; }
    public string TransactionId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? ErrorMessage { get; set; }
    /// <summary>
    /// True when the charge is awaiting 3DS/OTP from the cardholder.
    /// Not a permanent failure — the user must visit AuthUrl to complete payment.
    /// </summary>
    public bool IsPending { get; set; }
    /// <summary>
    /// Flutterwave redirect URL for 3DS/OTP completion. Only set when IsPending is true.
    /// </summary>
    public string? AuthUrl { get; set; }
}

/// <summary>
/// Result from a refund request
/// </summary>
public class FlutterwaveRefundResult
{
    public bool Success { get; set; }
    public string RefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal AmountRefunded { get; set; }
    public string TransactionId { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}
