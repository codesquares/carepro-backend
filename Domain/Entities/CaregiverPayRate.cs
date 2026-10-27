using MongoDB.Bson;
using System;

namespace Domain.Entities
{
    /// <summary>
    /// Admin-managed payroll rate table (Phase 9.3): the hourly rate paid for a given
    /// (<see cref="CaregiverType"/>, <see cref="ExperienceTier"/>) combination. Looked
    /// up by <c>Payroll</c> creation (Phase 9.7) for Hourly-<see cref="PayCalculationType"/>
    /// packages. Created and maintained through the admin CRUD endpoints, never seeded
    /// in migration code — same convention as <see cref="Package"/>.
    /// </summary>
    public class CaregiverPayRate
    {
        public ObjectId Id { get; set; }

        public CaregiverType CaregiverType { get; set; }

        public ExperienceTier ExperienceTier { get; set; }

        public decimal HourlyRate { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
