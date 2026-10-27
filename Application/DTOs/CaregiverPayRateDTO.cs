using System;
using System.ComponentModel.DataAnnotations;

namespace Application.DTOs
{
    public class CaregiverPayRateDTO
    {
        public string Id { get; set; } = string.Empty;
        /// <summary>"AuxiliaryNurse" | "CHEW" | "RegisteredNurse".</summary>
        public string CaregiverType { get; set; } = string.Empty;
        /// <summary>"Junior" | "Mid" | "Senior".</summary>
        public string ExperienceTier { get; set; } = string.Empty;
        public decimal HourlyRate { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class AddCaregiverPayRateRequest
    {
        [Required]
        [RegularExpression("^(AuxiliaryNurse|CHEW|RegisteredNurse)$",
            ErrorMessage = "CaregiverType must be one of: AuxiliaryNurse, CHEW, RegisteredNurse")]
        public string CaregiverType { get; set; } = string.Empty;

        [Required]
        [RegularExpression("^(Junior|Mid|Senior)$",
            ErrorMessage = "ExperienceTier must be one of: Junior, Mid, Senior")]
        public string ExperienceTier { get; set; } = string.Empty;

        [Range(0, 100_000_000)]
        public decimal HourlyRate { get; set; }

        public bool IsActive { get; set; } = true;
    }

    /// <summary>
    /// Partial update — only non-null fields are applied, mirroring <see cref="UpdatePackageRequest"/>.
    /// Changing CaregiverType/ExperienceTier is intentionally not "partial" here: both are
    /// re-validated together against the existing pair whenever either is supplied, so a rate
    /// can't be left pointing at a nonsensical combination mid-update.
    /// </summary>
    public class UpdateCaregiverPayRateRequest
    {
        /// <summary>Populated from the route by the controller — not supplied in the request body.</summary>
        public string Id { get; set; } = string.Empty;

        [RegularExpression("^(AuxiliaryNurse|CHEW|RegisteredNurse)$",
            ErrorMessage = "CaregiverType must be one of: AuxiliaryNurse, CHEW, RegisteredNurse")]
        public string? CaregiverType { get; set; }

        [RegularExpression("^(Junior|Mid|Senior)$",
            ErrorMessage = "ExperienceTier must be one of: Junior, Mid, Senior")]
        public string? ExperienceTier { get; set; }

        [Range(0, 100_000_000)]
        public decimal? HourlyRate { get; set; }

        public bool? IsActive { get; set; }
    }
}
