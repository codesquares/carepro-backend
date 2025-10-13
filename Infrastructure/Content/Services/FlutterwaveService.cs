using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
//using Microsoft.DotNet.Scaffolding.Shared.CodeModifier.CodeChange;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Org.BouncyCastle.Asn1.Crmf;
using RestSharp;

public class FlutterwaveService
{
    private readonly string _secretKey;
    private readonly string _baseUrl = "https://api.flutterwave.com/v3";

    public FlutterwaveService(IConfiguration configuration)
    {
        _secretKey = configuration["Flutterwave:SecretKey"];
    }

    public async Task<string> InitiatePayment(decimal amount, string email, string currency, string txRef, string redirectUrl)
    {
        var client = new RestClient(_baseUrl);
        var request = new RestRequest("/payments", Method.Post);
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
        var response = await client.ExecuteAsync(request);

        return response.Content;
    }

    public async Task<string> VerifyPayment(string transactionId)
    {
        var client = new RestClient(_baseUrl);
        var request = new RestRequest($"/transactions/{transactionId}/verify", Method.Get);
        request.AddHeader("Authorization", $"Bearer {_secretKey}");

        var response = await client.ExecuteAsync(request);
        return response.Content;
    }
}
