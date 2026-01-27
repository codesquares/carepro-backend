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
            _logger.LogWarning("Webhook secret hash not configured. Skipping signature verification.");
            return true;
        }

        if (string.IsNullOrEmpty(signatureHeader))
        {
            _logger.LogWarning("Missing verif-hash header");
            return false;
        }

        // v3 simply compares the header value with your secret hash
        var isValid = signatureHeader == _webhookSecretHash;
        
        if (!isValid)
        {
            _logger.LogWarning("Webhook signature mismatch. Received: {Received}", signatureHeader);
        }

        return isValid;
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
}
