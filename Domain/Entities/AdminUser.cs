using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Entities
{
    public class AdminUser
    {
        public ObjectId Id { get; set; }

        public string FirstName { get; set; }

        public string? MiddleName { get; set; }

        public string LastName { get; set; }

        public string Email { get; set; }

        public string? PhoneNo { get; set; }

        public string Password { get; set; }

        public string Role { get; set; }

        /// <summary>
        /// Department determines what data/operations this admin can access.
        /// SuperAdmin role bypasses department restrictions.
        /// </summary>
        public string? Department { get; set; }

        public string? Status { get; set; }

        public bool IsDeleted { get; set; }
        public DateTime? DeletedOn { get; set; }

        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// Valid admin departments. SuperAdmin has full access regardless of department.
    /// </summary>
    public static class AdminDepartments
    {
        public const string Finance = "Finance";
        public const string HR = "HR";
        public const string ComplianceAndLegal = "ComplianceAndLegal";
        public const string CareLeads = "CareLeads";
        public const string MarketingAndSales = "MarketingAndSales";

        public static readonly string[] All = new[]
        {
            Finance, HR, ComplianceAndLegal, CareLeads, MarketingAndSales
        };

        public static bool IsValid(string? department)
        {
            if (string.IsNullOrEmpty(department)) return true; // null is allowed (SuperAdmin or legacy)
            return All.Contains(department);
        }
    }
}
