using System;
using System.ComponentModel.DataAnnotations;

namespace Application.DTOs
{
    public class PackageDTO
    {
        public string Id { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string TierLabel { get; set; } = string.Empty;
        /// <summary>"AuxiliaryNurse" | "CHEW" | "RegisteredNurse".</summary>
        public string RequiredCaregiverType { get; set; } = string.Empty;
        public string? RequiredSpecialty { get; set; }
        public decimal BasePrice { get; set; }
        public decimal? AdditionalDayPrice { get; set; }
        /// <summary>"Hourly" | "Fixed" | null (legacy package predating Phase 9.2).</summary>
        public string? PayCalculationType { get; set; }
        /// <summary>Only set when PayCalculationType is "Fixed".</summary>
        public decimal? FixedCaregiverPay { get; set; }
        public string Description { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// Client-facing projection of an active <see cref="Domain.Entities.Package"/> — only
    /// what a client needs to browse and choose. Deliberately omits every operational /
    /// payroll-internal field on <see cref="PackageDTO"/>: <c>PayCalculationType</c>,
    /// <c>FixedCaregiverPay</c>, <c>IsActive</c> (this projection only ever contains active
    /// packages), and the <c>CreatedAt</c>/<c>UpdatedAt</c> audit timestamps.
    /// </summary>
    public class ClientPackageDTO
    {
        public string Id { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string TierLabel { get; set; } = string.Empty;
        /// <summary>Informational only — the client cannot filter or choose by this.
        /// "AuxiliaryNurse" | "CHEW" | "RegisteredNurse".</summary>
        public string RequiredCaregiverType { get; set; } = string.Empty;
        public string? RequiredSpecialty { get; set; }
        public string Description { get; set; } = string.Empty;
        public decimal BasePrice { get; set; }
        public decimal? AdditionalDayPrice { get; set; }
    }

    public class AddPackageRequest
    {
        [Required]
        [RegularExpression(@"^(Adult/Elder Care|Post-Partum Care|Post Surgery Care|Live-in Package)$",
            ErrorMessage = "Category must be one of: Adult/Elder Care, Post-Partum Care, Post Surgery Care, Live-in Package")]
        public string Category { get; set; } = string.Empty;

        [Required]
        [StringLength(80, MinimumLength = 1)]
        public string TierLabel { get; set; } = string.Empty;

        [Required]
        [RegularExpression("^(AuxiliaryNurse|CHEW|RegisteredNurse)$",
            ErrorMessage = "RequiredCaregiverType must be one of: AuxiliaryNurse, CHEW, RegisteredNurse")]
        public string RequiredCaregiverType { get; set; } = string.Empty;

        [StringLength(120)]
        public string? RequiredSpecialty { get; set; }

        [Range(0, 100_000_000)]
        public decimal BasePrice { get; set; }

        [Range(0, 100_000_000)]
        public decimal? AdditionalDayPrice { get; set; }

        [Required]
        [RegularExpression("^(Hourly|Fixed)$",
            ErrorMessage = "PayCalculationType must be one of: Hourly, Fixed")]
        public string PayCalculationType { get; set; } = string.Empty;

        /// <summary>Required when PayCalculationType is Fixed; must be omitted when Hourly.</summary>
        [Range(0, 100_000_000)]
        public decimal? FixedCaregiverPay { get; set; }

        [StringLength(2000)]
        public string? Description { get; set; }

        public bool IsActive { get; set; } = true;
    }

    /// <summary>
    /// Partial update — only non-null fields are applied, mirroring
    /// <see cref="UpdateTrainingMaterialRequest"/>.
    /// </summary>
    public class UpdatePackageRequest
    {
        /// <summary>Populated from the route by the controller — not supplied in the request body.</summary>
        public string Id { get; set; } = string.Empty;

        [RegularExpression(@"^(Adult/Elder Care|Post-Partum Care|Post Surgery Care|Live-in Package)$",
            ErrorMessage = "Category must be one of: Adult/Elder Care, Post-Partum Care, Post Surgery Care, Live-in Package")]
        public string? Category { get; set; }

        [StringLength(80, MinimumLength = 1)]
        public string? TierLabel { get; set; }

        [RegularExpression("^(AuxiliaryNurse|CHEW|RegisteredNurse)$",
            ErrorMessage = "RequiredCaregiverType must be one of: AuxiliaryNurse, CHEW, RegisteredNurse")]
        public string? RequiredCaregiverType { get; set; }

        /// <summary>
        /// Set to "" (empty/whitespace) to explicitly clear the specialty; null leaves it unchanged.
        /// </summary>
        [StringLength(120)]
        public string? RequiredSpecialty { get; set; }

        [Range(0, 100_000_000)]
        public decimal? BasePrice { get; set; }

        [Range(0, 100_000_000)]
        public decimal? AdditionalDayPrice { get; set; }

        [RegularExpression("^(Hourly|Fixed)$",
            ErrorMessage = "PayCalculationType must be one of: Hourly, Fixed")]
        public string? PayCalculationType { get; set; }

        /// <summary>
        /// Applied only if supplied. Switching PayCalculationType to Hourly clears this
        /// automatically regardless of what's passed; switching to (or staying) Fixed
        /// requires a positive value to already exist or be supplied here.
        /// </summary>
        [Range(0, 100_000_000)]
        public decimal? FixedCaregiverPay { get; set; }

        [StringLength(2000)]
        public string? Description { get; set; }

        public bool? IsActive { get; set; }
    }
}
