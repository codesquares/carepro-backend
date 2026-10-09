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

        public string? ServiceAddress { get; set; }
        /// <summary>True once the client has stamped their real device GPS via
        /// POST /contracts/{id}/service-location — before that, any coordinates on the
        /// contract are only a geocoded address, not real GPS.</summary>
        public bool? ServiceLocationSetByClient { get; set; }
        public DateTime? ServiceLocationSetAt { get; set; }

        // ── Cancellation window ──
        public DateTime? ContractSentAt { get; set; }
        public DateTime? CancellationWindowEndsAt { get; set; }
        public int CancellationWindowHours { get; set; }
        /// <summary>True while the client may still cancel (window not ended, not already cancelled).</summary>
        public bool CancellationWindowOpen { get; set; }
        public DateTime? CancelledAt { get; set; }
        public string? CancelledBy { get; set; }
        public DateTime? AcceptedByNonCancellationAt { get; set; }
        /// <summary>The acknowledgement text shown in the contract, email and client view (null when there is no window).</summary>
        public string? CancellationAcknowledgement { get; set; }
    }
}
