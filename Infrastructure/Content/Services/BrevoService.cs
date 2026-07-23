using Application.DTOs;
using Application.Interfaces.Content;
using Domain.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace Infrastructure.Content.Services
{
    public class BrevoService : IBrevoService
    {
        private readonly HttpClient _httpClient;
        private readonly BrevoSettings _settings;
        private readonly ILogger<BrevoService> _logger;

        public BrevoService(HttpClient httpClient, IOptions<BrevoSettings> settings, ILogger<BrevoService> logger)
        {
            _httpClient = httpClient;
            _settings = settings.Value;
            _logger = logger;
        }

        public async Task UpsertContactAsync(
            string email,
            Dictionary<string, object> attributes,
            List<int>? addToListIds = null,
            List<int>? removeFromListIds = null)
        {
            if (!_settings.IsConfigured)
            {
                _logger.LogDebug("Brevo API key not configured — skipping contact sync for {Email}", email);
                return;
            }

            var request = new BrevoUpsertContactRequest
            {
                Email = email,
                Attributes = attributes,
                ListIds = addToListIds is { Count: > 0 } ? addToListIds : null,
                UnlinkListIds = removeFromListIds is { Count: > 0 } ? removeFromListIds : null,
                UpdateEnabled = true
            };

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_settings.BaseUrl.TrimEnd('/')}/contacts")
            {
                Content = JsonContent.Create(request)
            };
            httpRequest.Headers.TryAddWithoutValidation("api-key", _settings.ApiKey);
            httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            try
            {
                var response = await _httpClient.SendAsync(httpRequest);
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning(
                        "Brevo contact upsert failed for {Email}: {StatusCode} {Body}",
                        email, response.StatusCode, body);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Brevo contact upsert threw for {Email}", email);
            }
        }
    }
}
