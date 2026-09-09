using System;

namespace Application.DTOs
{
    /// <summary>
    /// Phase 6 — a care agreement auto-generated from a Package + CarePro's standard
    /// terms when a PackageRequest reaches "confirmed". No negotiation, no per-client
    /// customisation beyond which package was bought.
    /// </summary>
    public class PackageContractDTO
    {
        public string Id { get; set; } = string.Empty;
        public string PackageRequestId { get; set; } = string.Empty;
        public string PackageId { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string CaregiverId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public decimal TotalAmount { get; set; }

        public string PackageCategory { get; set; } = string.Empty;
        public string PackageTierLabel { get; set; } = string.Empty;

        /// <summary>The rendered CarePro standard terms + package details (HTML).</summary>
        public string GeneratedTermsHtml { get; set; } = string.Empty;

        public DateTime ContractStartDate { get; set; }
        public DateTime ContractEndDate { get; set; }
        public DateTime CreatedAt { get; set; }

        /// <summary>True when this call created the contract; false when it already existed (idempotent).</summary>
        public bool NewlyGenerated { get; set; }
    }
}
