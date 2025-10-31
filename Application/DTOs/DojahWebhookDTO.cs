using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.DTOs
{
    public class DojahWebhookWrapper
    {
        public DojahWebhookRequest Request { get; set; } = new();
        public object? Data { get; set; }
    }

    public class DojahWebhookRequest
    {
        public bool Status { get; set; }
        public string VerificationStatus { get; set; } = string.Empty; // Ongoing, Abandoned, Completed, Pending, Failed
        public string ReferenceId { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public DojahWebhookData? Data { get; set; }
        public DojahWebhookMetadata? Metadata { get; set; }
        public string IdType { get; set; } = string.Empty;
        public string VerificationType { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string VerificationValue { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string IdUrl { get; set; } = string.Empty;
        public string BackUrl { get; set; } = string.Empty;
        public string SelfieUrl { get; set; } = string.Empty;
        public string VerificationUrl { get; set; } = string.Empty;
        public string VerificationMode { get; set; } = string.Empty;
        public DojahAmlData? Aml { get; set; }
    }

    public class DojahAmlData
    {
        public bool Status { get; set; }
    }

    public class DojahWebhookData
    {
        public DojahGovernmentData? GovernmentData { get; set; }
        public DojahUserData? UserData { get; set; }
        public DojahIdData? Id { get; set; }
        public DojahEmailData? Email { get; set; }
        public DojahSelfieData? Selfie { get; set; }
        public DojahPhoneData? PhoneNumber { get; set; }
        public DojahBusinessData? BusinessData { get; set; }
        public DojahBusinessIdData? BusinessId { get; set; }
        public DojahCountriesData? Countries { get; set; }
        public DojahIndexData? Index { get; set; }
        public List<DojahAdditionalDocument>? AdditionalDocument { get; set; }
    }

    public class DojahGovernmentData
    {
        public DojahGovernmentDataContent? Data { get; set; }
        public bool Status { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class DojahGovernmentDataContent
    {
        public DojahBvnEntity? Bvn { get; set; }
        public DojahNinEntity? Nin { get; set; }
    }

    public class DojahEmailData
    {
        public DojahEmailContent? Data { get; set; }
        public bool Status { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class DojahEmailContent
    {
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
        public string Bvn { get; set; } = string.Empty;
        public string Nin { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string MiddleName { get; set; } = string.Empty;
        public string DateOfBirth { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string PhoneNumber1 { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        public string Image { get; set; } = string.Empty;
    }

    public class DojahUserData
    {
        public DojahUserDataContent? Data { get; set; }
        public bool Status { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class DojahUserDataContent
    {
        public string Dob { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
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
        public string UserId { get; set; } = string.Empty;
        public DojahIpInfo? IpInfo { get; set; }
        public string DeviceInfo { get; set; } = string.Empty;
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