using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json.Serialization;

namespace Application.DTOs
{
    public class DojahWebhookWrapper
    {
        public DojahWebhookRequest Request { get; set; } = new();
        public object? Data { get; set; }
    }

    public class DojahWebhookRequest
    {
        [JsonPropertyName("status")]
        public bool Status { get; set; }
        
        [JsonPropertyName("verification_status")]
        public string VerificationStatus { get; set; } = string.Empty; // Ongoing, Abandoned, Completed, Pending, Failed
        
        [JsonPropertyName("reference_id")]
        public string ReferenceId { get; set; } = string.Empty;
        
        [JsonPropertyName("user_id")]
        public string UserId { get; set; } = string.Empty;
        
        [JsonPropertyName("data")]
        public DojahWebhookData? Data { get; set; }
        
        [JsonPropertyName("metadata")]
        public DojahWebhookMetadata? Metadata { get; set; }
        
        [JsonPropertyName("id_type")]
        public string IdType { get; set; } = string.Empty;
        
        [JsonPropertyName("verification_type")]
        public string VerificationType { get; set; } = string.Empty;
        
        [JsonPropertyName("value")]
        public string Value { get; set; } = string.Empty;
        
        [JsonPropertyName("verification_value")]
        public string VerificationValue { get; set; } = string.Empty;
        
        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;
        
        [JsonPropertyName("id_url")]
        public string IdUrl { get; set; } = string.Empty;
        
        [JsonPropertyName("back_url")]
        public string BackUrl { get; set; } = string.Empty;
        
        [JsonPropertyName("selfie_url")]
        public string SelfieUrl { get; set; } = string.Empty;
        
        [JsonPropertyName("verification_url")]
        public string VerificationUrl { get; set; } = string.Empty;
        
        [JsonPropertyName("verification_mode")]
        public string VerificationMode { get; set; } = string.Empty;
        
        [JsonPropertyName("aml")]
        public DojahAmlData? Aml { get; set; }
        
        [JsonPropertyName("widget_id")]
        public string WidgetId { get; set; } = string.Empty;
        
        [JsonPropertyName("signature")]
        public string Signature { get; set; } = string.Empty;
    }

    public class DojahAmlData
    {
        public bool Status { get; set; }
    }

    public class DojahWebhookData
    {
        [JsonPropertyName("government_data")]
        public DojahGovernmentData? GovernmentData { get; set; }
        
        [JsonPropertyName("user_data")]
        public DojahUserData? UserData { get; set; }
        
        [JsonPropertyName("id")]
        public DojahIdData? Id { get; set; }
        
        [JsonPropertyName("email")]
        public DojahEmailData? Email { get; set; }
        
        [JsonPropertyName("selfie")]
        public DojahSelfieData? Selfie { get; set; }
        
        [JsonPropertyName("phone_number")]
        public DojahPhoneData? PhoneNumber { get; set; }
        
        [JsonPropertyName("business_data")]
        public DojahBusinessData? BusinessData { get; set; }
        
        [JsonPropertyName("business_id")]
        public DojahBusinessIdData? BusinessId { get; set; }
        
        [JsonPropertyName("countries")]
        public DojahCountriesData? Countries { get; set; }
        
        [JsonPropertyName("index")]
        public DojahIndexData? Index { get; set; }
        
        [JsonPropertyName("additional_document")]
        public List<DojahAdditionalDocument>? AdditionalDocument { get; set; }
    }

    public class DojahGovernmentData
    {
        [JsonPropertyName("data")]
        public DojahGovernmentDataContent? Data { get; set; }
        
        [JsonPropertyName("status")]
        public bool Status { get; set; }
        
        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;
    }

    public class DojahGovernmentDataContent
    {
        [JsonPropertyName("bvn")]
        public DojahBvnEntity? Bvn { get; set; }
        
        [JsonPropertyName("nin")]
        public DojahNinEntity? Nin { get; set; }
    }

    public class DojahEmailData
    {
        [JsonPropertyName("data")]
        public DojahEmailContent? Data { get; set; }
        
        [JsonPropertyName("status")]
        public bool Status { get; set; }
        
        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;
    }

    public class DojahEmailContent
    {
        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;
    }

    public class DojahSelfieData
    {
        public DojahSelfieContent? Data { get; set; }
        public bool Status { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class DojahSelfieContent
    {
        public string SelfieUrl { get; set; } = string.Empty;
    }

    public class DojahPhoneData
    {
        public DojahPhoneContent? Data { get; set; }
        public bool Status { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class DojahPhoneContent
    {
        public string Phone { get; set; } = string.Empty;
    }

    public class DojahBusinessData
    {
        public string? BusinessName { get; set; }
        public string? BusinessType { get; set; }
        public string? BusinessNumber { get; set; }
        public string? BusinessAddress { get; set; }
        public string? RegistrationDate { get; set; }
    }

    public class DojahBusinessIdData
    {
        public string ImageUrl { get; set; } = string.Empty;
        public string BusinessName { get; set; } = string.Empty;
        public string BusinessType { get; set; } = string.Empty;
        public string BusinessNumber { get; set; } = string.Empty;
        public string BusinessAddress { get; set; } = string.Empty;
        public string RegistrationDate { get; set; } = string.Empty;
    }

    public class DojahCountriesData
    {
        public DojahCountriesContent? Data { get; set; }
        public bool Status { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class DojahCountriesContent
    {
        public string Country { get; set; } = string.Empty;
    }

    public class DojahIndexData
    {
        public object? Data { get; set; }
        public bool Status { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class DojahAdditionalDocument
    {
        public string DocumentUrl { get; set; } = string.Empty;
        public string DocumentType { get; set; } = string.Empty;
    }

    public class DojahBvnEntity
    {
        public DojahEntityData? Entity { get; set; }
    }

    public class DojahNinEntity
    {
        public DojahEntityData? Entity { get; set; }
    }

    public class DojahEntityData
    {
        [JsonPropertyName("bvn")]
        public string Bvn { get; set; } = string.Empty;
        
        [JsonPropertyName("nin")]
        public string Nin { get; set; } = string.Empty;
        
        [JsonPropertyName("first_name")]
        public string FirstName { get; set; } = string.Empty;
        
        [JsonPropertyName("last_name")]
        public string LastName { get; set; } = string.Empty;
        
        [JsonPropertyName("middle_name")]
        public string MiddleName { get; set; } = string.Empty;
        
        [JsonPropertyName("date_of_birth")]
        public string DateOfBirth { get; set; } = string.Empty;
        
        [JsonPropertyName("phone_number")]
        public string PhoneNumber { get; set; } = string.Empty;
        
        [JsonPropertyName("phone_number1")]
        public string PhoneNumber1 { get; set; } = string.Empty;
        
        [JsonPropertyName("gender")]
        public string Gender { get; set; } = string.Empty;
        
        [JsonPropertyName("image")]
        public string Image { get; set; } = string.Empty;
        
        [JsonPropertyName("image_url")]
        public string ImageUrl { get; set; } = string.Empty;
    }

    public class DojahUserData
    {
        [JsonPropertyName("data")]
        public DojahUserDataContent? Data { get; set; }
        
        [JsonPropertyName("status")]
        public bool Status { get; set; }
        
        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;
    }

    public class DojahUserDataContent
    {
        [JsonPropertyName("dob")]
        public string Dob { get; set; } = string.Empty;
        
        [JsonPropertyName("last_name")]
        public string LastName { get; set; } = string.Empty;
        
        [JsonPropertyName("first_name")]
        public string FirstName { get; set; } = string.Empty;
        
        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;
    }

    public class DojahIdData
    {
        public DojahIdDataContent? Data { get; set; }
        public bool Status { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class DojahIdDataContent
    {
        public string IdUrl { get; set; } = string.Empty;
        public DojahIdDetails? IdData { get; set; }
        public string BackUrl { get; set; } = string.Empty;
    }

    public class DojahIdDetails
    {
        public string Extras { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string MrzStatus { get; set; } = string.Empty;
        public string DateIssued { get; set; } = string.Empty;
        public string ExpiryDate { get; set; } = string.Empty;
        public string MiddleName { get; set; } = string.Empty;
        public string Nationality { get; set; } = string.Empty;
        public string DateOfBirth { get; set; } = string.Empty;
        public string DocumentType { get; set; } = string.Empty;
        public string DocumentNumber { get; set; } = string.Empty;
    }

    public class DojahWebhookMetadata
    {
        [JsonPropertyName("user_id")]
        public string UserId { get; set; } = string.Empty;
        
        [JsonPropertyName("ipinfo")]
        public DojahIpInfo? IpInfo { get; set; }
        
        [JsonPropertyName("device_info")]
        public string DeviceInfo { get; set; } = string.Empty;
        
        [JsonPropertyName("user_type")]
        public string UserType { get; set; } = string.Empty;
        
        [JsonPropertyName("platform")]
        public string Platform { get; set; } = string.Empty;
        
        [JsonPropertyName("timestamp")]
        public string Timestamp { get; set; } = string.Empty;
    }

    public class DojahIpInfo
    {
        public string As { get; set; } = string.Empty;
        public string Isp { get; set; } = string.Empty;
        public double Lat { get; set; }
        public double Lon { get; set; }
        public string Org { get; set; } = string.Empty;
        public string Zip { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public bool Proxy { get; set; }
        public string Query { get; set; } = string.Empty;
        public bool Mobile { get; set; }
        public string Status { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public bool Hosting { get; set; }
        public string District { get; set; } = string.Empty;
        public string Timezone { get; set; } = string.Empty;
        public string RegionName { get; set; } = string.Empty;
    }

    public class DojahWebhookStatistics
    {
        public int TotalWebhooksReceived { get; set; }
        public int SuccessfulVerifications { get; set; }
        public int FailedVerifications { get; set; }
        public int PendingVerifications { get; set; }
        public DateTime LastWebhookReceived { get; set; }
        public TimeSpan AverageProcessingTime { get; set; }
    }

    public class DojahSystemHealth
    {
        public bool IsHealthy { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime LastChecked { get; set; }
        public Dictionary<string, object> HealthDetails { get; set; } = new Dictionary<string, object>();
    }

    public class DojahVerificationDataResponse
    {
        public bool Status { get; set; }
        public List<DojahVerificationEntry> Data { get; set; } = new List<DojahVerificationEntry>();
        public string Message { get; set; } = string.Empty;
        public DojahPaginationInfo? Pagination { get; set; }
    }

    public class DojahVerificationEntry
    {
        public string Id { get; set; } = string.Empty;
        public string ReferenceId { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string VerificationType { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DojahWebhookData? Data { get; set; }
        public object? RawData { get; set; }
    }

    public class DojahPaginationInfo
    {
        public int CurrentPage { get; set; }
        public int TotalPages { get; set; }
        public int TotalRecords { get; set; }
        public int PageSize { get; set; }
    }
}