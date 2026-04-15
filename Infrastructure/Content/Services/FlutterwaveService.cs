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
        
        _logger.LogInformation("FlutterwaveService initialized with PublicKey: {PublicKey}", 
            _publicKey.Substring(0, Math.Min(20, _publicKey.Length)) + "...");
    }

    public async Task<string> InitiatePayment(decimal amount, string email, string currency, string txRef, string redirectUrl)
    {
        var client = new RestClient(_baseUrl);
        var request = new RestRequest("/v3/payments", Method.Post);
        request.AddHeader("Authorization", $"Bearer {_secretKey}");
        request.AddHeader("Content-Type", "application/json");

        var body = new
        {
            tx_ref = txRef,
            amount = amount,
            currency = currency,
            redirect_url = redirectUrl,
            customer = new { email = email }
        };

        request.AddJsonBody(body);
        
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
                narration = $"CarePro Recurring Service - {txRef}"
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

            if (json.TryGetProperty("data", out var data) &&
                data.TryGetProperty("card", out var card))
            {
                result.PaymentToken = card.TryGetProperty("token", out var token) ? token.GetString() : null;
                result.CardLastFour = card.TryGetProperty("last_4digits", out var last4) ? last4.GetString() : null;
                result.CardBrand = card.TryGetProperty("type", out var type) ? type.GetString() : null;
                result.CardExpiry = card.TryGetProperty("expiry", out var expiry) ? expiry.GetString() : null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not extract card token from transaction {TransactionId}. Subscription will need manual payment method setup.", transactionId);
        }

        return result;
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
                return new FlutterwaveVerificationResult
                {
                    Success = true,
                    Status = data.GetProperty("status").GetString() ?? string.Empty,
                    TxRef = data.GetProperty("tx_ref").GetString() ?? string.Empty,
                    Amount = data.GetProperty("amount").GetDecimal(),
                    Currency = data.GetProperty("currency").GetString() ?? string.Empty,
                    TransactionId = data.GetProperty("id").GetInt64().ToString()
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
}

public class FlutterwaveVerificationResult
{
    public bool Success { get; set; }
    public string Status { get; set; } = string.Empty;
    public string TxRef { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string TransactionId { get; set; } = string.Empty;
    
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
}
