using Application.DTOs;
using Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace Infrastructure.Content.Services
{
    public class DojahDocumentVerificationService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<DojahDocumentVerificationService> _logger;
        private readonly string _apiBaseUrl;
        private readonly string _appId;
        private readonly string _secretKey;

        public DojahDocumentVerificationService(
            HttpClient httpClient,
            ILogger<DojahDocumentVerificationService> logger,
            IConfiguration configuration)
        {
            _httpClient = httpClient;
            _logger = logger;
            _apiBaseUrl = configuration["DojahDocumentSettings:ApiBaseUrl"] ?? "https://api.dojah.io";
            _appId = configuration["DojahDocumentSettings:AppId"] ?? "";
            _secretKey = configuration["DojahDocumentSettings:SecretKey"] ?? "";
        }

        public async Task<DocumentVerificationResult> VerifyDocumentAsync(byte[] imageBytes, string fileName)
        {
            try
            {
                _logger.LogInformation("Starting document verification for file: {FileName}", fileName);

                // Convert to base64 without data URI prefix
                var base64Image = Convert.ToBase64String(imageBytes);

                // Prepare the request payload
                var requestPayload = new
                {
                    input_type = "base64",
                    imagefrontside = base64Image
                };

                var jsonPayload = JsonSerializer.Serialize(requestPayload);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                // Add required headers
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("AppId", _appId);
                _httpClient.DefaultRequestHeaders.Add("Authorization", _secretKey);

                // Make the API call
                var response = await _httpClient.PostAsync($"{_apiBaseUrl}/api/v1/document/analysis", content);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    _logger.LogInformation("Document verification successful for file: {FileName}", fileName);
                    
                    return ParseDojahResponse(responseContent);
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Document verification failed. Status: {StatusCode}, Content: {Content}", 
                        response.StatusCode, errorContent);
                    
                    return new DocumentVerificationResult
                    {
                        Status = DocumentVerificationStatus.VerificationFailed,
                        IsSuccessful = false,
                        ErrorMessage = $"API call failed with status: {response.StatusCode}",
                        Confidence = 0
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception occurred during document verification for file: {FileName}", fileName);
                
                return new DocumentVerificationResult
                {
                    Status = DocumentVerificationStatus.VerificationFailed,
                    IsSuccessful = false,
                    ErrorMessage = ex.Message,
                    Confidence = 0
                };
            }
        }

        private DocumentVerificationResult ParseDojahResponse(string responseContent)
        {
            try
            {
                using var document = JsonDocument.Parse(responseContent);
                var entity = document.RootElement.GetProperty("entity");
                var status = entity.GetProperty("status");

                var overallStatus = status.GetProperty("overall_status").GetInt32();
                var reason = status.GetProperty("reason").GetString() ?? "";

                var result = new DocumentVerificationResult
                {
                    IsSuccessful = true,
                    RawResponse = responseContent,
                    Confidence = CalculateConfidence(status)
                };

                // Map Dojah status to our internal status
                result.Status = overallStatus == 1 
                    ? DocumentVerificationStatus.Verified 
                    : DocumentVerificationStatus.Invalid;

                // Extract document information if available
                if (entity.TryGetProperty("text_data", out var textData) && textData.ValueKind == JsonValueKind.Array)
                {
                    result.ExtractedInfo = ExtractDocumentInfo(textData);
                }

                // Extract document type information
                if (entity.TryGetProperty("document_type", out var docType))
                {
                    result.DocumentType = ExtractDocumentTypeInfo(docType);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing Dojah response");
                
                return new DocumentVerificationResult
                {
                    Status = DocumentVerificationStatus.VerificationFailed,
                    IsSuccessful = false,
                    ErrorMessage = "Failed to parse verification response",
                    Confidence = 0,
                    RawResponse = responseContent
                };
            }
        }

        private decimal CalculateConfidence(JsonElement status)
        {
            try
            {
                var factors = new List<bool>();
                
                // Check various quality indicators
                if (status.TryGetProperty("document_type", out var docType))
                    factors.Add(docType.GetString() == "Yes");
                
                if (status.TryGetProperty("document_images", out var docImages))
                    factors.Add(docImages.GetString() == "Yes");
                
                if (status.TryGetProperty("text", out var text))
                    factors.Add(text.GetString() == "Yes");

                if (factors.Count == 0) return 0.5m;

                var positiveFactors = factors.Count(f => f);
                return (decimal)positiveFactors / factors.Count;
            }
            catch
            {
                return 0.5m; // Default confidence if we can't calculate
            }
        }

        private CertificateExtractedInfo ExtractDocumentInfo(JsonElement textData)
        {
            var extractedInfo = new CertificateExtractedInfo();

            try
            {
                foreach (var item in textData.EnumerateArray())
                {
                    if (item.TryGetProperty("field_key", out var fieldKey) && 
                        item.TryGetProperty("value", out var value) &&
                        item.TryGetProperty("status", out var status) &&
                        status.GetInt32() == 1) // Only use successfully extracted fields
                    {
                        var key = fieldKey.GetString();
                        var val = value.GetString();

                        switch (key)
                        {
                            case "first_name":
                                extractedInfo.FirstName = val;
                                break;
                            case "last_name":
                                extractedInfo.LastName = val;
                                break;
                            case "given_names":
                                extractedInfo.FullName = val;
                                break;
                            case "document_number":
                                extractedInfo.DocumentNumber = val;
                                break;
                            case "expiry_date":
                                if (DateTime.TryParse(val, out var expiryDate))
                                    extractedInfo.ExpiryDate = expiryDate;
                                break;
                            case "issue_date":
                                if (DateTime.TryParse(val, out var issueDate))
                                    extractedInfo.IssueDate = issueDate;
                                break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error extracting document information from Dojah response");
            }

            return extractedInfo;
        }

        private DocumentTypeInfo ExtractDocumentTypeInfo(JsonElement docType)
        {
            try
            {
                return new DocumentTypeInfo
                {
                    DocumentName = docType.TryGetProperty("document_name", out var name) ? name.GetString() : null,
                    CountryName = docType.TryGetProperty("document_country_name", out var country) ? country.GetString() : null,
                    CountryCode = docType.TryGetProperty("document_country_code", out var code) ? code.GetString() : null
                };
            }
            catch
            {
                return new DocumentTypeInfo();
            }
        }
    }

    public class DocumentVerificationResult
    {
        public DocumentVerificationStatus Status { get; set; }
        public bool IsSuccessful { get; set; }
        public string? ErrorMessage { get; set; }
        public decimal Confidence { get; set; }
        public string? RawResponse { get; set; }
        public CertificateExtractedInfo? ExtractedInfo { get; set; }
        public DocumentTypeInfo? DocumentType { get; set; }
    }

    public class CertificateExtractedInfo
    {
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? FullName { get; set; }
        public string? DocumentNumber { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public DateTime? IssueDate { get; set; }
    }

    public class DocumentTypeInfo
    {
        public string? DocumentName { get; set; }
        public string? CountryName { get; set; }
        public string? CountryCode { get; set; }
    }
}