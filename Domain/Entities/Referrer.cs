using MongoDB.Bson;

namespace Domain.Entities
{
    public class Referrer
    {
        public ObjectId Id { get; set; } = ObjectId.GenerateNewId();
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? PhoneNo { get; set; }

        /// <summary>
        /// Null on referrers created before the alias feature shipped.
        /// </summary>
        public string? Alias { get; set; }

        /// <summary>
        /// Null on referrers created before the approval-workflow status field existed.
        /// </summary>
        public string? Status { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public static class ReferrerStatus
    {
        public const string PendingApproval = "PendingApproval";
        public const string Approved = "Approved";
        public const string Rejected = "Rejected";
    }
}
