using MongoDB.Bson;
using System;
using System.Collections.Generic;

namespace Domain.Entities
{
    /// <summary>
    /// A pre-priced care package variant (Phase 3). Admin-managed pricing data —
    /// created and maintained through the admin CRUD endpoints, never seeded in
    /// migration code.
    ///
    /// <see cref="RequiredCaregiverType"/> is an explicit per-package field (reusing
    /// the Phase 2.1 <see cref="Domain.Entities.CaregiverType"/> enum), NOT derived
    /// from <see cref="TierLabel"/> — Post Surgery Care uses RegisteredNurse across
    /// all three of its tiers, which a tier→type formula could not express.
    /// </summary>
    public class Package
    {
        public ObjectId Id { get; set; }

        /// <summary>
        /// One of <see cref="PackageCategories"/>: "Adult/Elder Care",
        /// "Post-Partum Care", "Post Surgery Care", "Live-in Package".
        /// </summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>
        /// Free text (e.g. "Essential", "Standard", "Premium"). Deliberately not a
        /// strict enum — Post-Partum Care only uses two of the three usual tier
        /// names, so consistency is enforced at the admin-UI validation layer.
        /// </summary>
        public string TierLabel { get; set; } = string.Empty;

        public CaregiverType RequiredCaregiverType { get; set; }

        /// <summary>Nullable, e.g. "Midwifery". Same field concept as Phase 2.1's Caregiver.Specialty.</summary>
        public string? RequiredSpecialty { get; set; }

        public decimal BasePrice { get; set; }

        /// <summary>Only set for packages where the "extra days" add-on applies; otherwise null.</summary>
        public decimal? AdditionalDayPrice { get; set; }

        /// <summary>
        /// Explicit field (Phase 9.2) — not derived from Category/TierLabel, same
        /// convention as <see cref="RequiredCaregiverType"/>. Nullable because real
        /// Package documents created before Phase 9 predate this field, and the
        /// MongoDB EF Core provider rejects missing non-nullable properties on read.
        /// Every package should be given an explicit value going forward via the
        /// admin CRUD endpoints.
        /// </summary>
        public PayCalculationType? PayCalculationType { get; set; }

        /// <summary>
        /// Caregiver's flat pay for the engagement. Only meaningful — and only ever
        /// non-null — when <see cref="PayCalculationType"/> is Fixed; enforced by
        /// PackageService, not just documented, because this drives real payroll
        /// amounts.
        /// </summary>
        public decimal? FixedCaregiverPay { get; set; }

        public string Description { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>Allowed <see cref="Package.Category"/> values.</summary>
    public static class PackageCategories
    {
        public const string AdultElderCare = "Adult/Elder Care";
        public const string PostPartumCare = "Post-Partum Care";
        public const string PostSurgeryCare = "Post Surgery Care";
        public const string LiveInPackage = "Live-in Package";

        public static readonly IReadOnlyCollection<string> All = new[]
        {
            AdultElderCare, PostPartumCare, PostSurgeryCare, LiveInPackage
        };
    }

    /// <summary>How a package's caregiver pay is calculated (Phase 9.2).</summary>
    public enum PayCalculationType
    {
        Hourly = 0,
        Fixed = 1
    }
}
