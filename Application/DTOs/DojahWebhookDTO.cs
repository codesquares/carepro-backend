using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.DTOs
{
    public class DojahWebhookRequest
    {
        public bool Status { get; set; }
        public string VerificationStatus { get; set; } = string.Empty;
        public string ReferenceId { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public DojahWebhookData? Data { get; set; }
        public DojahWebhookMetadata? Metadata { get; set; }
        public string IdType { get; set; } = string.Empty;
        public string VerificationType { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    public class DojahWebhookData
    {
        public DojahGovernmentData? GovernmentData { get; set; }
        public DojahUserData? UserData { get; set; }
        public DojahIdData? Id { get; set; }
    }

    public class DojahGovernmentData
    {
        public DojahBvnEntity? Bvn { get; set; }
        public DojahNinEntity? Nin { get; set; }
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
        public DojahEntityData? Data { get; set; }
    }

    public class DojahIdData
    {
        public DojahEntityData? Data { get; set; }
    }

    public class DojahWebhookMetadata
    {
        public string UserId { get; set; } = string.Empty;
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
}