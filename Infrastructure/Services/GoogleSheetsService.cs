using Application.Interfaces;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

public class GoogleSheetsService : IGoogleSheetsService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<GoogleSheetsService> _logger;

    public GoogleSheetsService(IConfiguration configuration, ILogger<GoogleSheetsService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task AppendSignupDataAsync(string firstName, string lastName, string phoneNumber, string email, string userType)
    {
        try
        {
            // Check if Google Sheets is configured (production only)
            var spreadsheetId = _configuration["GoogleSheets:SpreadsheetId"];
            if (string.IsNullOrEmpty(spreadsheetId))
            {
                _logger.LogDebug("Google Sheets not configured. Skipping signup tracking.");
                return;
            }

            var credential = GetCredential();
            if (credential == null)
            {
                _logger.LogWarning("Google Sheets credentials not available. Skipping signup tracking.");
                return;
            }

            var service = new SheetsService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = "CarePro"
            });

            var sheetName = _configuration["GoogleSheets:SheetName"] ?? "Signups";
            
            var values = new List<IList<object>>
            {
                new List<object> 
                { 
                    DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"), 
                    firstName ?? "", 
                    lastName ?? "", 
                    phoneNumber ?? "", 
                    email ?? "", 
                    userType ?? "" 
                }
            };

            var range = $"{sheetName}!A:F";
            var valueRange = new ValueRange { Values = values };
            var request = service.Spreadsheets.Values.Append(valueRange, spreadsheetId, range);
            request.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.RAW;
            
            await request.ExecuteAsync();
            
            _logger.LogInformation("Successfully logged signup to Google Sheets: {Email} ({UserType})", email, userType);
        }
        catch (Exception ex)
        {
            // Log error but don't throw - we don't want to fail signup if Sheets logging fails
            _logger.LogError(ex, "Failed to append signup data to Google Sheets for {Email}", email);
        }
    }

    private GoogleCredential? GetCredential()
    {
        try
        {
            // First try to get from GoogleSheets:CredentialsJson (preferred format)
            var credentialsJson = _configuration["GoogleSheets:CredentialsJson"];
            
            // Fallback to GoogleSheets:Credentials (environment variable format)
            if (string.IsNullOrEmpty(credentialsJson))
            {
                credentialsJson = _configuration["GoogleSheets:Credentials"];
            }
            
            if (string.IsNullOrEmpty(credentialsJson))
            {
                _logger.LogDebug("Google Sheets credentials not found in configuration.");
                return null;
            }

            return GoogleCredential.FromJson(credentialsJson)
                .CreateScoped(SheetsService.Scope.Spreadsheets);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Google credential from configuration");
            return null;
        }
    }
}
