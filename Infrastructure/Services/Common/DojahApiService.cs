using Application.DTOs;
using Application.Interfaces.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Infrastructure.Services.Common
{
    public class DojahApiService : IDojahApiService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _config;
        private readonly ILogger<DojahApiService> _logger;
        private readonly string _baseUrl = "https://api.dojah.io";

        public DojahApiService(HttpClient httpClient, IConfiguration config, ILogger<DojahApiService> logger)
        {
            _httpClient = httpClient;
            _config = config;
            _logger = logger;
        }

        public async Task<DojahVerificationDataResponse> GetAllVerificationDataAsync(
            string? term = null,
            string? start = null,
            string? end = null,
            string? status = null)
        {
            try
            {
                var appId = _config["Dojah:AppId"];
                var apiKey = _config["Dojah:ApiKey"];

                if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(apiKey))
                {
                    _logger.LogError("Dojah credentials not configured");
                    return new DojahVerificationDataResponse
                    {
                        Status = false,
                        Message = "Dojah credentials not configured"
                    };
                }

                // Build query string
                var queryParams = new List<string>();
                if (!string.IsNullOrEmpty(term)) queryParams.Add($"term={Uri.EscapeDataString(term)}");
                if (!string.IsNullOrEmpty(start))
                {
                    var formattedStart = ConvertToRequestedDateFormat(start);
                    queryParams.Add($"start={Uri.EscapeDataString(formattedStart)}");
                }
                if (!string.IsNullOrEmpty(end))
                {
                    var formattedEnd = ConvertToRequestedDateFormat(end);
                    queryParams.Add($"end={Uri.EscapeDataString(formattedEnd)}");
                }
                if (!string.IsNullOrEmpty(status)) queryParams.Add($"status={Uri.EscapeDataString(status)}");

                var queryString = queryParams.Count > 0 ? "?" + string.Join("&", queryParams) : "";
                var url = $"{_baseUrl}/api/v1/kyc/verifications{queryString}";

                // Set headers
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("AppId", appId);
                _httpClient.DefaultRequestHeaders.Add("Authorization", apiKey);

                _logger.LogInformation("Calling Dojah API: {Url}", url);

                var response = await _httpClient.GetAsync(url);
                var content = await response.Content.ReadAsStringAsync();

                _logger.LogInformation("Dojah API response status: {StatusCode}", response.StatusCode);
                _logger.LogDebug("Dojah API response content: {Content}", content);

                if (response.IsSuccessStatusCode)
                {
                    try
                    {
                        var result = JsonSerializer.Deserialize<DojahVerificationDataResponse>(content, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                        return result ?? new DojahVerificationDataResponse
                        {
                            Status = false,
                            Message = "Failed to deserialize response"
                        };
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogError(ex, "Failed to deserialize Dojah API response");
                        return new DojahVerificationDataResponse
                        {
                            Status = false,
                            Message = "Failed to parse API response",
                            Data = new List<DojahVerificationEntry>
                            {
                                new DojahVerificationEntry
                                {
                                    Id = "raw-response",
                                    ReferenceId = "raw-data",
                                    Email = "raw-response@dojah.com",
                                    Status = "Raw",
                                    VerificationType = "Raw Response",
                                    CreatedAt = DateTime.UtcNow,
                                    UpdatedAt = DateTime.UtcNow,
                                    RawData = content
                                }
                            }
                        };
                    }
                }
                else
                {
                    _logger.LogError("Dojah API error: {StatusCode} - {Content}", response.StatusCode, content);
                    return new DojahVerificationDataResponse
                    {
                        Status = false,
                        Message = $"API error: {response.StatusCode} - {content}"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling Dojah API");
                return new DojahVerificationDataResponse
                {
                    Status = false,
                    Message = $"Error: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Converts date from ISO format (YYYY-MM-DD) to Dojah expected format (DD/MM/YYYY)
        /// </summary>
        /// <param name="dateString">Date string in ISO format or already in DD/MM/YYYY format</param>
        /// <returns>Date string in DD/MM/YYYY format</returns>
        private string ConvertToRequestedDateFormat(string dateString)
        {
            if (string.IsNullOrEmpty(dateString))
                return dateString;

            // If already in DD/MM/YYYY format, return as is
            if (dateString.Contains("/"))
                return dateString;

            // Try to parse as ISO date (YYYY-MM-DD) and convert to DD/MM/YYYY
            if (DateTime.TryParseExact(dateString, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var parsedDate))
            {
                return parsedDate.ToString("dd/MM/yyyy");
            }

            // Try to parse as standard date and convert to DD/MM/YYYY
            if (DateTime.TryParse(dateString, out var generalDate))
            {
                return generalDate.ToString("dd/MM/yyyy");
            }

            // If parsing fails, return original string
            _logger.LogWarning("Could not parse date string: {DateString}", dateString);
            return dateString;
        }
    }
}